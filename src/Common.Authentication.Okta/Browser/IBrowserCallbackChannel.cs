using System;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Browser
{
    /// <summary>
    /// Bridges the custom-scheme redirect (delivered to the running instance by the OS / single-instance
    /// coordinator) back to the in-flight OIDC browser request.
    /// </summary>
    public interface IBrowserCallbackChannel
    {
        /// <summary>Called by the host when a <c>{scheme}://...</c> activation URI arrives.</summary>
        void Publish(string callbackUri);

        /// <summary>Awaited by the browser until a matching callback URI is published.</summary>
        Task<string> WaitForCallbackAsync(CancellationToken cancellationToken);
    }
}
