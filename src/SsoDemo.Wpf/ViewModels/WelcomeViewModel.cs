using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta;
using Common.Authentication.Okta.Claims;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;
using SsoDemo.Wpf.Services;

namespace SsoDemo.Wpf.ViewModels
{
    /// <summary>Signed-in confirmation plus a panel for testing token-authenticated API calls.</summary>
    public sealed class WelcomeViewModel : BindableBase, INavigationAware
    {
        private readonly IOktaAuthenticationService authentication;
        private readonly IRegionManager regionManager;
        private readonly IDemoApiClient apiClient;
        private readonly IDemoApiBClient apiBClient;

        private string displayName = string.Empty;
        private string email = string.Empty;
        private string employeeId = string.Empty;
        private bool isBusy;
        private bool isCallingApi;
        private string apiResponse = string.Empty;
        private string? apiError;

        public WelcomeViewModel(
            IOktaAuthenticationService authentication,
            IRegionManager regionManager,
            IDemoApiClient apiClient,
            IDemoApiBClient apiBClient)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            this.regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.apiBClient = apiBClient ?? throw new ArgumentNullException(nameof(apiBClient));

            SignOutCommand = new DelegateCommand(async () => await SignOutAsync(), () => !IsBusy)
                .ObservesProperty(() => IsBusy);
            CallProfileCommand = new DelegateCommand(async () => await CallApiAsync(apiClient.GetProfileAsync), () => !IsCallingApi)
                .ObservesProperty(() => IsCallingApi);
            CallOrdersCommand = new DelegateCommand(async () => await CallApiAsync(apiClient.GetOrdersAsync), () => !IsCallingApi)
                .ObservesProperty(() => IsCallingApi);
            CallApiBDirectCommand = new DelegateCommand(async () => await CallApiAsync(apiBClient.GetResourceAsync), () => !IsCallingApi)
                .ObservesProperty(() => IsCallingApi);
            CallApiBViaExchangeCommand = new DelegateCommand(async () => await CallApiAsync(apiClient.GetPartnerViaExchangeAsync), () => !IsCallingApi)
                .ObservesProperty(() => IsCallingApi);
        }

        public DelegateCommand SignOutCommand { get; }

        public DelegateCommand CallProfileCommand { get; }

        public DelegateCommand CallOrdersCommand { get; }

        public DelegateCommand CallApiBDirectCommand { get; }

        public DelegateCommand CallApiBViaExchangeCommand { get; }

        public string DisplayName
        {
            get => displayName;
            private set => SetProperty(ref displayName, value);
        }

        public string Email
        {
            get => email;
            private set => SetProperty(ref email, value);
        }

        public string EmployeeId
        {
            get => employeeId;
            private set => SetProperty(ref employeeId, value);
        }

        public string ApiBaseUrl => apiClient.BaseUrl;

        public bool IsBusy
        {
            get => isBusy;
            private set => SetProperty(ref isBusy, value);
        }

        public bool IsCallingApi
        {
            get => isCallingApi;
            private set => SetProperty(ref isCallingApi, value);
        }

        public string ApiResponse
        {
            get => apiResponse;
            private set => SetProperty(ref apiResponse, value);
        }

        public string? ApiError
        {
            get => apiError;
            private set
            {
                if (SetProperty(ref apiError, value))
                {
                    RaisePropertyChanged(nameof(HasApiError));
                }
            }
        }

        public bool HasApiError => !string.IsNullOrWhiteSpace(ApiError);

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var principal = authentication.CurrentPrincipal;
            DisplayName = principal?.FindFirst(ClaimTypes.Name)?.Value ?? "(unknown)";
            Email = principal?.FindFirst(ClaimTypes.Email)?.Value ?? "(none)";
            EmployeeId = principal?.FindFirst(CustomClaimTypes.AdEmployeeId)?.Value ?? "(none)";

            ApiResponse = string.Empty;
            ApiError = null;
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        private async Task CallApiAsync(Func<CancellationToken, Task<string>> call)
        {
            IsCallingApi = true;
            ApiError = null;
            try
            {
                ApiResponse = await call(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WelcomeViewModel] API call failed: {ex}");
                ApiResponse = string.Empty;
                ApiError = ex.Message;
            }
            finally
            {
                IsCallingApi = false;
            }
        }

        private async Task SignOutAsync()
        {
            IsBusy = true;
            try
            {
                await authentication.SignOutAsync(CancellationToken.None);
                regionManager.RequestNavigate(RegionNames.Content, ViewNames.SignedOut);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WelcomeViewModel] Sign-out threw: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
