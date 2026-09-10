using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta;

namespace SsoDemo.Wpf.Services
{
    /// <summary>Attaches the current Okta access token as a bearer header to every outgoing request.</summary>
    public sealed class BearerTokenHandler : DelegatingHandler
    {
        private readonly IOktaAuthenticationService authentication;

        public BearerTokenHandler(IOktaAuthenticationService authentication)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await authentication.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                Debug.WriteLine("[BearerTokenHandler] No access token available for the request.");
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
