using Lively.Common.Helpers;
using Lively.Models.Enums;
using System;
using System.IO;

namespace Lively.Common
{
    public static class Constants
    {
        public static class CommonPaths
        {
            public static string AppDataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationType.Name);
            public static string LogDir { get; } = Path.Combine(AppDataDir, "logs");
            public static string LogDirUI { get; } = Path.Combine(AppDataDir, "UI"); //temp use..
            public static string TempDir { get; } = Path.Combine(AppDataDir, "temp");
            public static string TempWebView2Dir { get; } = Path.Combine(AppDataDir, "WebView2");
            public static string TempMediaDir { get; } = Path.Combine(AppDataDir, "Media");
            public static string AppRulesPath { get; } = Path.Combine(AppDataDir, "AppRules.json");
            public static string WallpaperLayoutPath { get; } = Path.Combine(AppDataDir, "WallpaperLayout.json");
            public static string ScreenSaverLayoutPath { get; } = Path.Combine(AppDataDir, "ScreenSaverLayout.json");
            public static string UserSettingsPath { get; } = Path.Combine(AppDataDir, "Settings.json");
            public static string MusicAppExclusionRulesPath { get; } = Path.Combine(AppDataDir, "MusicAppExclusionRules.json");
            /// <summary>
            /// Timed wallpaper rotation, written by the settings window and read by the core timer.
            /// </summary>
            public static string PlaylistPath { get; } = Path.Combine(AppDataDir, "Playlist.json");
            /// <summary>
            /// Per screen fps budget, same split as the playlist: UI writes, core reads.
            /// </summary>
            public static string DisplayPerformancePath { get; } = Path.Combine(AppDataDir, "DisplayPerformance.json");
            public static string WeatherSettingsPath { get; } = Path.Combine(AppDataDir, "WeatherSettings.json");
            public static string ThemeDir { get; } = Path.Combine(AppDataDir, "Themes");
            public static string ThemeCacheDir { get; } = Path.Combine(Path.GetTempPath(), "Lively Wallpaper", "themes");
            public static string WebView2RootCacheDir { get; } = Path.Combine(Path.GetTempPath(), "Lively Wallpaper", "WebView2Cache");
            public static string TokensPath { get; } = Path.Combine(AppDataDir, "Tokens.dat");
            public static string ScreenshotDir { get; } = Path.Combine(AppDataDir, "Screenshots");
        }

        /// <summary>
        /// To be combined with dynamic path Settings.WallpaperDir
        /// </summary>
        public static class CommonPartialPaths
        {
            public static string WallpaperInstallDir { get; } = "wallpapers";
            public static string WallpaperInstallTempDir { get; } = Path.Combine("SaveData", "wptmp");
            public static string WallpaperSettingsDir { get; } = Path.Combine("SaveData", "wpdata");
        }

        /// <summary>
        /// To be combined with Core base directory.
        /// Single player engine: every wallpaper type is rendered by the WebView2 plugin.
        /// </summary>
        public static class PlayerPartialPaths
        {
            public static string PlayerDir { get; } = "plugins";
            public static string WebView2Dir { get; } = Path.Combine("plugins", "webview2");
            public static string WebView2Path { get; } = Path.Combine(WebView2Dir, "Lively.Player.WebView2.exe");
        }

        public static class MachineLearning
        {
            public static string BaseDir { get; } = Path.Combine(CommonPaths.AppDataDir, "ML");
            public static string MiDaSDir { get; } = Path.Combine(BaseDir, "Midas");
        }

        public static class SingleInstance
        {
            public static string UniqueAppName { get; } = "LIVELY:DESKTOPWALLPAPERSYSTEM";
            public static string PipeServerName { get; } = UniqueAppName + Environment.UserName; //backward compatibility < v1.9
            public static string GrpcPipeServerName { get; } = "Grpc_" + PipeServerName;
        }
        
        public static class ApplicationType
        {
            public static string Name { get; } = "Lively Wallpaper";
            public static bool IsTestBuild { get; } = false;

            // Single channel fork build: updates and release notes come from this repository.
            public static string UpdateRepoOwner { get; } = "ninokiru";
            public static string UpdateRepoName { get; } = "Lively-Wallpaper";
            public static string UpdateRepoUrl { get; } = $"https://github.com/{UpdateRepoOwner}/{UpdateRepoName}";
        }

        public static class Weather
        {
            //todo: make compile-time flag.
            public static string OpenWeatherMapAPIKey = string.Empty;
        }
    }
}
