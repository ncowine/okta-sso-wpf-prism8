using System;
using Common.Authentication.Okta.Browser;
using Common.Authentication.Okta.Claims;
using Common.Authentication.Okta.Tokens;
using Common.Authentication.Okta.Wpf;
using IdentityModel.OidcClient.Browser;
using Prism.Ioc;

namespace Common.Authentication.Okta
{
    /// <summary>
    /// Registers every type this library needs against a Prism <see cref="IContainerRegistry"/>, so
    /// a host app wires up Okta sign-in with one call instead of re-registering each piece by hand.
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers <see cref="OktaAuthenticationOptions"/>, the browser/token/claims/validator
        /// pipeline and <see cref="IOktaAuthenticationService"/>.
        /// </summary>
        /// <param name="containerRegistry">The host app's container registry.</param>
        /// <param name="options">
        /// Typically <see cref="OktaOptionsFactory.FromAppConfig"/>. Registered as a singleton instance.
        /// </param>
        /// <param name="ssoHost">
        /// The <see cref="OktaSsoHost"/> constructed and started in <c>OnStartup</c>. Its
        /// <see cref="OktaSsoHost.CallbackChannel"/> is registered as <see cref="IBrowserCallbackChannel"/>,
        /// and its <see cref="OktaSsoHost.ApplicationId"/> scopes the DPAPI token store — the same
        /// application id used for single-instance/custom-scheme registration is reused here so the
        /// two never drift apart.
        /// </param>
        public static IContainerRegistry AddOktaAuthentication(
            this IContainerRegistry containerRegistry,
            OktaAuthenticationOptions options,
            OktaSsoHost ssoHost)
        {
            if (containerRegistry is null)
            {
                throw new ArgumentNullException(nameof(containerRegistry));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (ssoHost is null)
            {
                throw new ArgumentNullException(nameof(ssoHost));
            }

            containerRegistry.RegisterInstance(options);
            containerRegistry.RegisterInstance(ssoHost.CallbackChannel);

            containerRegistry.RegisterSingleton<IBrowser, SystemBrowser>();
            containerRegistry.RegisterSingleton<IAccessTokenValidator, OktaAccessTokenValidator>();
            containerRegistry.RegisterSingleton<IClaimsPrincipalFactory, OktaClaimsPrincipalFactory>();
            containerRegistry.RegisterInstance<ITokenStore>(new DpapiTokenStore(ssoHost.ApplicationId));
            containerRegistry.RegisterSingleton<IOktaAuthenticationService, OktaAuthenticationService>();

            return containerRegistry;
        }
    }
}
