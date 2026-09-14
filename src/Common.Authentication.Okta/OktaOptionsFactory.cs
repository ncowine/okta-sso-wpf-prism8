using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace Common.Authentication.Okta
{
    /// <summary>
    /// Builds <see cref="OktaAuthenticationOptions"/> from a host app's <c>App.config</c>
    /// &lt;appSettings&gt; — shared here so every host app wires up Okta options the same way
    /// instead of re-implementing the appSettings/placeholder-detection ceremony per client.
    /// </summary>
    public static class OktaOptionsFactory
    {
        public static OktaAuthenticationOptions FromAppConfig()
        {
            var settings = ConfigurationManager.AppSettings;

            // Defaults for every optional setting live once, on the options class itself — start
            // from a fresh instance and only overwrite what App.config actually specifies, rather
            // than restating each default here too.
            var options = new OktaAuthenticationOptions
            {
                OktaDomain = Required(settings, "Okta:OktaDomain"),
                ClientId = Required(settings, "Okta:ClientId"),
            };

            options.AuthorizationServerId = Optional(settings, "Okta:AuthorizationServerId", options.AuthorizationServerId);
            options.Audience = Optional(settings, "Okta:Audience", options.Audience);
            options.RedirectUri = Optional(settings, "Okta:RedirectUri", options.RedirectUri);
            options.PostLogoutRedirectUri = Optional(settings, "Okta:PostLogoutRedirectUri", options.PostLogoutRedirectUri);
            options.Scope = Optional(settings, "Okta:Scope", options.Scope);
            options.CustomUriScheme = Optional(settings, "Okta:CustomUriScheme", options.CustomUriScheme);
            options.ClockSkew = TimeSpan.FromSeconds(
                OptionalDouble(settings, "Okta:ClockSkewSeconds", options.ClockSkew.TotalSeconds));
            options.ValidateTokenClientId = OptionalBool(settings, "Okta:ValidateTokenClientId", options.ValidateTokenClientId);
            options.AllowInsecureHttp = OptionalBool(settings, "Okta:AllowInsecureHttp", options.AllowInsecureHttp);
            options.NameSourceClaim = Optional(settings, "Okta:NameSourceClaim", options.NameSourceClaim);
            options.EmailSourceClaim = Optional(settings, "Okta:EmailSourceClaim", options.EmailSourceClaim);
            options.EmployeeIdSourceClaim = Optional(settings, "Okta:EmployeeIdSourceClaim", options.EmployeeIdSourceClaim);

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

        private static string? Raw(NameValueCollection settings, string key)
        {
            var value = settings[key];
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
                    $"'{key}' is missing or still set to a placeholder value in App.config.");
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
