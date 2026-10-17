using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common;
using Lively.Common.Factories;
using Lively.Common.Helpers.Shell;
using Lively.Common.Helpers.Storage;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using Lively.UI.Shared.Factories;
using Lively.UI.Shared.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    public partial class SettingsWallpaperViewModel : ObservableObject
    {
        private readonly IUserSettingsClient userSettings;
        private readonly IDesktopCoreClient desktopCore;
        private readonly IDialogService dialogService;
        private readonly IDispatcherService dispatcher;
        private readonly IDownloadService downloader;
        private readonly IResourceService i18n;
        private readonly IAudioDeviceFactory audioDeviceFactory;
        private readonly IApplicationsRulesFactory appRuleFactory;
        private readonly ICommandsClient commandsClient;
        private readonly IDisplayManagerClient displayManager;
        private readonly LibraryViewModel library;

        public SettingsWallpaperViewModel(IUserSettingsClient userSettings,
            IDesktopCoreClient desktopCore,
            IDialogService dialogService,
            IDispatcherService dispatcher,
            ICommandsClient commandsClient,
            IDownloadService downloader,
            IResourceService i18n,
            IAudioDeviceFactory audioDeviceFactory,
            IApplicationsRulesFactory appRuleFactory,
            IDisplayManagerClient displayManager,
            LibraryViewModel library)
        {
            this.i18n = i18n;
            this.userSettings = userSettings;
            this.desktopCore = desktopCore;
            this.dialogService = dialogService;
            this.appRuleFactory = appRuleFactory;
            this.commandsClient = commandsClient;
            this.audioDeviceFactory = audioDeviceFactory;
            this.dispatcher = dispatcher;
            this.downloader = downloader;
            this.displayManager = displayManager;
            this.library = library;

            AppMusicExclusionRules = new ObservableCollection<AppMusicExclusionRuleModel>(GetAppMusicExclusionRule());
            LoadPlaylistConfig();
            IsDesktopAutoWallpaper = userSettings.Settings.DesktopAutoWallpaper;
            //IsLockScreenAutoWallpaper = userSettings.Settings.LockScreenAutoWallpaper;
            SelectedWallpaperScalingIndex = (int)userSettings.Settings.WallpaperScaling;
            SelectedWallpaperInputMode = (int)userSettings.Settings.InputForward;
            MouseMoveOnDesktop = userSettings.Settings.MouseInputMovAlways;
            WebDebuggingPort = userSettings.Settings.WebDebugPort;
            IsWebDebuggingEnabled = !string.IsNullOrEmpty(WebDebuggingPort);
            IsBrowserDiskCacheEnabled = userSettings.Settings.IsBrowserDiskCacheEnabled;
            IsWebView2Required = !WebViewUtil.IsWebView2Available();
            // AudioDevices is populated only when IsShowAudioDevices is true for better UX.
        }

        private bool _isDesktopAutoWallpaper;
        public bool IsDesktopAutoWallpaper
        {
            get => _isDesktopAutoWallpaper;
            set
            {
                if (userSettings.Settings.DesktopAutoWallpaper != value)
                {
                    userSettings.Settings.DesktopAutoWallpaper = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isDesktopAutoWallpaper, value);
            }
        }

        //private bool _isLockScreenAutoWallpaper;
        //public bool IsLockScreenAutoWallpaper
        //{
        //    get => _isLockScreenAutoWallpaper;
        //    set
        //    {
        //        if (userSettings.Settings.LockScreenAutoWallpaper != value)
        //        {
        //            userSettings.Settings.LockScreenAutoWallpaper = value;
        //            UpdateSettingsConfigFile();
        //        }
        //        SetProperty(ref _isLockScreenAutoWallpaper, value);
        //    }
        //}

        private int _selectedWallpaperScalingIndex;
        public int SelectedWallpaperScalingIndex
        {
            get => _selectedWallpaperScalingIndex;
            set
            {
                if (userSettings.Settings.WallpaperScaling != (WallpaperScaler)value)
                {
                    userSettings.Settings.WallpaperScaling = (WallpaperScaler)value;
                    UpdateSettingsConfigFile();
                    _ = WallpaperRestart([WallpaperType.video, WallpaperType.picture, WallpaperType.videostream, WallpaperType.gif]);
                }
                SetProperty(ref _selectedWallpaperScalingIndex, value);
            }
        }

        private int _selectedWallpaperInputMode;
        public int SelectedWallpaperInputMode
        {
            get => _selectedWallpaperInputMode;
            set
            {
                if (userSettings.Settings.InputForward != (InputForwardMode)value)
                {
                    var newSetting = (InputForwardMode)value;
                    var previousSetting = userSettings.Settings.InputForward;
                    userSettings.Settings.InputForward = newSetting;
                    UpdateSettingsConfigFile();

                    if (newSetting == InputForwardMode.mousekeyboard)
                        DesktopUtil.SetDesktopIconVisibility(false);
                    else if (previousSetting == InputForwardMode.mousekeyboard)
                        DesktopUtil.SetDesktopIconVisibility(true);
                }
                IsDesktopIconsHidden = userSettings.Settings.InputForward == InputForwardMode.mousekeyboard;
                SetProperty(ref _selectedWallpaperInputMode, value);
            }
        }

        [ObservableProperty]
        private bool isDesktopIconsHidden;

        private bool _mouseMoveOnDesktop;
        public bool MouseMoveOnDesktop
        {
            get => _mouseMoveOnDesktop;
            set
            {
                if (userSettings.Settings.MouseInputMovAlways != value)
                {
                    userSettings.Settings.MouseInputMovAlways = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _mouseMoveOnDesktop, value);
            }
        }

        [ObservableProperty]
        private bool isWebView2Required;

        [ObservableProperty]
        private bool isWebView2Installing;

        [RelayCommand]
        private async Task InstallWebView2()
        {
            try
            {
                IsWebView2Installing = true;

                if (await WebViewUtil.InstallWebView2(downloader))
                {
                    IsWebView2Required = false;
                    // Restart wallpaper
                    _ = WallpaperRestart([WallpaperType.web, WallpaperType.webaudio, WallpaperType.url, WallpaperType.videostream, WallpaperType.video, WallpaperType.gif]);
                    // Restart to reload WebView2 (Update changelog page.)
                    _ = commandsClient.RestartUI();
                }
                else
                {
                    LinkUtil.OpenBrowser(WebViewUtil.DownloadUrl);
                }
            }
            finally
            {
                IsWebView2Installing = false;
            }
        }

        private bool _isWebDebuggingEnabled;
        public bool IsWebDebuggingEnabled
        {
            get => _isWebDebuggingEnabled;
            set
            {
                if (SetProperty(ref _isWebDebuggingEnabled, value))
                {
                    if (value && string.IsNullOrWhiteSpace(WebDebuggingPort))
                        WebDebuggingPort = "9223";
                    else if (!value)
                        WebDebuggingPort = string.Empty;
                }
            }
        }

        private string _webDebuggingPort;
        public string WebDebuggingPort
        {
            get => _webDebuggingPort;
            set
            {
                if (userSettings.Settings.WebDebugPort != value)
                {
                    userSettings.Settings.WebDebugPort = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _webDebuggingPort, value);
            }
        }

        private bool _isBrowserDiskCacheEnabled;
        public bool IsBrowserDiskCacheEnabled
        {
            get => _isBrowserDiskCacheEnabled;
            set
            {
                if (userSettings.Settings.IsBrowserDiskCacheEnabled != value)
                {
                    userSettings.Settings.IsBrowserDiskCacheEnabled = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isBrowserDiskCacheEnabled, value);
            }
        }

        #region apprules

        [ObservableProperty]
        private bool isMusicSettingsChanged;

        private bool _isShowAudioDevices;
        public bool IsShowAudioDevices
        {
            get => _isShowAudioDevices;
            set
            {
                if (value && AudioDevices == null)
                {
                    var defaultName = i18n.GetString("TextDefault.Text");
                    var defaultIcon = "ms-appx:///Assets/icons8-audio-96.png";
                    try
                    {
                        AudioDevices = new ObservableCollection<AudioDevice>(audioDeviceFactory.GetRenderDevices());
                        var defaultDevice = audioDeviceFactory.GetDefaultRenderDevice();
                        defaultName = $"{defaultName} [{defaultDevice.Name}]";
                        defaultIcon = defaultDevice.DeviceIcon;
                    }
                    catch {
                        AudioDevices = [];
                    }
                    AudioDevices.Insert(0, new(string.Empty, defaultName, defaultIcon));
                    SelectedAudioDevice = AudioDevices.FirstOrDefault(x => x.Id == userSettings.Settings.VisualizerAudioDeviceId) ?? AudioDevices[0];
                }
                SetProperty(ref _isShowAudioDevices, value);
            }
        }

        [ObservableProperty]
        private ObservableCollection<AudioDevice> audioDevices;

        private AudioDevice _selectedAudioDevice;
        public AudioDevice SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set
            {
                if (value != null && userSettings.Settings.VisualizerAudioDeviceId != value.Id)
                {
                    userSettings.Settings.VisualizerAudioDeviceId = value.Id;
                    UpdateSettingsConfigFile();

                    IsMusicSettingsChanged = true;
                }
                SetProperty(ref _selectedAudioDevice, value);
            }
        }

        [ObservableProperty]
        private ObservableCollection<AppMusicExclusionRuleModel> appMusicExclusionRules;

        private AppMusicExclusionRuleModel _selectedAppMusicExclusionRuleItem;
        public AppMusicExclusionRuleModel SelectedAppMusicExclusionRuleItem
        {
            get => _selectedAppMusicExclusionRuleItem;
            set
            {
                SetProperty(ref _selectedAppMusicExclusionRuleItem, value);
                RemoveAppMusicExclusionRuleCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private async Task AddAppMusicExclusionRule()
        {
            var result = await dialogService.ShowApplicationPickerDialogAsync();
            if (result is null)
                return;

            try
            {
                var rule = appRuleFactory.CreateAppMusicExclusionRule(result.AppPath);
                if (AppMusicExclusionRules.Any(x => x.AppName.Equals(rule.AppName, StringComparison.Ordinal)))
                    return;

                AppMusicExclusionRules.Add(rule);
                IsMusicSettingsChanged = true;
                UpdateAppMusicExclusionRule();
            }
            catch { /* Failed to parse program information, ignore. */ }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveAppRule))]
        private void RemoveAppMusicExclusionRule()
        {
            AppMusicExclusionRules.Remove(SelectedAppMusicExclusionRuleItem);
            IsMusicSettingsChanged = true;
            UpdateAppMusicExclusionRule();
        }

        private bool CanRemoveAppRule => SelectedAppMusicExclusionRuleItem != null;

        [RelayCommand]
        private async Task RestartMusicWallpapers()
        {
            IsMusicSettingsChanged = false;
            await WallpaperRestart([WallpaperType.web, WallpaperType.webaudio]);
        }

        private void UpdateAppMusicExclusionRule()
        {
            JsonStorage<List<AppMusicExclusionRuleModel>>.StoreData(Constants.CommonPaths.MusicAppExclusionRulesPath, AppMusicExclusionRules.ToList());
        }

        private static List<AppMusicExclusionRuleModel> GetAppMusicExclusionRule()
        {
            try
            {
                if (File.Exists(Constants.CommonPaths.MusicAppExclusionRulesPath))
                    return JsonStorage<List<AppMusicExclusionRuleModel>>.LoadData(Constants.CommonPaths.MusicAppExclusionRulesPath);
            }
            catch { /* Ignore */ }
            return [];
        }

        #endregion //apprules

        #region playlist

        // Matches the order of the interval items declared in SettingsWallpaperView.xaml.
        private static readonly int[] playlistIntervals = [5, 15, 30, 60, 120, 180, 360, 720];

        // Snapshot of the file, keeps the fields the ui does not edit.
        private PlaylistModel playlist;

        public List<string> PlaylistHours { get; } = Enumerable.Range(0, 24).Select(x => $"{x:00}:00").ToList();
        public ObservableCollection<LibraryModel> PlaylistSources => library.LibraryItems;
        public List<string> PlaylistScreens { get; } = new();

        [ObservableProperty]
        private ObservableCollection<PlaylistItemModel> playlistItems = new();

        private bool _isPlaylistEnabled;
        public bool IsPlaylistEnabled
        {
            get => _isPlaylistEnabled;
            set
            {
                if (SetProperty(ref _isPlaylistEnabled, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private bool _isPlaylistShuffle;
        public bool IsPlaylistShuffle
        {
            get => _isPlaylistShuffle;
            set
            {
                if (SetProperty(ref _isPlaylistShuffle, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private int _selectedPlaylistIntervalIndex;
        public int SelectedPlaylistIntervalIndex
        {
            get => _selectedPlaylistIntervalIndex;
            set
            {
                if (SetProperty(ref _selectedPlaylistIntervalIndex, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private int _selectedPlaylistDaysIndex;
        public int SelectedPlaylistDaysIndex
        {
            get => _selectedPlaylistDaysIndex;
            set
            {
                if (SetProperty(ref _selectedPlaylistDaysIndex, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private int _selectedPlaylistStartHourIndex;
        public int SelectedPlaylistStartHourIndex
        {
            get => _selectedPlaylistStartHourIndex;
            set
            {
                if (SetProperty(ref _selectedPlaylistStartHourIndex, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private int _selectedPlaylistEndHourIndex;
        public int SelectedPlaylistEndHourIndex
        {
            get => _selectedPlaylistEndHourIndex;
            set
            {
                if (SetProperty(ref _selectedPlaylistEndHourIndex, value))
                    UpdatePlaylistConfigFile();
            }
        }

        private LibraryModel _selectedPlaylistSource;
        public LibraryModel SelectedPlaylistSource
        {
            get => _selectedPlaylistSource;
            set
            {
                SetProperty(ref _selectedPlaylistSource, value);
                AddPlaylistItemCommand.NotifyCanExecuteChanged();
            }
        }

        private int _selectedPlaylistScreenIndex;
        public int SelectedPlaylistScreenIndex
        {
            get => _selectedPlaylistScreenIndex;
            set => SetProperty(ref _selectedPlaylistScreenIndex, value);
        }

        private PlaylistItemModel _selectedPlaylistItem;
        public PlaylistItemModel SelectedPlaylistItem
        {
            get => _selectedPlaylistItem;
            set
            {
                SetProperty(ref _selectedPlaylistItem, value);
                RemovePlaylistItemCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(CanAddPlaylistItem))]
        private void AddPlaylistItem()
        {
            var source = SelectedPlaylistSource;
            if (source is null || string.IsNullOrEmpty(source.LivelyInfoFolderPath))
                return;

            var screenIndex = SelectedPlaylistScreenIndex;
            var display = screenIndex >= 0 && screenIndex < displayManager.DisplayMonitors.Count
                ? displayManager.DisplayMonitors[screenIndex]
                : displayManager.PrimaryMonitor;

            var duplicate = PlaylistItems.Any(x =>
                string.Equals(x.LivelyInfoFolderPath, source.LivelyInfoFolderPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.MonitorDeviceId, display?.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                return;

            PlaylistItems.Add(new PlaylistItemModel
            {
                LivelyInfoFolderPath = source.LivelyInfoFolderPath,
                Title = source.Title,
                MonitorDeviceId = display?.DeviceId,
            });
            UpdatePlaylistConfigFile();
        }

        private bool CanAddPlaylistItem => SelectedPlaylistSource != null;

        [RelayCommand(CanExecute = nameof(CanRemovePlaylistItem))]
        private void RemovePlaylistItem()
        {
            if (SelectedPlaylistItem is null)
                return;

            PlaylistItems.Remove(SelectedPlaylistItem);
            UpdatePlaylistConfigFile();
        }

        private bool CanRemovePlaylistItem => SelectedPlaylistItem != null;

        private void LoadPlaylistConfig()
        {
            playlist = GetPlaylistConfig();
            foreach (var display in displayManager.DisplayMonitors)
                PlaylistScreens.Add($"{display.Index + 1} - {display.Bounds.Width}x{display.Bounds.Height}");

            var intervalIndex = Array.IndexOf(playlistIntervals, playlist.IntervalMinutes);
            // Seeded through the backing fields so opening the page never writes the file.
            _isPlaylistEnabled = playlist.IsEnabled;
            _isPlaylistShuffle = playlist.IsShuffle;
            _selectedPlaylistIntervalIndex = intervalIndex < 0 ? 2 : intervalIndex;
            _selectedPlaylistDaysIndex = Math.Clamp((int)playlist.Days, 0, 2);
            _selectedPlaylistStartHourIndex = Math.Clamp(playlist.StartHour, 0, 23);
            _selectedPlaylistEndHourIndex = Math.Clamp(playlist.EndHour, 0, 23);
            PlaylistItems = new ObservableCollection<PlaylistItemModel>(playlist.Items ?? []);
        }

        private static PlaylistModel GetPlaylistConfig()
        {
            try
            {
                if (File.Exists(Constants.CommonPaths.PlaylistPath))
                    return JsonStorage<PlaylistModel>.LoadData(Constants.CommonPaths.PlaylistPath);
            }
            catch { /* Corrupted file, start from the defaults. */ }

            return new PlaylistModel();
        }

        /// <summary>
        /// The core re-reads this file on its own clock, nothing has to be restarted here.
        /// </summary>
        public void UpdatePlaylistConfigFile()
        {
            _ = dispatcher.TryEnqueue(() =>
            {
                var model = playlist ?? new PlaylistModel();
                model.IsEnabled = IsPlaylistEnabled;
                model.IsShuffle = IsPlaylistShuffle;
                model.IntervalMinutes = playlistIntervals[Math.Clamp(SelectedPlaylistIntervalIndex, 0, playlistIntervals.Length - 1)];
                model.Days = (PlaylistDays)Math.Clamp(SelectedPlaylistDaysIndex, 0, 2);
                model.StartHour = Math.Clamp(SelectedPlaylistStartHourIndex, 0, 23);
                model.EndHour = Math.Clamp(SelectedPlaylistEndHourIndex, 0, 23);
                model.Items = PlaylistItems.ToList();
                JsonStorage<PlaylistModel>.StoreData(Constants.CommonPaths.PlaylistPath, model);
            });
        }

        #endregion //playlist

        public void UpdateSettingsConfigFile()
        {
            _ = dispatcher.TryEnqueue(userSettings.Save<SettingsModel>);
        }

        private async Task WallpaperRestart(WallpaperType[] type)
        {
            var originalWallpapers = desktopCore.Wallpapers.Where(x => type.Any(y => y == x.Category)).ToList();
            if (originalWallpapers.Any())
            {
                foreach (var item in type)
                {
                    await desktopCore.CloseWallpaper(item);
                }

                foreach (var item in originalWallpapers)
                {
                    await desktopCore.SetWallpaper(item.LivelyInfoFolderPath, item.Display.DeviceId);
                    if (userSettings.Settings.WallpaperArrangement == WallpaperArrangement.span
                        || userSettings.Settings.WallpaperArrangement == WallpaperArrangement.duplicate)
                    {
                        break;
                    }
                }
            }
        }
    }
}
