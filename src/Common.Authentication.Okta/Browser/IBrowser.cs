using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Browser
{
    /// <summary>
    /// Launches the authorization request and waits for the redirect back to the app.
    /// Implemented by <see cref="SystemBrowser"/> for this demo (OS default browser + custom
    /// URI scheme), but the OIDC client only depends on this abstraction.
    /// </summary>
    public interface IBrowser
    {
        Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default);
    }
}
