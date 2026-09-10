using System.Threading;
using System.Threading.Tasks;

namespace SsoDemo.Wpf.Services
{
    /// <summary>Calls the token-protected demo API (<c>tools/DemoApi</c>).</summary>
    public interface IDemoApiClient
    {
        /// <summary>Base URL the client targets (for display).</summary>
        string BaseUrl { get; }

        /// <summary><c>GET /api/profile</c> — the API's view of the caller's token.</summary>
        Task<string> GetProfileAsync(CancellationToken cancellationToken = default);

        /// <summary><c>GET /api/orders</c> — fake data keyed off the token identity.</summary>
        Task<string> GetOrdersAsync(CancellationToken cancellationToken = default);

        /// <summary><c>GET /api/partner</c> — DemoApi exchanges the token (RFC 8693) and calls DemoApiB.</summary>
        Task<string> GetPartnerViaExchangeAsync(CancellationToken cancellationToken = default);
    }
}
