using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common.Factories;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    public partial class SettingsPerformanceViewModel : ObservableObject
    {
        // Matches the order of the items declared in SettingsPerformanceView.xaml.
        internal static readonly int[] FpsLimitSteps = [0, 120, 60, 30, 24, 15];

        private readonly IDialogService dialogService;
        private readonly IUserSettingsClient userSettings;
        private readonly IDispatcherService dispatcher;
        private readonly IApplicationsRulesFactory appRuleFactory;
        private readonly IDisplayManagerClient displayManager;
        private readonly IDesktopCoreClient desktopCore;

        public SettingsPerformanceViewModel(
            IUserSettingsClient userSettings,
            IDialogService dialogService,
            IDispatcherService dispatcher,
            IApplicationsRulesFactory appRuleFactory,
            IDisplayManagerClient displayManager,
            IDesktopCoreClient desktopCore)
        {
            this.userSettings = userSettings;
            this.dialogService = dialogService;
            this.dispatcher = dispatcher;
            this.appRuleFactory = appRuleFactory;
            this.displayManager = displayManager;
            this.desktopCore = desktopCore;

            SelectedAppFullScreenIndex = (int)userSettings.Settings.AppFullscreenPause;
            SelectedAppFocusIndex = (int)userSettings.Settings.AppFocusPause;
            SelectedBatteryPowerIndex = (int)userSettings.Settings.BatteryPause;
            SelectedRemoteDestopPowerIndex = (int)userSettings.Settings.RemoteDesktopPause;
            IsRestartAfterLockscreen = userSettings.Settings.IsRestartAfterLockscreen;
            SelectedPowerSaveModeIndex = (int)userSettings.Settings.PowerSaveModePause;
            SelectedDisplayPauseRuleIndex = (int)userSettings.Settings.DisplayPauseSettings;
            SelectedPauseAlgorithmIndex = (int)userSettings.Settings.ProcessMonitorAlgorithm;
            //Only pause rules are shown to user, rest is internal use.
            AppRules = new ObservableCollection<ApplicationRulesModel>(userSettings.AppRules.Where(x => x.Rule == Models.Enums.AppRules.pause));
            LoadDisplayFrameLimits();
        }

        [ObservableProperty]
        private bool isSelectedAppFocus;

        private int _selectedAppFullScreenIndex;
        public int SelectedAppFullScreenIndex
        {
            get => _selectedAppFullScreenIndex;
            set
            {
                if (userSettings.Settings.AppFullscreenPause != (AppRules)value)
                {
                    userSettings.Settings.AppFullscreenPause = (AppRules)value;
                    UpdateSettingsConfigFile();
                }
                IsSelectedAppFocus = userSettings.Settings.AppFullscreenPause != Models.Enums.AppRules.ignore;
                SetProperty(ref _selectedAppFullScreenIndex, value);
            }
        }

        private int _selectedAppFocusIndex;
        public int SelectedAppFocusIndex
        {
            get => _selectedAppFocusIndex;
            set
            {
                if (userSettings.Settings.AppFocusPause != (AppRules)value)
                {
                    userSettings.Settings.AppFocusPause = (AppRules)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedAppFocusIndex, value);
            }
        }

        private int _selectedBatteryPowerIndex;
        public int SelectedBatteryPowerIndex
        {
            get => _selectedBatteryPowerIndex;
            set
            {
                if (userSettings.Settings.BatteryPause != (AppRules)value)
                {
                    userSettings.Settings.BatteryPause = (AppRules)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedBatteryPowerIndex, value);
            }
        }

        private int _selectedPowerSaveModeIndex;
        public int SelectedPowerSaveModeIndex
        {
            get => _selectedPowerSaveModeIndex;
            set
            {
                if (userSettings.Settings.PowerSaveModePause != (AppRules)value)
                {
                    userSettings.Settings.PowerSaveModePause = (AppRules)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedPowerSaveModeIndex, value);
            }
        }

        private int _selectedRemoteDestopPowerIndex;
        public int SelectedRemoteDestopPowerIndex
        {
            get => _selectedRemoteDestopPowerIndex;
            set
            {
                if (userSettings.Settings.RemoteDesktopPause != (AppRules)value)
                {
                    userSettings.Settings.RemoteDesktopPause = (AppRules)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedRemoteDestopPowerIndex, value);
            }
        }

        private bool _isRestartAfterLockscreen;
        public bool IsRestartAfterLockscreen
        {
            get => _isRestartAfterLockscreen;
            set
            {
                if (userSettings.Settings.IsRestartAfterLockscreen != value)
                {
                    userSettings.Settings.IsRestartAfterLockscreen = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isRestartAfterLockscreen, value);
            }
        }

        private int _selectedDisplayPauseRuleIndex;
        public int SelectedDisplayPauseRuleIndex
        {
            get => _selectedDisplayPauseRuleIndex;
            set
            {
                if (userSettings.Settings.DisplayPauseSettings != (DisplayPause)value)
                {
                    userSettings.Settings.DisplayPauseSettings = (DisplayPause)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedDisplayPauseRuleIndex, value);
            }
        }

        private int _selectedPauseAlgorithmIndex;
        public int SelectedPauseAlgorithmIndex
        {
            get => _selectedPauseAlgorithmIndex;
            set
            {
                if (userSettings.Settings.ProcessMonitorAlgorithm != (ProcessMonitorAlgorithm)value)
                {
                    userSettings.Settings.ProcessMonitorAlgorithm = (ProcessMonitorAlgorithm)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedPauseAlgorithmIndex, value);
            }
        }

        #region apprules

        [ObservableProperty]
        private ObservableCollection<ApplicationRulesModel> appRules = [];

        private ApplicationRulesModel _selectedAppRuleItem;
        public ApplicationRulesModel SelectedAppRuleItem
        {
            get => _selectedAppRuleItem;
            set
            {
                SetProperty(ref _selectedAppRuleItem, value);
                RemoveAppRuleCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private async Task AddAppRule()
        {
            var result = await dialogService.ShowApplicationPickerDialogAsync();
            if (result is null)
                return;

            try
            {
                var rule = appRuleFactory.CreateAppPauseRule(result.AppPath, Models.Enums.AppRules.pause);
                if (AppRules.Any(x => x.AppName.Equals(rule.AppName, StringComparison.Ordinal)))
                    return;

                userSettings.AppRules.Add(rule);
                AppRules.Add(rule);
                UpdateAppRulesConfigFile();
            }
            catch { /* Failed to parse program information, ignore. */ }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveAppRule))]
        private void RemoveAppRule()
        {
            userSettings.AppRules.Remove(SelectedAppRuleItem);
            AppRules.Remove(SelectedAppRuleItem);
            UpdateAppRulesConfigFile();
        }

        private bool CanRemoveAppRule => SelectedAppRuleItem != null;

        #endregion //apprules

        #region frame limit

        [ObservableProperty]
        private ObservableCollection<DisplayFrameLimitItem> displayFrameLimits = new();

        private void LoadDisplayFrameLimits()
        {
            var saved = DisplayPerformanceUtil.Load();
            DisplayFrameLimits = new ObservableCollection<DisplayFrameLimitItem>();
            foreach (var display in displayManager.DisplayMonitors)
            {
                var limit = saved
                    .FirstOrDefault(x => string.Equals(x?.DeviceId, display.DeviceId, StringComparison.OrdinalIgnoreCase))
                    ?.FpsLimit ?? 0;
                DisplayFrameLimits.Add(new DisplayFrameLimitItem(this, display, limit));
            }
        }

        /// <summary>
        /// Writes the per screen budget and restarts whatever is on that screen so the
        /// new frame rate takes effect without waiting for the next wallpaper change.
        /// </summary>
        internal async void UpdateFpsLimit(string deviceId, int fpsLimit)
        {
            try
            {
                var rules = DisplayPerformanceUtil.Load();
                var rule = rules.FirstOrDefault(x => string.Equals(x?.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (rule is null)
                {
                    rule = new DisplayPerformanceModel { DeviceId = deviceId };
                    rules.Add(rule);
                }
                if (rule.FpsLimit == fpsLimit)
                    return;

                rule.FpsLimit = fpsLimit;
                DisplayPerformanceUtil.Save(rules);

                var target = desktopCore.Wallpapers
                    .FirstOrDefault(x => string.Equals(x?.Display?.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (target is not null)
                    await desktopCore.SetWallpaper(target.LivelyInfoFolderPath, deviceId);
            }
            catch (Exception ex)
            {
                // A failed restart leaves the saved value for the next wallpaper change.
                System.Diagnostics.Debug.WriteLine($"Failed to apply frame limit: {ex.Message}");
            }
        }

        #endregion //frame limit

        public void UpdateSettingsConfigFile()
        {
            _ = dispatcher.TryEnqueue(userSettings.Save<SettingsModel>);
        }

        public void UpdateAppRulesConfigFile()
        {
            _ = dispatcher.TryEnqueue(userSettings.Save<List<ApplicationRulesModel>>);
        }
    }

    /// <summary>
    /// One row of the per screen frame rate list.
    /// </summary>
    public partial class DisplayFrameLimitItem : ObservableObject
    {
        private readonly SettingsPerformanceViewModel owner;

        public string DeviceId { get; }
        public string DisplayName { get; }

        public DisplayFrameLimitItem(SettingsPerformanceViewModel owner, DisplayMonitor display, int fpsLimit)
        {
            this.owner = owner;
            DeviceId = display.DeviceId;
            DisplayName = $"{display.Index + 1} - {display.Bounds.Width}x{display.Bounds.Height}";
            // Seeded without going through the property so loading never saves or restarts.
            _selectedFpsIndex = Array.IndexOf(SettingsPerformanceViewModel.FpsLimitSteps, fpsLimit);
            if (_selectedFpsIndex < 0)
                _selectedFpsIndex = 0;
        }

        private int _selectedFpsIndex;
        public int SelectedFpsIndex
        {
            get => _selectedFpsIndex;
            set
            {
                if (SetProperty(ref _selectedFpsIndex, value) &&
                    value >= 0 && value < SettingsPerformanceViewModel.FpsLimitSteps.Length)
                {
                    owner.UpdateFpsLimit(DeviceId, SettingsPerformanceViewModel.FpsLimitSteps[value]);
                }
            }
        }
    }
}
