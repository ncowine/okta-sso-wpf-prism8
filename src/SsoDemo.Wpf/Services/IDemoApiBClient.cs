using System.Threading;
using System.Threading.Tasks;

namespace SsoDemo.Wpf.Services
{
    /// <summary>Calls the third-party API (<c>tools/DemoApiB</c>) directly with the user's token.</summary>
    public interface IDemoApiBClient
    {
        string BaseUrl { get; }

        /// <summary><c>GET /b/resource</c> — direct call; DemoApiB sees the user, no actor.</summary>
        Task<string> GetResourceAsync(CancellationToken cancellationToken = default);
    }
}
