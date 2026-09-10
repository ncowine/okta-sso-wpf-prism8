using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta;
using Prism.Mvvm;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;

namespace SsoDemo.Wpf.ViewModels
{
    /// <summary>
    /// Startup view model. Runs the sign-in flow with no user interaction: silent refresh first,
    /// then the Okta browser flow. Navigates to <see cref="ViewNames.Welcome"/> on success or
    /// <see cref="ViewNames.AccessDenied"/> on failure.
    /// </summary>
    public sealed class AuthenticatingViewModel : BindableBase, INavigationAware
    {
        private readonly IOktaAuthenticationService authentication;
        private readonly IRegionManager regionManager;

        private string statusText = "Signing you in…";
        private bool started;

        public AuthenticatingViewModel(IOktaAuthenticationService authentication, IRegionManager regionManager)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            this.regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
        }

        public string StatusText
        {
            get => statusText;
            private set => SetProperty(ref statusText, value);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (started)
            {
                return;
            }

            started = true;
            _ = RunSignInFlowAsync();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // Allow the flow to run again if we navigate back here (e.g. after sign-out).
            started = false;
            StatusText = "Signing you in…";
        }

        private async Task RunSignInFlowAsync()
        {
            try
            {
                var result = await authentication.TrySignInSilentAsync(CancellationToken.None);

                if (!result.Success)
                {
                    StatusText = "Waiting for you to finish signing in through your browser…";
                    result = await authentication.SignInInteractiveAsync(CancellationToken.None);
                }

                if (result.Success)
                {
                    Debug.WriteLine("[AuthenticatingViewModel] Sign-in succeeded; navigating to Welcome.");
                    regionManager.RequestNavigate(RegionNames.Content, ViewNames.Welcome);
                    return;
                }

                Debug.WriteLine($"[AuthenticatingViewModel] Sign-in failed: {result.Error}");
                NavigateToAccessDenied(result.Error);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthenticatingViewModel] Sign-in flow threw: {ex}");
                NavigateToAccessDenied(ex.Message);
            }
        }

        private void NavigateToAccessDenied(string? errorDetail)
        {
            var parameters = new NavigationParameters
            {
                { NavigationKeys.ErrorDetail, errorDetail ?? "Unknown error." },
            };
            regionManager.RequestNavigate(RegionNames.Content, ViewNames.AccessDenied, parameters);
        }
    }
}
