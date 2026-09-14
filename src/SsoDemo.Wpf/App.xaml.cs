using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Common.Authentication.Okta;
using Common.Authentication.Okta.Wpf;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;
using SsoDemo.Wpf.Services;
using SsoDemo.Wpf.ViewModels;
using SsoDemo.Wpf.Views;

namespace SsoDemo.Wpf
{
    /// <summary>
    /// Prism + DryIoc application host. Owns the whole sign-in gate: the shell is never created
    /// or shown until <see cref="OnInitialized"/> has confirmed the user is authenticated. There
    /// is no in-app sign-out — closing the app (Exit) is the only way out.
    /// </summary>
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

            if (ConfigurationManager.AppSettings["Diagnostics:TraceFile"] is { Length: > 0 } traceFile)
            {
                Trace.Listeners.Add(new TextWriterTraceListener(traceFile));
                Trace.AutoFlush = true;
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

        // The shell is gated behind sign-in (see OnInitialized) — Prism must not create or show
        // it as part of its normal startup pipeline.
        protected override Window? CreateShell() => null;

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.AddOktaAuthentication(oktaOptions!, ssoHost!);

            containerRegistry.RegisterInstance(new DemoApiOptions
            {
                BaseUrl = ConfigurationManager.AppSettings["Api:BaseUrl"] ?? "http://localhost:5006",
                BaseUrlB = ConfigurationManager.AppSettings["Api:BaseUrlB"] ?? "http://localhost:5007",
            });
            containerRegistry.RegisterSingleton<IDemoApiClient, DemoApiClient>();
            containerRegistry.RegisterSingleton<IDemoApiBClient, DemoApiBClient>();

            containerRegistry.RegisterForNavigation<WelcomeView, WelcomeViewModel>(ViewNames.Welcome);
        }

        protected override async void OnInitialized()
        {
            base.OnInitialized();

            // Scheme registration + callback routing must be armed before sign-in starts; no window
            // needs to exist yet — the shell isn't even created until sign-in succeeds.
            ssoHost!.Activate();

            // No sign-in UI: silent refresh first, then the browser flow, entirely headless.
            var authentication = Container.Resolve<IOktaAuthenticationService>();
            var result = await authentication.TrySignInSilentAsync();
            if (!result.Success)
            {
                result = await authentication.SignInInteractiveAsync();
            }

            if (!result.Success)
            {
                Debug.WriteLine($"[App] Sign-in failed; exiting without showing the shell: {result.Error}");
                MessageBox.Show(
                    "You don't have permission to access this application.\n\n" + result.Error,
                    "Okta SSO Demo — access denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var shell = Container.Resolve<MainWindow>();

            // CreateShell() returned null so Prism's own Initialize() never ran the shell-specific
            // RegionManager wiring it normally does right after creating it; do it ourselves.
            var regionManager = Container.Resolve<IRegionManager>();
            RegionManager.SetRegionManager(shell, regionManager);
            RegionManager.UpdateRegions();

            MainWindow = shell;
            regionManager.RequestNavigate(RegionNames.Content, ViewNames.Welcome);
            shell.Show();
            ssoHost.AttachShell(shell);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ssoHost?.Dispose();
            base.OnExit(e);
        }
    }
}
