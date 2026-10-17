using Lively.Common.Helpers;
using Lively.Models.Services;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace Lively.Common.Services
{
    public sealed class GithubUpdaterService : IAppUpdaterService
    {
        //in milliseconds
        private readonly int fetchDelayError = 30 * 60 * 1000; //30min
        private readonly int fetchDelayRepeat = 12 * 60 * 60 * 1000; //12hr
        private readonly Timer retryTimer = new Timer();
        private static Architecture ProcessArch => RuntimeInformation.ProcessArchitecture;

        //public
        public AppUpdateStatus Status { get; private set; } = AppUpdateStatus.notchecked;
        public DateTime LastCheckTime { get; private set; } = DateTime.MinValue;
        public Version LastCheckVersion { get; private set; } = new Version(0, 0, 0, 0);
        public string LastCheckChangelog { get; private set; }
        public Uri LastCheckUri { get; private set; }
        public string LastCheckFileName { get; private set; }

        public event EventHandler<AppUpdaterEventArgs> UpdateChecked;

        public GithubUpdaterService()
        {
            retryTimer.Elapsed += RetryTimer_Elapsed;
            //giving the retry delay is not reliable since it will reset if system sleeps/suspends.
            retryTimer.Interval = 5 * 60 * 1000;

            Debug.WriteLine("App Update checking is disabled in DEBUG mode.");
        }

        /// <summary>
        /// Check for updates periodically.
        /// </summary>
        public void Start()
        {
            retryTimer.Start();
        }

        /// <summary>
        /// Stops periodic updates check.
        /// </summary>
        public void Stop()
        {
            if (retryTimer.Enabled)
            {
                retryTimer.Stop();
            }
        }

        private void RetryTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if ((DateTime.Now - LastCheckTime).TotalMilliseconds > (Status != AppUpdateStatus.error ? fetchDelayRepeat : fetchDelayError))
            {
                _ = CheckUpdate(0);
            }
        }

        public async Task<AppUpdateStatus> CheckUpdate(int fetchDelay)
        {
            // msix already has built-in updater Or skip on debugging.
            if (PackageUtil.IsRunningAsPackaged || BuildInfoUtil.IsDebugBuild())
                return AppUpdateStatus.notchecked;

            ReleaseInfo info = null;
            bool fetched = false;
            try
            {
                await Task.Delay(fetchDelay);
                info = await GetReleaseInfo();
                fetched = true;
                if (info is null)
                {
                    // Nothing published on the release page yet, so there is simply nothing to update to.
                    Status = AppUpdateStatus.uptodate;
                }
                else
                {
                    int verCompare = GithubUtil.CompareAssemblyVersion(info.Version);
                    if (verCompare > 0)
                    {
                        //update available.
                        Status = AppUpdateStatus.available;
                    }
                    else if (verCompare < 0)
                    {
                        //beta release.
                        Status = AppUpdateStatus.invalid;
                    }
                    else
                    {
                        //up-to-date.
                        Status = AppUpdateStatus.uptodate;
                    }

                    if (Status == AppUpdateStatus.available && info.Url is null)
                    {
                        // A newer tag exists but carries no installer for this architecture,
                        // which the ui cannot recover from on its own.
                        Debug.WriteLine($"Release {info.Tag} has no setup asset for {ProcessArch}.");
                        Status = AppUpdateStatus.error;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Update fetch error:" + e.ToString());
                Status = AppUpdateStatus.error;
            }

            // A failed lookup keeps whatever the last successful one found, so the
            // changelog card does not go blank just because the network dropped.
            // A fetch that succeeded but found nothing published clears the stale data.
            if (fetched)
            {
                LastCheckUri = info?.Url;
                LastCheckFileName = info?.FileName;
                LastCheckChangelog = info?.Changelog;
                LastCheckVersion = info?.Version ?? new Version(0, 0, 0, 0);
            }
            LastCheckTime = DateTime.Now;

            UpdateChecked?.Invoke(this,
                new AppUpdaterEventArgs(Status,
                    LastCheckVersion,
                    LastCheckTime,
                    LastCheckUri,
                    LastCheckFileName));
            return Status;
        }

        /// <summary>
        /// isBeta is kept for the rpc contract; this fork publishes a single channel.
        /// </summary>
        public async Task<(Uri Url, string FileName, Version AppVersion)> GetLatestRelease(bool isBeta)
        {
            var info = await GetReleaseInfo();
            return info is null ? (null, null, null) : (info.Url, info.FileName, info.Version);
        }

        private sealed class ReleaseInfo
        {
            public string Tag;
            public string Changelog;
            public Version Version;
            public Uri Url;
            public string FileName;
        }

        private static async Task<ReleaseInfo> GetReleaseInfo()
        {
            var release = await GithubUtil.GetLatestRelease(Constants.ApplicationType.UpdateRepoOwner,
                Constants.ApplicationType.UpdateRepoName);
            if (release is null)
                return null;

            var info = new ReleaseInfo
            {
                Tag = release.TagName,
                Version = GithubUtil.GetVersion(release),
                Changelog = release.Body,
            };

            // Installer name produced by Script_x64.iss: luma_setup_ARCH_full_vXXXX.exe.
            // The lively_ prefix is still accepted so an older release can be installed,
            // and the x86 spelling covers the builds published while every x64 installer
            // was named that way.
            var arch = GetArchSetupString(ProcessArch);
            var names = ProcessArch == Architecture.X64
                ? new[]
                {
                    $"luma_setup_{arch}_full",
                    $"lively_setup_{arch}_full",
                    "luma_setup_x86_full",
                    "lively_setup_x86_full",
                }
                : new[]
                {
                    $"luma_setup_{arch}_full",
                    $"lively_setup_{arch}_full",
                };

            foreach (var prefix in names)
            {
                var (fileName, url) = GithubUtil.GetAssetUrl(release, prefix);
                if (fileName is not null)
                {
                    info.FileName = fileName;
                    info.Url = url is null ? null : new Uri(url);
                    break;
                }
            }

            return info;
        }

        private static string GetArchSetupString(Architecture arch)
        {
            return arch switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new NotImplementedException(),
            };
        }
    }
}
