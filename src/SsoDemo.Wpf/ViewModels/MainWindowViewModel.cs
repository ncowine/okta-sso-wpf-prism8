using System;
using System.Windows;
using Common.Authentication.Okta;
using Prism.Mvvm;

namespace SsoDemo.Wpf.ViewModels
{
    public sealed class MainWindowViewModel : BindableBase, IDisposable
    {
        private readonly IOktaAuthenticationService authentication;
        private string statusText = "Not signed in";

        public MainWindowViewModel(IOktaAuthenticationService authentication)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            this.authentication.StateChanged += OnAuthenticationStateChanged;
            UpdateStatus(this.authentication.CurrentPrincipal?.Identity?.Name);
        }

        public string Title => "Okta SSO Demo — Prism 8 + DryIoc";

        public string StatusText
        {
            get => statusText;
            private set => SetProperty(ref statusText, value);
        }

        private void OnAuthenticationStateChanged(object? sender, AuthenticationStateChangedEventArgs e)
        {
            var name = e.Principal?.Identity?.Name;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => UpdateStatus(name));
            }
            else
            {
                UpdateStatus(name);
            }
        }

        private void UpdateStatus(string? userName) =>
            StatusText = string.IsNullOrWhiteSpace(userName) ? "Not signed in" : $"Signed in as {userName}";

        public void Dispose() => authentication.StateChanged -= OnAuthenticationStateChanged;
    }
}
