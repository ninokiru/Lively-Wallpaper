using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Core;
using Lively.Core.Display;
using Lively.Core.Wallpapers;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Drawing;

namespace Lively.Factories
{
    public class WallpaperPluginFactory : IWallpaperPluginFactory
    {
        private readonly IWebView2UserDataFactory webView2UserDataFactory;
        private readonly IDisplayManager displayManager;
        private readonly ILivelyPropertyFactory lpFactory;
        private readonly IUserSettingsService userSettings;

        public WallpaperPluginFactory(ILivelyPropertyFactory lpFactory,
            IWebView2UserDataFactory webViewUserDataFactory,
            IDisplayManager displayManager,
            IUserSettingsService userSettings)
        {
            this.lpFactory = lpFactory;
            this.userSettings = userSettings;
            this.displayManager = displayManager;
            this.webView2UserDataFactory = webViewUserDataFactory;
        }

        public IWallpaper CreateDwmThumbnailWallpaper(LibraryModel model,
                                                      IntPtr thumbnailSrc,
                                                      Rectangle targetRect,
                                                      DisplayMonitor display)
        {
            return new DwmThumbnailPlayer(thumbnailSrc, model, display, targetRect);
        }

        public IWallpaper CreateWallpaper(LibraryModel model,
            DisplayMonitor display,
            WallpaperArrangement arrangement,
            bool isWindowed = false)
        {
            switch (model.LivelyInfo.Type)
            {
                case WallpaperType.web:
                case WallpaperType.webaudio:
                case WallpaperType.url:
                case WallpaperType.videostream:
                case WallpaperType.video:
                case WallpaperType.gif:
                    return CreateWebView2Wallpaper(model, display, arrangement, isWindowed);
                case WallpaperType.picture:
                    // Static images use the shell wallpaper api: no extra process, no gpu cost.
                    return new PictureWinApi(model.FilePath, model, display, arrangement, userSettings.Settings.WallpaperScaling);
                case WallpaperType.app:
                case WallpaperType.bizhawk:
                case WallpaperType.unity:
                case WallpaperType.unityaudio:
                case WallpaperType.godot:
                    if (PackageUtil.IsRunningAsPackaged)
                    {
                        throw new MsixNotAllowedException("Program wallpaper on MSIX package not allowed.");
                    }
                    else
                    {
                        return new ExtPrograms(model.FilePath, model, display,
                          userSettings.Settings.WallpaperWaitTime);
                    }
            }
            throw new PluginNotFoundException("Wallpaper player not found.");
        }

        private IWallpaper CreateWebView2Wallpaper(LibraryModel model,
            DisplayMonitor display,
            WallpaperArrangement arrangement,
            bool isWindowed)
        {
            var settings = userSettings.Settings;
            var scaler = model.LivelyInfo.Type switch
            {
                WallpaperType.gif => settings.ScalerGif,
                WallpaperType.video => settings.ScalerVideo,
                _ => settings.WallpaperScaling,
            };

            return new WebView2Wallpaper(model.FilePath,
                model,
                display,
                lpFactory.CreateLivelyPropertyFolder(model, display, arrangement, userSettings),
                GetWebView2UserDataDir(arrangement, display, isWindowed),
                settings.ApplicationTheme,
                settings.AudioVolumeGlobal,
                settings.WebDebugPort,
                GetWebView2Scale(display, isWindowed),
                settings.VisualizerAudioDeviceId,
                scaler);
        }

        private string GetWebView2UserDataDir(WallpaperArrangement arrangement, DisplayMonitor display, bool isWindowed)
        {
            // A windowed preview must not share (or wipe) the disk cache of a running wallpaper.
            return !isWindowed && userSettings.Settings.IsBrowserDiskCacheEnabled ?
                webView2UserDataFactory.GetUserDataFolder(arrangement, display) : webView2UserDataFactory.GetTempUserDataFolder();
        }

        private double? GetWebView2Scale(DisplayMonitor display, bool isWindowed)
        {
            if (isWindowed || !displayManager.IsMultiScreen())
                return null;

            // When running as child of WorkerW/Progman, the WebView2 surface does not pick up the correct DPI. 
            return DpiUtil.TryGetDisplayScale(display.HMonitor, out double targetScale) ? targetScale : null;
        }

        #region exceptions

        public class MsixNotAllowedException : Exception
        {
            public MsixNotAllowedException()
            {
            }

            public MsixNotAllowedException(string message)
                : base(message)
            {
            }

            public MsixNotAllowedException(string message, Exception inner)
                : base(message, inner)
            {
            }
        }

        public class PluginNotFoundException : Exception
        {
            public PluginNotFoundException()
            {
            }

            public PluginNotFoundException(string message)
                : base(message)
            {
            }

            public PluginNotFoundException(string message, Exception inner)
                : base(message, inner)
            {
            }
        }

        #endregion //exceptions
    }
}
