using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Common.Authentication.Okta;
using Common.Authentication.Okta.Browser;
using Common.Authentication.Okta.Claims;
using Common.Authentication.Okta.Tokens;
using Common.Authentication.Okta.Wpf;
using IdentityModel.OidcClient.Browser;
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
        private const string ApplicationId = "SsoDemo.Wpf";

        private OktaAuthenticationOptions? oktaOptions;
        private OktaSsoHost? ssoHost;

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

            // The Okta SSO ceremony (single-instance enforcement, custom-URI-scheme registration,
            // routing the browser's OAuth redirect back into this process) lives in the library —
            // see Common.Authentication.Okta.Wpf.OktaSsoHost.
            oktaOptions = OktaOptionsFactory.FromAppConfig();
            ssoHost = new OktaSsoHost(oktaOptions, ApplicationId);

            if (!ssoHost.TryStart(e.Args))
            {
                ssoHost.Dispose();
                ssoHost = null;
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
                ssoHost?.Dispose();
                ssoHost = null;
                Shutdown(1);
            }
        }

        protected override Window CreateShell() => Container.Resolve<MainWindow>();

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterInstance(ssoHost!.CallbackChannel);
            containerRegistry.RegisterInstance(oktaOptions!);

            containerRegistry.RegisterSingleton<IBrowser, SystemBrowser>();
            containerRegistry.RegisterSingleton<IAccessTokenValidator, OktaAccessTokenValidator>();
            containerRegistry.RegisterSingleton<IClaimsPrincipalFactory, OktaClaimsPrincipalFactory>();
            containerRegistry.RegisterSingleton<ITokenStore, DpapiTokenStore>();
            containerRegistry.RegisterSingleton<IOktaAuthenticationService, OktaAuthenticationService>();

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

            ssoHost!.Activate(MainWindow);

            // The Authenticating view drives the silent-then-browser sign-in flow on navigation.
            Container.Resolve<IRegionManager>().RequestNavigate(RegionNames.Content, ViewNames.Authenticating);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ssoHost?.Dispose();
            base.OnExit(e);
        }
    }
}
