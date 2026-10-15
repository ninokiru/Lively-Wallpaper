using Lively.UI.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace Lively.UI.WinUI.Views.Pages.Settings
{
    public sealed partial class SettingsGeneralView : Page
    {
        private readonly SettingsGeneralViewModel viewModel;

        public SettingsGeneralView()
        {
            this.InitializeComponent();
            this.viewModel = App.Services.GetRequiredService<SettingsGeneralViewModel>();
            this.DataContext = viewModel;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            viewModel.OnClose();
        }
    }
}
