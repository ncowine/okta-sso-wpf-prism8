using System;

namespace Common.Authentication.Okta.Browser
{
    /// <summary>Parameters for a browser-based authorization request.</summary>
    public sealed class BrowserOptions
    {
        public BrowserOptions(string startUrl, string endUrl)
        {
            StartUrl = startUrl ?? throw new ArgumentNullException(nameof(startUrl));
            EndUrl = endUrl ?? throw new ArgumentNullException(nameof(endUrl));
        }

        /// <summary>The authorization endpoint URL to open, including the query string.</summary>
        public string StartUrl { get; }

        /// <summary>The redirect URI the flow is expected to return to.</summary>
        public string EndUrl { get; }
    }
}
