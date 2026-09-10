using System;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using SsoDemo.Wpf.Infrastructure;

namespace SsoDemo.Wpf.ViewModels
{
    /// <summary>Shown after sign-out. Sign-in only restarts when the user asks for it.</summary>
    public sealed class SignedOutViewModel : BindableBase
    {
        private readonly IRegionManager regionManager;

        public SignedOutViewModel(IRegionManager regionManager)
        {
            this.regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            SignInCommand = new DelegateCommand(() =>
                regionManager.RequestNavigate(RegionNames.Content, ViewNames.Authenticating));
        }

        public DelegateCommand SignInCommand { get; }
    }
}
