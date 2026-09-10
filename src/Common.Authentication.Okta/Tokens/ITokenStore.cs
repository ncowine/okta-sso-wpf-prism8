using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Tokens
{
    /// <summary>Secure, per-user persistence for the current <see cref="TokenSet"/>.</summary>
    public interface ITokenStore
    {
        Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default);

        Task<TokenSet?> LoadAsync(CancellationToken cancellationToken = default);

        Task ClearAsync(CancellationToken cancellationToken = default);
    }
}
