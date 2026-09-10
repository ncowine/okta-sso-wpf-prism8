using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Browser
{
    /// <summary>
    /// Single-slot rendezvous between the OS activation callback and the waiting browser request.
    /// Only one interactive flow runs at a time.
    /// </summary>
    public sealed class BrowserCallbackChannel : IBrowserCallbackChannel
    {
        private readonly object sync = new();
        private TaskCompletionSource<string>? pending;

        public Task<string> WaitForCallbackAsync(CancellationToken cancellationToken)
        {
            lock (sync)
            {
                pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            cancellationToken.Register(static state =>
            {
                var tcs = (TaskCompletionSource<string>)state!;
                tcs.TrySetCanceled();
            }, pending);

            return pending.Task;
        }

        public void Publish(string callbackUri)
        {
            TaskCompletionSource<string>? waiter;
            lock (sync)
            {
                waiter = pending;
            }

            if (waiter is null)
            {
                Debug.WriteLine($"[BrowserCallbackChannel] Callback received with no waiter, ignoring: {callbackUri}");
                return;
            }

            Debug.WriteLine("[BrowserCallbackChannel] Delivered callback URI to waiting browser request.");
            waiter.TrySetResult(callbackUri);
        }
    }
}
