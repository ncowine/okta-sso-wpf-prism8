using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using Common.Authentication.Okta.Browser;
using Common.Authentication.Okta.Platform;

namespace Common.Authentication.Okta.Wpf
{
    /// <summary>
    /// Bundles the OAuth-redirect plumbing every WPF host of this library needs: single-instance
    /// enforcement, custom-URI-scheme registration, and routing the browser's redirect back into the
    /// running app (forwarding activation from a secondary launch, bringing the window forward).
    /// This is the one piece of the flow that's genuinely WPF-shaped ceremony rather than plain
    /// sign-in logic, so it lives here instead of being copied into every host app's App.xaml.cs.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override void OnStartup(StartupEventArgs e)
    /// {
    ///     var options = MyOptionsFactory.Load();
    ///     ssoHost = new OktaSsoHost(options, "MyApp");
    ///
    ///     if (!ssoHost.TryStart(e.Args))
    ///     {
    ///         ssoHost.Dispose();
    ///         Shutdown();
    ///         return;
    ///     }
    ///
    ///     base.OnStartup(e); // build the container; register ssoHost.CallbackChannel as IBrowserCallbackChannel
    /// }
    ///
    /// protected override void OnInitialized()
    /// {
    ///     base.OnInitialized();
    ///     ssoHost!.Activate(MainWindow);
    /// }
    ///
    /// protected override void OnExit(ExitEventArgs e)
    /// {
    ///     ssoHost?.Dispose();
    ///     base.OnExit(e);
    /// }
    /// </code>
    /// </example>
    public sealed class OktaSsoHost : IDisposable
    {
        private readonly OktaAuthenticationOptions options;
        private readonly ISingleInstanceCoordinator singleInstance;
        private readonly ICustomUriSchemeRegistrar schemeRegistrar;
        private string? activationArgument;
        private bool disposed;

        /// <param name="options">The same options passed to <see cref="OktaAuthenticationService"/>.</param>
        /// <param name="applicationId">
        /// A stable, per-application identifier used to scope the single-instance mutex/pipe name
        /// (e.g. <c>"MyCompany.MyApp"</c>). Must be unique to this application on the machine.
        /// </param>
        /// <param name="callbackChannel">
        /// Defaults to a new <see cref="BrowserCallbackChannel"/>. Register whatever instance you pass
        /// here (or <see cref="CallbackChannel"/> if you let this construct one) as <see cref="IBrowserCallbackChannel"/>
        /// in your DI container, since <see cref="Common.Authentication.Okta.Browser.SystemBrowser"/> needs the same instance.
        /// </param>
        /// <param name="singleInstanceCoordinator">Defaults to a new <see cref="NamedPipeSingleInstanceCoordinator"/>. Overridable for testing.</param>
        /// <param name="schemeRegistrar">Defaults to a new <see cref="HkcuCustomUriSchemeRegistrar"/>. Overridable for testing.</param>
        public OktaSsoHost(
            OktaAuthenticationOptions options,
            string applicationId,
            IBrowserCallbackChannel? callbackChannel = null,
            ISingleInstanceCoordinator? singleInstanceCoordinator = null,
            ICustomUriSchemeRegistrar? schemeRegistrar = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                throw new ArgumentException("An application id is required.", nameof(applicationId));
            }

            CallbackChannel = callbackChannel ?? new BrowserCallbackChannel();
            singleInstance = singleInstanceCoordinator ?? new NamedPipeSingleInstanceCoordinator(applicationId);
            this.schemeRegistrar = schemeRegistrar ?? new HkcuCustomUriSchemeRegistrar(options);
        }

        /// <summary>
        /// The channel the OAuth redirect is delivered through. Register this same instance as
        /// <see cref="IBrowserCallbackChannel"/> in your DI container.
        /// </summary>
        public IBrowserCallbackChannel CallbackChannel { get; }

        /// <summary>
        /// Determines whether this process is the primary instance and, if not, forwards a pending
        /// OAuth callback to it. Call from <c>OnStartup</c>, before building the DI container / calling
        /// <c>base.OnStartup</c>.
        /// </summary>
        /// <returns>
        /// <c>true</c> if this is the primary instance and startup should continue; <c>false</c> if
        /// another instance is already running, in which case the caller must dispose this host and
        /// shut down without doing any further startup work.
        /// </returns>
        public bool TryStart(string[] commandLineArgs)
        {
            activationArgument = commandLineArgs?.FirstOrDefault(IsSchemeActivation);

            if (singleInstance.IsPrimaryInstance)
            {
                return true;
            }

            if (activationArgument is not null)
            {
                Debug.WriteLine("[OktaSsoHost] Secondary instance: forwarding OAuth callback to the running app.");
                // Let the running instance pull itself to the foreground once it has the callback.
                ForegroundWindow.GrantToRunningInstance();
                singleInstance.SignalPrimary(activationArgument);
                Thread.Sleep(250);
            }
            else
            {
                Debug.WriteLine("[OktaSsoHost] Another instance is already running; exiting.");
            }

            singleInstance.Dispose();
            return false;
        }

        /// <summary>
        /// Registers the custom URI scheme, starts listening for callbacks forwarded from secondary
        /// instances, and delivers a callback this launch already carried. Call from
        /// <c>OnInitialized</c>, once <paramref name="mainWindow"/> exists.
        /// </summary>
        public void Activate(Window mainWindow)
        {
            if (mainWindow is null)
            {
                throw new ArgumentNullException(nameof(mainWindow));
            }

            schemeRegistrar.EnsureRegistered();

            singleInstance.StartListening(argument =>
            {
                if (!IsSchemeActivation(argument))
                {
                    return;
                }

                Debug.WriteLine("[OktaSsoHost] Routing forwarded OAuth callback to the browser channel.");
                CallbackChannel.Publish(argument);
                mainWindow.Dispatcher.BeginInvoke(new Action(() => ForegroundWindow.Bring(mainWindow)));
            });

            if (activationArgument is not null)
            {
                CallbackChannel.Publish(activationArgument);
                ForegroundWindow.Bring(mainWindow);
            }
        }

        private bool IsSchemeActivation(string? argument) =>
            argument is not null &&
            argument.StartsWith(options.CustomUriScheme + ":", StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            singleInstance.Dispose();
        }
    }
}
