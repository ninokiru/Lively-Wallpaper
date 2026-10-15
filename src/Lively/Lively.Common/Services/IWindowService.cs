using System.Threading.Tasks;

namespace Lively.Common.Services
{
    public interface IWindowService
    {
        bool IsGridOverlayVisible { get; }
        void ShowLogWindow();
        void ShowDiagnosticWindow();
        void ShowGridOverlay(bool isVisible);
        Task<bool> ShowWallpaperDialogWindowAsync(object wallpaper);
    }
}