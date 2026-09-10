using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Common.Authentication.Okta;
using Common.Authentication.Okta.Browser;
using Common.Authentication.Okta.Claims;
using Common.Authentication.Okta.Platform;
using Common.Authentication.Okta.Tokens;
using IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;
using SsoDemo.Wpf.Services;
using SsoDemo.Wpf.ViewModels;
using SsoDemo.Wpf.Views;

namespace SsoDemo.Wpf
{
    /// <summary>Prism + DryIoc application host.</summary>
    public partial class App : PrismApplication
    {
        private const string SingleInstanceId = "SsoDemo.Wpf";

        private ISingleInstanceCoordinator? singleInstance;
        private string customUriScheme = "app";
        private string? activationArgument;

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                Debug.WriteLine($"[App] Unhandled UI exception: {args.Exception}");
                MessageBox.Show(args.Exception.Message, "Okta SSO Demo — error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Debug.WriteLine($"[App] Unhandled exception: {args.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Debug.WriteLine($"[App] Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };

            if (Environment.GetEnvironmentVariable("SSODEMO_TRACE_FILE") is { Length: > 0 } traceFile)
            {
                System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(traceFile));
                System.Diagnostics.Trace.AutoFlush = true;
            }

            customUriScheme = ConfigurationManager.AppSettings["Okta:CustomUriScheme"] ?? "app";
            activationArgument = e.Args.FirstOrDefault(IsSchemeActivation);

            singleInstance = new NamedPipeSingleInstanceCoordinator(SingleInstanceId);

            if (!singleInstance.IsPrimaryInstance)
            {
                if (activationArgument is not null)
                {
                    Debug.WriteLine("[App] Secondary instance: forwarding OAuth callback to the running app.");
                    // Let the running instance pull itself to the foreground once it has the callback.
                    ForegroundWindow.GrantToRunningInstance();
                    singleInstance.SignalPrimary(activationArgument);
                    Thread.Sleep(250);
                }
                else
                {
                    Debug.WriteLine("[App] Another instance is already running; exiting.");
                }

                singleInstance.Dispose();
                singleInstance = null;
                Shutdown();
                return;
            }

            try
            {
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Startup failed: {ex}");
                MessageBox.Show(
                    ex.Message,
                    "Okta SSO Demo — startup error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                singleInstance?.Dispose();
                singleInstance = null;
                Shutdown(1);
            }
        }

        protected override Window CreateShell() => Container.Resolve<MainWindow>();

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            var options = OktaOptionsFactory.FromAppConfig();
            containerRegistry.RegisterInstance(options);

            var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddDebug());
            containerRegistry.RegisterInstance<ILoggerFactory>(loggerFactory);

            containerRegistry.RegisterSingleton<IBrowserCallbackChannel, BrowserCallbackChannel>();
            containerRegistry.RegisterSingleton<IBrowser, SystemBrowser>();
            containerRegistry.RegisterSingleton<IAccessTokenValidator, OktaAccessTokenValidator>();
            containerRegistry.RegisterSingleton<IClaimsPrincipalFactory, OktaClaimsPrincipalFactory>();
            containerRegistry.RegisterSingleton<ITokenStore, DpapiTokenStore>();
            containerRegistry.RegisterSingleton<IOktaAuthenticationService, OktaAuthenticationService>();
            containerRegistry.RegisterSingleton<ICustomUriSchemeRegistrar, HkcuCustomUriSchemeRegistrar>();

            containerRegistry.RegisterInstance(new DemoApiOptions
            {
                BaseUrl = Environment.GetEnvironmentVariable("SSO_API_BASEURL")
                    ?? ConfigurationManager.AppSettings["Api:BaseUrl"]
                    ?? "http://localhost:5006",
                BaseUrlB = Environment.GetEnvironmentVariable("SSO_API_BASEURL_B")
                    ?? ConfigurationManager.AppSettings["Api:BaseUrlB"]
                    ?? "http://localhost:5007",
            });
            containerRegistry.RegisterSingleton<IDemoApiClient, DemoApiClient>();
            containerRegistry.RegisterSingleton<IDemoApiBClient, DemoApiBClient>();

            containerRegistry.RegisterForNavigation<AuthenticatingView, AuthenticatingViewModel>(ViewNames.Authenticating);
            containerRegistry.RegisterForNavigation<WelcomeView, WelcomeViewModel>(ViewNames.Welcome);
            containerRegistry.RegisterForNavigation<SignedOutView, SignedOutViewModel>(ViewNames.SignedOut);
            containerRegistry.RegisterForNavigation<AccessDeniedView, AccessDeniedViewModel>(ViewNames.AccessDenied);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            Container.Resolve<ICustomUriSchemeRegistrar>().EnsureRegistered();

            var callbackChannel = Container.Resolve<IBrowserCallbackChannel>();
            singleInstance!.StartListening(argument =>
            {
                if (!IsSchemeActivation(argument))
                {
                    return;
                }

                Debug.WriteLine("[App] Routing forwarded OAuth callback to the browser channel.");
                callbackChannel.Publish(argument);
                Dispatcher.BeginInvoke(new Action(() => ForegroundWindow.Bring(MainWindow)));
            });

            if (activationArgument is not null)
            {
                callbackChannel.Publish(activationArgument);
                ForegroundWindow.Bring(MainWindow);
            }

            // The Authenticating view drives the silent-then-browser sign-in flow on navigation.
            Container.Resolve<IRegionManager>().RequestNavigate(RegionNames.Content, ViewNames.Authenticating);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            singleInstance?.Dispose();
            base.OnExit(e);
        }

        private bool IsSchemeActivation(string? argument) =>
            argument is not null &&
            argument.StartsWith(customUriScheme + ":", StringComparison.OrdinalIgnoreCase);
    }
}
