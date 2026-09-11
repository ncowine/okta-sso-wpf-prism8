using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Tokens
{
    /// <summary>
    /// Persists the <see cref="TokenSet"/> as JSON encrypted with Windows DPAPI
    /// (<see cref="DataProtectionScope.CurrentUser"/>) under
    /// <c>%LOCALAPPDATA%\SsoDemo\tokens.dat</c>.
    /// </summary>
    public sealed class DpapiTokenStore : ITokenStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SsoDemo.Okta.TokenStore.v1");

        private readonly string filePath;
        private readonly SemaphoreSlim gate = new(1, 1);

        public DpapiTokenStore()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SsoDemo");
            Directory.CreateDirectory(directory);
            filePath = Path.Combine(directory, "tokens.dat");
        }

        public async Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(tokens);
            var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // FileStream.WriteAsync rather than File.WriteAllBytesAsync (not available on
                // .NET Framework) so this works identically on every target of this library.
                using (var stream = new FileStream(
                    filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await stream.WriteAsync(protectedBytes, 0, protectedBytes.Length, cancellationToken).ConfigureAwait(false);
                }

                Debug.WriteLine($"[DpapiTokenStore] Saved token set to {filePath}.");
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<TokenSet?> LoadAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                byte[] protectedBytes;
                using (var stream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    protectedBytes = new byte[stream.Length];
                    var offset = 0;
                    while (offset < protectedBytes.Length)
                    {
                        var read = await stream.ReadAsync(protectedBytes, offset, protectedBytes.Length - offset, cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        offset += read;
                    }
                }

                var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<TokenSet>(plaintext);
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                Debug.WriteLine($"[DpapiTokenStore] Could not read stored tokens, treating as signed out: {ex.Message}");
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Debug.WriteLine("[DpapiTokenStore] Cleared stored tokens.");
                }
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[DpapiTokenStore] Failed to delete token file: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
