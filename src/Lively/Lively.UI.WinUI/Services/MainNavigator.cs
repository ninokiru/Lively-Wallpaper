using Lively.Common.Services;
using Lively.Models.Enums;
using Lively.UI.WinUI.Views.Pages;
using Lively.UI.WinUI.Views.Pages.Gallery;
using Lively.UI.WinUI.Views.Pages.Settings;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace Lively.UI.WinUI.Services
{
    public class MainNavigator : IMainNavigator
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <inheritdoc/>
        public event EventHandler<ContentPageType>? ContentPageChanged;

        public object? RootFrame { get; set; }

        /// <inheritdoc/>
        public object? Frame { get; set; }

        public ContentPageType? CurrentPage { get; private set; } = null;

        public void NavigateTo(ContentPageType contentPage, object navArgs = null)
        {
            if (CurrentPage == contentPage)
                return;

            InternalNavigateTo(contentPage, new DrillInNavigationTransitionInfo(), navArgs);
        }

        public void Reload()
        {
            if (CurrentPage == null)
                return;

            InternalNavigateTo(CurrentPage.Value, new EntranceNavigationTransitionInfo());
        }

        private void InternalNavigateTo(ContentPageType contentPage, NavigationTransitionInfo transition, object navArgs = null)
        {
            Type pageType = contentPage switch
            {
                ContentPageType.library => typeof(LibraryView),
                ContentPageType.gallery => typeof(GalleryView),
                ContentPageType.appupdate => typeof(AppUpdateView),
                ContentPageType.settingsGeneral => typeof(SettingsGeneralView),
                ContentPageType.settingsPerformance => typeof(SettingsPerformanceView),
                ContentPageType.settingsScreensaver => typeof(SettingsScreensaverView),
                ContentPageType.settingsWallpaper => typeof(SettingsWallpaperView),
                ContentPageType.settingsSystem => typeof(SettingsSystemView),
                _ => throw new NotImplementedException(),
            };

            if (Frame is not Frame f)
                return;

            // A page that throws while being built (bad resource, view model that cannot be
            // resolved) used to take the whole UI client down with a native fast-fail, leaving
            // an empty content area and no log entry. Stay on the current page and record it.
            try
            {
                if (!f.Navigate(pageType, navArgs, transition))
                    throw new InvalidOperationException($"Navigation to {pageType.Name} was refused by the frame.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return;
            }

            CurrentPage = contentPage;
            ContentPageChanged?.Invoke(this, contentPage);
        }
    }
}
