using Lively.UI.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Lively.UI.WinUI.Views.Pages
{
    /// <summary>
    /// Update page. The status banner and the release notes are bound from the view
    /// model, so nothing besides resolving that view model is wired up here.
    /// </summary>
    public sealed partial class AppUpdateView : Page
    {
        public AppUpdateView()
        {
            this.InitializeComponent();
            this.DataContext = App.Services.GetRequiredService<AppUpdateViewModel>();
        }
    }
}
