using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models.Enums;
using Lively.Models.Services;
using Lively.UI.Shared.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// System update page: check, install, read what changed.
    /// The page is modelled on the Windows Update screen - one status banner with a
    /// single action at a time, and the release notes rendered natively from the
    /// changelog the core already returns, so no browser page is involved.
    /// </summary>
    public partial class AppUpdateViewModel : ObservableObject
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IResourceService i18n;
        private readonly IAppUpdaterClient appUpdater;
        private readonly IDesktopCoreClient desktopCore;
        private readonly IDownloadService downloader;
        private readonly IDialogService dialogService;
        private readonly IDispatcherService dispatcher;

        private CancellationTokenSource downloadCts;

        public AppUpdateViewModel(IAppUpdaterClient appUpdater,
            IDesktopCoreClient desktopCore,
            IDownloadService downloader,
            IDispatcherService dispatcher,
            IDialogService dialogService,
            IResourceService i18n)
        {
            this.appUpdater = appUpdater;
            this.desktopCore = desktopCore;
            this.downloader = downloader;
            this.dialogService = dialogService;
            this.dispatcher = dispatcher;
            this.i18n = i18n;

            UpdateState(appUpdater.Status, appUpdater.LastCheckTime, appUpdater.LastCheckVersion);
            appUpdater.UpdateChecked += AppUpdater_UpdateChecked;
            i18n.CultureChanged += I18n_CultureChanged;

            // Only runs once: the core checks on a delay after launch, so the page can
            // be opened before the first fetch has happened.
            if (appUpdater.Status == AppUpdateStatus.notchecked)
                _ = CheckUpdate();
        }

        public bool IsBetaBuild => Constants.ApplicationType.IsTestBuild;

        /// <summary>
        /// Version of the running build, shown under the status title.
        /// </summary>
        public string InstalledVersionText
        {
            get
            {
                var version = "v" + desktopCore.AssemblyVersion;
                return IsBetaBuild ? version + " (b)" : version;
            }
        }

        [ObservableProperty]
        private double currentProgress;

        [ObservableProperty]
        private bool isUpdateChecking;

        [ObservableProperty]
        private bool isUpdateDownloading;

        [ObservableProperty]
        private bool isUpdateInstalling;

        [ObservableProperty]
        private bool isUpdateAvailable;

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private bool showProgress;

        [ObservableProperty]
        private bool showRefreshGlyph = true;

        [ObservableProperty]
        private bool showSuccessBadge;

        [ObservableProperty]
        private bool showErrorBadge;

        [ObservableProperty]
        private string statusTitle;

        [ObservableProperty]
        private string statusDetail;

        [ObservableProperty]
        private string lastCheckedText;

        [ObservableProperty]
        private string updateLogText;

        /// <summary>
        /// Line under the progress bar, e.g. "Downloading update 42%".
        /// </summary>
        [ObservableProperty]
        private string progressText;

        [ObservableProperty]
        private bool hasUpdateLog;

        /// <summary>
        /// The banner carries the download mark until work starts, then it becomes a
        /// spinning ring - one element swapping appearance instead of two fading.
        /// </summary>
        partial void OnIsBusyChanged(bool value)
        {
            ShowRefreshGlyph = !value;
        }

        [RelayCommand]
        private async Task CheckUpdate()
        {
            // The button stays live during a fetch; a second request would only stack
            // another GitHub api call onto the same lookup.
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;
                ShowProgress = true;
                IsUpdateChecking = true;
                ProgressText = i18n.GetString("TextUpdateChecking");
                StatusTitle = i18n.GetString("TextUpdateChecking");
                await appUpdater.CheckUpdate();
            }
            catch (Exception ex)
            {
                // The core reports a failed lookup through the status stream, so this
                // only catches the rpc call itself breaking.
                Logger.Error(ex);
                ShowFailure();
            }
            finally
            {
                IsUpdateChecking = false;
                if (!IsUpdateDownloading)
                {
                    IsBusy = false;
                    ShowProgress = false;
                }
            }
        }

        /// <summary>
        /// One click update: fetches the setup when it is not on disk yet, then hands
        /// over to it. The installer closes both processes and relaunches the app.
        /// </summary>
        [RelayCommand]
        private async Task ApplyUpdate()
        {
            if (IsUpdateDownloading || IsUpdateInstalling)
                return;

            var fileName = Path.GetFileName(appUpdater.LastCheckFileName ?? string.Empty);
            if (string.IsNullOrEmpty(fileName))
            {
                ShowFailure();
                return;
            }

            var filePath = Path.Combine(Constants.CommonPaths.TempDir, fileName);
            var setupUri = appUpdater.LastCheckUri;

            try
            {
                IsBusy = true;
                ShowProgress = true;
                CurrentProgress = 0;

                if (File.Exists(filePath))
                {
                    // A previous click already fetched it, so go straight to setup.
                    CurrentProgress = 100;
                    ProgressText = i18n.GetString("UpdateInstallingTitle");
                }
                else
                {
                    if (setupUri is null)
                        throw new FileNotFoundException("No installer asset is published for this release.");

                    IsUpdateDownloading = true;
                    StatusTitle = i18n.GetString("UpdateDownloadingTitle");
                    ProgressText = i18n.GetString("UpdateDownloadingTitle");

                    downloadCts?.Dispose();
                    downloadCts = new CancellationTokenSource();
                    var token = downloadCts.Token;

                    Logger.Info($"Downloading update: {filePath}");
                    await downloader.DownloadFile(setupUri, filePath,
                        new Progress<(double downloaded, double total)>(progress =>
                        {
                            if (progress.total > 0)
                            {
                                CurrentProgress = Math.Clamp(progress.downloaded * 100 / progress.total, 0, 100);
                                ProgressText = $"{i18n.GetString("UpdateDownloadingTitle")}  {CurrentProgress:0}%";
                            }
                        }), token);

                    if (token.IsCancellationRequested)
                        return;
                }

                IsUpdateDownloading = false;
                IsUpdateInstalling = true;
                StatusTitle = i18n.GetString("UpdateInstallingTitle");
                ProgressText = i18n.GetString("UpdateInstallingTitle");

                Logger.Info("Update ready, handing over to setup");
                await appUpdater.StartUpdate();
            }
            catch (OperationCanceledException)
            {
                // Cancelled mid transfer: the partial file would only confuse the next attempt.
                TryDelete(filePath);
                UpdateState(appUpdater.Status, appUpdater.LastCheckTime, appUpdater.LastCheckVersion);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                TryDelete(filePath);
                await dialogService.ShowDialogAsync($"{i18n.GetString("LivelyExceptionAppUpdateFail")}\n\nException:\n{ex}",
                    i18n.GetString("TextError"),
                    i18n.GetString("TextOK"));
                UpdateState(appUpdater.Status, appUpdater.LastCheckTime, appUpdater.LastCheckVersion);
            }
            finally
            {
                IsUpdateDownloading = false;
                IsUpdateInstalling = false;
                IsBusy = false;
                ShowProgress = false;
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            downloadCts?.Cancel();
        }

        private void AppUpdater_UpdateChecked(object sender, AppUpdaterEventArgs e)
        {
            dispatcher.TryEnqueue(() =>
            {
                UpdateState(e.UpdateStatus, e.UpdateDate, e.UpdateVersion);
            });
        }

        private void I18n_CultureChanged(object sender, string e)
        {
            UpdateState(appUpdater.Status, appUpdater.LastCheckTime, appUpdater.LastCheckVersion);
        }

        private void ShowFailure()
        {
            IsUpdateAvailable = false;
            ShowSuccessBadge = false;
            ShowErrorBadge = true;
            StatusTitle = i18n.GetString("TextupdateCheckFail");
            StatusDetail = InstalledVersionText;
        }

        private void UpdateState(AppUpdateStatus status, DateTime date, Version version)
        {
            IsUpdateAvailable = status == AppUpdateStatus.available;
            ShowErrorBadge = status == AppUpdateStatus.error;
            // The green tick means "nothing to do": an available update is shown by its
            // own action button instead.
            ShowSuccessBadge = status == AppUpdateStatus.uptodate;
            ShowRefreshGlyph = !IsBusy;

            switch (status)
            {
                case AppUpdateStatus.uptodate:
                    StatusTitle = i18n.GetString("UpdateUpToDateTitle");
                    StatusDetail = $"{InstalledVersionText} - {i18n.GetString("UpdateUpToDateDetail")}";
                    break;
                case AppUpdateStatus.available:
                    StatusTitle = i18n.GetString("UpdateAvailableTitle");
                    StatusDetail = $"{InstalledVersionText} -> v{version}";
                    break;
                case AppUpdateStatus.invalid:
                    StatusTitle = i18n.GetString("UpdateNewerTitle");
                    StatusDetail = InstalledVersionText;
                    break;
                case AppUpdateStatus.error:
                    StatusTitle = i18n.GetString("TextupdateCheckFail");
                    StatusDetail = InstalledVersionText;
                    break;
                default:
                    StatusTitle = i18n.GetString("TextUpdateChecking");
                    StatusDetail = InstalledVersionText;
                    break;
            }

            LastCheckedText = FormatLastChecked(status, date);

            var log = FormatChangelog(appUpdater.LastCheckChangelog);
            HasUpdateLog = log.Length > 0;
            UpdateLogText = HasUpdateLog ? log : i18n.GetString("UpdateLogEmpty");
        }

        private string FormatLastChecked(AppUpdateStatus status, DateTime date)
        {
            var label = i18n.GetString("TextLastChecked");
            if (status == AppUpdateStatus.notchecked || date == default || date == DateTime.MinValue)
                return $"{label}: {i18n.GetString("UpdateLastCheckedNever")}";

            var time = date.ToString("t", CultureInfo.CurrentCulture);
            if (date.Date == DateTime.Today)
                return $"{label}: {i18n.GetString("UpdateToday")}, {time}";

            return $"{label}: {date.ToString("d", CultureInfo.CurrentCulture)}, {time}";
        }

        /// <summary>
        /// Release notes come from GitHub as markdown. The page shows them as plain text,
        /// so the markup is flattened instead of pulled into a web view.
        /// </summary>
        private static string FormatChangelog(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return string.Empty;

            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

            // Drop block separators and html the release body sometimes carries.
            text = Regex.Replace(text, @"^\s*```[^\n]*\n?", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*[-*]{3,}\s*$", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(text, @"<[^>\n]+>", string.Empty);

            // Images carry no meaning without the page, links keep their label.
            text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", string.Empty);
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");

            // Headings become plain lines, inline emphasis is removed.
            text = Regex.Replace(text, @"^\s*#{1,6}\s*", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(text, @"[*_]{1,2}([^*_\n][^*\n]*?)[*_]{1,2}", "$1");
            text = text.Replace('`', ' ');

            // Bullets read better as typographic ones, numbered items keep their number.
            text = Regex.Replace(text, @"^\s*[-*+]\s+", "• ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*(\d+[.)])\s+", "$1 ", RegexOptions.Multiline);

            var builder = new StringBuilder(text.Length);
            var blankRuns = 0;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length == 0)
                {
                    if (++blankRuns > 1)
                        continue;
                }
                else
                {
                    blankRuns = 0;
                }
                builder.Append(trimmed).Append('\n');
            }

            return builder.ToString().Trim();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
    }
}
