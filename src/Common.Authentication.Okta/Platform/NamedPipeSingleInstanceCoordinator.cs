using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Platform
{
    /// <summary>
    /// Single-instance coordination using a named <see cref="Mutex"/> for ownership and a
    /// <see cref="NamedPipeServerStream"/> for delivering activation arguments to the primary instance.
    /// All names are per-user.
    /// </summary>
    public sealed class NamedPipeSingleInstanceCoordinator : ISingleInstanceCoordinator
    {
        private readonly string mutexName;
        private readonly string pipeName;
        private readonly Mutex mutex;
        private readonly bool ownsMutex;

        private CancellationTokenSource? listenerCts;
        private Task? listenerTask;
        private bool disposed;

        public NamedPipeSingleInstanceCoordinator(string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                throw new ArgumentException("Application id is required.", nameof(applicationId));
            }

            var scopedId = Scope(applicationId);
            mutexName = $@"Local\{scopedId}.mutex";
            pipeName = $"{scopedId}.pipe";

            mutex = new Mutex(initiallyOwned: true, mutexName, out ownsMutex);
            Debug.WriteLine($"[SingleInstance] IsPrimaryInstance={ownsMutex} (mutex '{mutexName}').");
        }

        public bool IsPrimaryInstance => ownsMutex;

        public void StartListening(Action<string> onActivationArgumentReceived)
        {
            if (onActivationArgumentReceived is null)
            {
                throw new ArgumentNullException(nameof(onActivationArgumentReceived));
            }

            if (!ownsMutex)
            {
                throw new InvalidOperationException("Only the primary instance can listen for activations.");
            }

            if (listenerTask is not null)
            {
                return;
            }

            listenerCts = new CancellationTokenSource();
            listenerTask = Task.Run(() => ListenLoopAsync(onActivationArgumentReceived, listenerCts.Token));
        }

        public void SignalPrimary(string argument)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(nameof(argument));
            }

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(3000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine(argument);
                Debug.WriteLine("[SingleInstance] Forwarded activation argument to primary instance.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Failed to signal primary instance: {ex.Message}");
            }
        }

        private async Task ListenLoopAsync(Action<string> callback, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Debug.WriteLine($"[SingleInstance] Listener: awaiting a connection on pipe '{pipeName}'.");
                    using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    using var reader = new StreamReader(server, new UTF8Encoding(false));
                    // The CancellationToken overload of ReadLineAsync isn't available on .NET
                    // Framework; WaitForConnectionAsync above already gates the loop on cancellation.
                    var argument = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        Debug.WriteLine($"[SingleInstance] Received activation argument: {argument}");
                        try
                        {
                            callback(argument);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SingleInstance] Activation callback threw: {ex}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstance] Listener error: {ex.Message}");
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static string Scope(string applicationId)
        {
            var raw = $"{applicationId}|{Environment.UserName}";
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            }

            // BitConverter.ToString/Convert.ToHexString both upper-case; this avoids the
            // .NET-Framework-incompatible Convert.ToHexString overload.
            return "OktaSso_" + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                listenerCts?.Cancel();
                listenerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Listener shutdown error: {ex.Message}");
            }

            listenerCts?.Dispose();

            if (ownsMutex)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Not owned on this thread; safe to ignore during shutdown.
                }
            }

            mutex.Dispose();
        }
    }
}
