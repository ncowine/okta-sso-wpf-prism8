using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using Common.Authentication.Okta;

namespace SsoDemo.Wpf.Infrastructure
{
    /// <summary>
    /// Builds <see cref="OktaAuthenticationOptions"/> from <c>App.config</c> &lt;appSettings&gt;.
    /// Any <c>Okta:Name</c> setting can be overridden by the environment variable
    /// <c>SSO_OKTA_NAME</c> (used by <c>scripts/run-demo.ps1</c> to point the app at the dummy IdP).
    /// </summary>
    public static class OktaOptionsFactory
    {
        public static OktaAuthenticationOptions FromAppConfig()
        {
            var settings = ConfigurationManager.AppSettings;

            var options = new OktaAuthenticationOptions
            {
                OktaDomain = Required(settings, "Okta:OktaDomain"),
                AuthorizationServerId = Optional(settings, "Okta:AuthorizationServerId", "default"),
                ClientId = Required(settings, "Okta:ClientId"),
                Audience = Optional(settings, "Okta:Audience", "api://default"),
                RedirectUri = Optional(settings, "Okta:RedirectUri", "app://auth/callback"),
                PostLogoutRedirectUri = Optional(settings, "Okta:PostLogoutRedirectUri", "app://auth/callback"),
                Scope = Optional(settings, "Okta:Scope", "openid profile email offline_access"),
                CustomUriScheme = Optional(settings, "Okta:CustomUriScheme", "app"),
                ClockSkew = TimeSpan.FromSeconds(OptionalDouble(settings, "Okta:ClockSkewSeconds", 60)),
                ValidateTokenClientId = OptionalBool(settings, "Okta:ValidateTokenClientId", true),
                AllowInsecureHttp = OptionalBool(settings, "Okta:AllowInsecureHttp", false),
                NameSourceClaim = Optional(settings, "Okta:NameSourceClaim", "sub"),
                EmailSourceClaim = Optional(settings, "Okta:EmailSourceClaim", "SAMAccount"),
                EmployeeIdSourceClaim = Optional(settings, "Okta:EmployeeIdSourceClaim", "empID"),
            };

            options.Validate();
            Debug.WriteLine(
                $"[OktaOptionsFactory] Loaded options. Authority='{options.Authority}', " +
                $"RedirectUri='{options.RedirectUri}', AllowInsecureHttp={options.AllowInsecureHttp}.");

            if (options.AllowInsecureHttp)
            {
                Debug.WriteLine("[OktaOptionsFactory] WARNING: insecure http transport is enabled (dummy IdP / dev only).");
            }

            return options;
        }

        private static string EnvName(string key) => "SSO_" + key.Replace(':', '_').ToUpperInvariant();

        private static string? Raw(NameValueCollection settings, string key)
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvName(key));
            var value = string.IsNullOrWhiteSpace(fromEnv) ? settings[key] : fromEnv;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Required(NameValueCollection settings, string key)
        {
            var value = Raw(settings, key);
            if (value is null ||
                value.StartsWith("YOUR-", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("XXXX"))
            {
                throw new ConfigurationErrorsException(
                    $"'{key}' is missing or still set to a placeholder value " +
                    $"(set it in App.config or via the {EnvName(key)} environment variable).");
            }

            return value;
        }

        private static string Optional(NameValueCollection settings, string key, string fallback) =>
            Raw(settings, key) ?? fallback;

        private static bool OptionalBool(NameValueCollection settings, string key, bool fallback) =>
            bool.TryParse(Raw(settings, key), out var parsed) ? parsed : fallback;

        private static double OptionalDouble(NameValueCollection settings, string key, double fallback) =>
            double.TryParse(Raw(settings, key), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }
}
