using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Browser
{
    /// <summary>
    /// <see cref="IBrowser"/> implementation for OidcClient that opens the user's default browser and
    /// waits for the custom-scheme redirect to be delivered via <see cref="IBrowserCallbackChannel"/>.
    /// </summary>
    public sealed class SystemBrowser : IBrowser
    {
        private readonly IBrowserCallbackChannel callbackChannel;
        private readonly OktaAuthenticationOptions options;

        public SystemBrowser(IBrowserCallbackChannel callbackChannel, OktaAuthenticationOptions options)
        {
            this.callbackChannel = callbackChannel ?? throw new ArgumentNullException(nameof(callbackChannel));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<BrowserResult> InvokeAsync(BrowserOptions browserOptions, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = new CancellationTokenSource(options.InteractiveTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            var waitTask = callbackChannel.WaitForCallbackAsync(linked.Token);

            try
            {
                Debug.WriteLine($"[SystemBrowser] Opening default browser: {browserOptions.StartUrl}");
                if (Environment.GetEnvironmentVariable("SSODEMO_NO_BROWSER") == "1")
                {
                    Debug.WriteLine("[SystemBrowser] SSODEMO_NO_BROWSER=1: not launching a browser (test mode).");
                }
                else
                {
                    using var process = Process.Start(new ProcessStartInfo(browserOptions.StartUrl) { UseShellExecute = true });
                    Debug.WriteLine($"[SystemBrowser] Browser launch requested (process id {process?.Id.ToString() ?? "n/a"}); awaiting redirect.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemBrowser] Failed to launch browser: {ex.Message}");
                return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = ex.Message };
            }

            try
            {
                var callbackUri = await waitTask.ConfigureAwait(false);
                Debug.WriteLine("[SystemBrowser] Redirect received; sign-in browser step complete.");
                return new BrowserResult { ResultType = BrowserResultType.Success, Response = callbackUri };
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                Debug.WriteLine("[SystemBrowser] Timed out waiting for the sign-in redirect.");
                return new BrowserResult { ResultType = BrowserResultType.Timeout };
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[SystemBrowser] Sign-in was cancelled.");
                return new BrowserResult { ResultType = BrowserResultType.UserCancel };
            }
        }
    }
}
