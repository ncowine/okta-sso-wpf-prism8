using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Common.Authentication.Okta.Platform
{
    /// <summary>
    /// Registers the custom URI scheme under <c>HKEY_CURRENT_USER</c> (no elevation required) so the
    /// browser can hand the OAuth redirect back to this application.
    /// </summary>
    public sealed class HkcuCustomUriSchemeRegistrar : ICustomUriSchemeRegistrar
    {
        private readonly OktaAuthenticationOptions options;

        public HkcuCustomUriSchemeRegistrar(OktaAuthenticationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void EnsureRegistered()
        {
            var scheme = options.CustomUriScheme;
            var command = BuildLaunchCommand();

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true)
                ?? throw new InvalidOperationException(@"Unable to open HKCU\Software\Classes.");
            using var schemeKey = classes.CreateSubKey(scheme, writable: true);

            var existingCommand = schemeKey.OpenSubKey(@"shell\open\command")?.GetValue(null) as string;
            if (string.Equals(existingCommand, command, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[HkcuCustomUriSchemeRegistrar] Scheme '{scheme}://' already registered.");
                return;
            }

            schemeKey.SetValue(null, $"URL:{scheme} protocol");
            schemeKey.SetValue("URL Protocol", string.Empty);

            using var commandKey = schemeKey.CreateSubKey(@"shell\open\command", writable: true);
            commandKey.SetValue(null, command);

            Debug.WriteLine($"[HkcuCustomUriSchemeRegistrar] Registered '{scheme}://' -> {command}");
        }

        private static string BuildLaunchCommand()
        {
            // Process.GetCurrentProcess().MainModule.FileName (rather than the newer
            // Environment.ProcessPath, which .NET Framework doesn't have) works identically on
            // every target this library builds for.
            var processPath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine the current executable path.");

            // When launched through `dotnet run` / `dotnet <HostApp>.dll` the host is dotnet.exe;
            // register the managed entry point explicitly so activation still reaches this app.
            if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var entryAssembly = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(entryAssembly))
                {
                    return $"\"{processPath}\" \"{entryAssembly}\" \"%1\"";
                }
            }

            return $"\"{processPath}\" \"%1\"";
        }
    }
}
