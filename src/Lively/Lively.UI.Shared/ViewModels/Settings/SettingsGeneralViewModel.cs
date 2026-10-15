using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common;
using Lively.Common.Helpers.Files;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    public partial class SettingsGeneralViewModel : ObservableObject
    {
        private readonly IUserSettingsClient userSettings;
        private readonly IDesktopCoreClient desktopCore;
        private readonly IDisplayManagerClient displayManager;
        private readonly IDialogService dialogService;
        private readonly LibraryViewModel libraryVm;
        private readonly IResourceService i18n;
        private readonly IDispatcherService dispatcher;
        private readonly IFileService fileService;

        public SettingsGeneralViewModel(LibraryViewModel libraryVm, IDesktopCoreClient desktopCore,
            IUserSettingsClient userSettings,
            IDisplayManagerClient displayManager,
            IDispatcherService dispatcher,
            IFileService fileService,
            IResourceService i18n,
            IDialogService dialogService)
        {
            this.libraryVm = libraryVm;
            this.desktopCore = desktopCore;
            this.displayManager = displayManager;
            this.userSettings = userSettings;
            this.dialogService = dialogService;
            this.dispatcher = dispatcher;
            this.fileService = fileService;
            this.i18n = i18n;

            Languages = new ObservableCollection<LanguageModel>(Common.Languages.SupportedLanguages);
            Languages.Insert(0, new(i18n.GetString("LanguageSettingsItemSystemDefault/Content"), string.Empty));
            SelectedLanguage = string.IsNullOrWhiteSpace(userSettings.Settings.Language) ? 
                Languages[0] : Languages.FirstOrDefault(x => x.Code == userSettings.Settings.Language) ?? Languages[0];

            IsStartup = userSettings.Settings.Startup;
            WallpaperDirectory = userSettings.Settings.WallpaperDir;
            IsSysTrayIconVisible = userSettings.Settings.SysTrayIcon;
            GlobalWallpaperVolume = userSettings.Settings.AudioVolumeGlobal;
            IsAudioOnlyOnDesktop = userSettings.Settings.AudioOnlyOnDesktop;
            IsReducedMotion = userSettings.Settings.UIMode != LivelyGUIState.normal;
            MoveExistingWallpaperNewDir = userSettings.Settings.WallpaperDirMoveExistingWallpaperNewDir;
            SelectedDisplayAudioOutputIndex = (int)userSettings.Settings.DisplayAudioOutput;
            UpdateAudioOutputScreens();

            displayManager.DisplayChanged += DisplayManager_DisplayChanged;
        }

        private bool _isStartup;
        public bool IsStartup
        {
            get => _isStartup;
            set
            {
                if (userSettings.Settings.Startup != value)
                {
                    userSettings.Settings.Startup = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isStartup, value);
            }
        }

        public ObservableCollection<LanguageModel> Languages { get; }

        private LanguageModel _selectedLanguage;
        public LanguageModel SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                string code = value == Languages[0] ?  string.Empty : value?.Code;
                if (code != null && userSettings.Settings.Language != code)
                {
                    userSettings.Settings.Language = code;
                    UpdateSettingsConfigFile();
                    // This will reload the page and vm (transient) via INavigator.
                    i18n.SetCulture(code);
                }
                SetProperty(ref _selectedLanguage, value);
            }
        }

        private bool _isSysTrayIconVisible;
        public bool IsSysTrayIconVisible
        {
            get => _isSysTrayIconVisible;
            set
            {
                if (userSettings.Settings.SysTrayIcon != value)
                {
                    userSettings.Settings.SysTrayIcon = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isSysTrayIconVisible, value);
            }
        }

        private bool _isReducedMotion;
        public bool IsReducedMotion
        {
            get => _isReducedMotion;
            set
            {
                var currentValue = userSettings.Settings.UIMode != LivelyGUIState.normal;
                if (currentValue != value)
                {
                    userSettings.Settings.UIMode = value ? LivelyGUIState.lite : LivelyGUIState.normal;
                    libraryVm.UpdateAnimationSettings(userSettings.Settings.UIMode);
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isReducedMotion, value);
            }
        }

        private int _globalWallpaperVolume;
        public int GlobalWallpaperVolume
        {
            get => _globalWallpaperVolume;
            set
            {
                if (userSettings.Settings.AudioVolumeGlobal != value)
                {
                    userSettings.Settings.AudioVolumeGlobal = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _globalWallpaperVolume, value);
            }
        }

        private bool _isAudioOnlyOnDesktop;
        public bool IsAudioOnlyOnDesktop
        {
            get => _isAudioOnlyOnDesktop;
            set
            {
                if (userSettings.Settings.AudioOnlyOnDesktop != value)
                {
                    userSettings.Settings.AudioOnlyOnDesktop = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isAudioOnlyOnDesktop, value);
            }
        }

        private int _selectedDisplayAudioOutputIndex;
        public int SelectedDisplayAudioOutputIndex
        {
            get => _selectedDisplayAudioOutputIndex;
            set
            {
                if (value < 0)
                    return;

                if (userSettings.Settings.DisplayAudioOutput != (DisplayAudioMode)value)
                {
                    userSettings.Settings.DisplayAudioOutput = (DisplayAudioMode)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedDisplayAudioOutputIndex, value);
                IsAudioOutputScreenSelection = (DisplayAudioMode)value == DisplayAudioMode.selection;
            }
        }

        [ObservableProperty]
        private bool isAudioOutputScreenSelection;

        [ObservableProperty]
        private ObservableCollection<ScreenLayoutModel> audioOutputscreens = [];

        private ScreenLayoutModel _selectedAudioOutputScreen;
        public ScreenLayoutModel SelectedAudioOutputScreen
        {
            get => _selectedAudioOutputScreen;
            set
            {
                if (value is null)
                    return;

                SetProperty(ref _selectedAudioOutputScreen, value);
                if (!userSettings.Settings.SelectedAudioOutputDisplay.Equals(value.Screen))
                {
                    userSettings.Settings.SelectedAudioOutputDisplay = value.Screen;
                    UpdateSettingsConfigFile();
                }
            }
        }

        [RelayCommand]
        private async Task ThemeBackground()
        {
            await dialogService.ShowThemeDialogAsync();
        }

        [ObservableProperty]
        private string wallpaperDirectory;

        private bool _moveExistingWallpaperNewDir;
        public bool MoveExistingWallpaperNewDir
        {
            get => _moveExistingWallpaperNewDir;
            set
            {
                if (userSettings.Settings.WallpaperDirMoveExistingWallpaperNewDir != value)
                {
                    userSettings.Settings.WallpaperDirMoveExistingWallpaperNewDir = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _moveExistingWallpaperNewDir, value);
            }
        }

        [ObservableProperty]
        private bool wallpaperDirectoryChangeOngoing;

        [RelayCommand]
        private async Task OpenWallpaperDirectory()
        {
            await fileService.OpenFolderAsync(userSettings.Settings.WallpaperDir);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDirectoryChange))]
        private async Task WallpaperDirectoryChange()
        {
            var folder = await fileService.PickFolderAsync(["*"]);
            if (folder is null)
                return;

            await WallpaperDirectoryChange(folder);
        }

        private bool CanExecuteDirectoryChange() => !WallpaperDirectoryChangeOngoing;

        public async Task WallpaperDirectoryChange(string newDir)
        {
            bool isDestEmptyDir = false;
            if (string.Equals(newDir, userSettings.Settings.WallpaperDir, StringComparison.OrdinalIgnoreCase))
            {
                // Refresh request.
                libraryVm.UpdateWallpaperDirectory(userSettings.Settings.WallpaperDir);
                return;
            }

            try
            {
                var parentDir = Directory.GetParent(newDir).ToString();
                if (parentDir != null)
                {
                    if (Directory.Exists(Path.Combine(parentDir, Constants.CommonPartialPaths.WallpaperInstallDir)) &&
                        Directory.Exists(Path.Combine(parentDir, Constants.CommonPartialPaths.WallpaperSettingsDir)))
                    {
                        //User selected wrong directory, lively needs the SaveData folder also(root).
                        newDir = parentDir;
                    }
                }

                WallpaperDirectoryChangeOngoing = true;
                WallpaperDirectoryChangeCommand.NotifyCanExecuteChanged();
                //create destination directory's if not exist.
                Directory.CreateDirectory(Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperInstallDir));
                Directory.CreateDirectory(Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperInstallTempDir));
                Directory.CreateDirectory(Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperSettingsDir));

                if (userSettings.Settings.WallpaperDirMoveExistingWallpaperNewDir)
                {
                    await Task.Run(() =>
                    {
                        FileUtil.DirectoryCopy(Path.Combine(WallpaperDirectory, Constants.CommonPartialPaths.WallpaperInstallDir),
                            Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperInstallDir), true);
                        FileUtil.DirectoryCopy(Path.Combine(WallpaperDirectory, Constants.CommonPartialPaths.WallpaperInstallTempDir),
                            Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperInstallTempDir), true);
                        FileUtil.DirectoryCopy(Path.Combine(WallpaperDirectory, Constants.CommonPartialPaths.WallpaperSettingsDir),
                            Path.Combine(newDir, Constants.CommonPartialPaths.WallpaperSettingsDir), true);
                    });
                }
                else
                {
                    isDestEmptyDir = true;
                }
            }
            catch (Exception)
            {
                //TODO: log
                return;
            }
            finally
            {
                WallpaperDirectoryChangeOngoing = false;
                WallpaperDirectoryChangeCommand.NotifyCanExecuteChanged();
            }

            //exit all running wp's immediately
            await desktopCore.CloseAllWallpapers();

            var previousDirectory = userSettings.Settings.WallpaperDir;
            userSettings.Settings.WallpaperDir = newDir;
            UpdateSettingsConfigFile();
            WallpaperDirectory = userSettings.Settings.WallpaperDir;
            libraryVm.UpdateWallpaperDirectory(newDir);

            if (!isDestEmptyDir)
            {
                //not deleting the root folder, what if the user selects a folder that is not used by Lively alone!
                var result1 = await FileUtil.TryDeleteDirectoryAsync(Path.Combine(previousDirectory, Constants.CommonPartialPaths.WallpaperInstallDir), 1000, 3000);
                var result2 = await FileUtil.TryDeleteDirectoryAsync(Path.Combine(previousDirectory, Constants.CommonPartialPaths.WallpaperInstallTempDir), 0, 1000);
                var result3 = await FileUtil.TryDeleteDirectoryAsync(Path.Combine(previousDirectory, Constants.CommonPartialPaths.WallpaperSettingsDir), 0, 1000);
                if (!(result1 && result2 && result3))
                {
                    //TODO: Dialogue
                }
            }
        }

        public void OnClose()
        {
            displayManager.DisplayChanged -= DisplayManager_DisplayChanged;
        }

        private void DisplayManager_DisplayChanged(object sender, EventArgs e)
        {
            dispatcher.TryEnqueue(UpdateAudioOutputScreens);
        }

        private void UpdateAudioOutputScreens()
        {
            AudioOutputscreens.Clear();
            foreach (var display in displayManager.DisplayMonitors)
            {
                AudioOutputscreens.Add(new ScreenLayoutModel(display, null, null, string.Empty));
            }
            SelectedAudioOutputScreen = AudioOutputscreens.FirstOrDefault(x => x.Screen.Equals(userSettings.Settings.SelectedAudioOutputDisplay))
                ?? AudioOutputscreens.FirstOrDefault(x => x.Screen.IsPrimary);
        }

        public void UpdateSettingsConfigFile()
        {
            dispatcher.TryEnqueue(userSettings.Save<SettingsModel>);
        }
    }
}
