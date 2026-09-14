using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <c>%LOCALAPPDATA%\{applicationId}\tokens.dat</c>.
    /// </summary>
    public sealed class DpapiTokenStore : ITokenStore
    {
        private readonly byte[] entropy;
        private readonly string filePath;
        private readonly SemaphoreSlim gate = new(1, 1);

        /// <param name="applicationId">
        /// The same stable, per-application identifier passed to
        /// <see cref="Wpf.OktaSsoHost"/> (e.g. <c>"MyCompany.MyApp"</c>). Scopes both the storage
        /// folder and the DPAPI entropy so two host apps on the same machine/user never share or
        /// collide on the same token file.
        /// </param>
        public DpapiTokenStore(string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                throw new ArgumentException("An application id is required.", nameof(applicationId));
            }

            entropy = Encoding.UTF8.GetBytes($"{applicationId}.Okta.TokenStore.v1");

            var folderName = Sanitize(applicationId);
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                folderName);
            Directory.CreateDirectory(directory);
            filePath = Path.Combine(directory, "tokens.dat");
        }

        private static string Sanitize(string applicationId)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = applicationId.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var sanitized = new string(chars);
            if (sanitized == applicationId)
            {
                return sanitized;
            }

            // applicationId contained characters invalid in a folder name; two different ids could
            // sanitize to the same string (e.g. "Team:App" and "Team/App" both -> "Team_App"), so
            // append a short hash of the original to keep them from colliding on disk.
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(applicationId));
            return sanitized + "_" + BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty);
        }

        public async Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(tokens);
            var protectedBytes = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);

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

                var plaintext = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
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
