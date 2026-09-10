using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;

namespace SsoDemo.Wpf.ViewModels
{
    /// <summary>Shown when sign-in fails. The only action is to close the application.</summary>
    public sealed class AccessDeniedViewModel : BindableBase, INavigationAware
    {
        private string errorDetail = string.Empty;

        public AccessDeniedViewModel()
        {
            ExitCommand = new DelegateCommand(() => Application.Current.Shutdown());
        }

        public DelegateCommand ExitCommand { get; }

        public string Headline => "You don't have permission to access this application.";

        public string ErrorDetail
        {
            get => errorDetail;
            private set => SetProperty(ref errorDetail, value);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            ErrorDetail = navigationContext.Parameters.GetValue<string>(NavigationKeys.ErrorDetail) ?? string.Empty;
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }
    }
}
