using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SsoDemo.Wpf.Infrastructure
{
    /// <summary>
    /// Brings the app window back to the user after the external browser hands the OAuth callback
    /// to the running instance. Uses only gentle, well-behaved calls: WPF's own
    /// <see cref="Window.Activate"/> (permitted because the redirect instance called
    /// <see cref="GrantToRunningInstance"/>), falling back to a taskbar flash. It never forces
    /// keyboard focus or attaches thread input, both of which can misdirect a stray click.
    /// </summary>
    internal static class ForegroundWindow
    {
        private const int AsfwAny = -1;
        private const uint FlashwAll = 3;
        private const uint FlashwTimernofg = 12;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        /// <summary>
        /// Called by the short-lived secondary instance (the one the OS launches for the
        /// <c>app://</c> redirect) so the already-running app is permitted to foreground itself.
        /// </summary>
        public static void GrantToRunningInstance()
        {
            try
            {
                AllowSetForegroundWindow(AsfwAny);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForegroundWindow] AllowSetForegroundWindow failed: {ex.Message}");
            }
        }

        /// <summary>Called by the primary instance to draw attention to <paramref name="window"/>.</summary>
        public static void Bring(Window? window)
        {
            if (window is null)
            {
                return;
            }

            try
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                if (!window.Activate())
                {
                    FlashTaskbar(window);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForegroundWindow] Bring failed: {ex.Message}");
            }
        }

        private static void FlashTaskbar(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle,
                dwFlags = FlashwAll | FlashwTimernofg,
                uCount = uint.MaxValue,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
    }
}
