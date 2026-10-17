using Lively.Models.Services;
using System;
using System.Threading.Tasks;

namespace Lively.Common.Services
{
    public interface IAppUpdaterService
    {
        DateTime LastCheckTime { get; }
        Uri LastCheckUri { get; }
        string LastCheckFileName {get;}
        Version LastCheckVersion { get; }
        /// <summary>
        /// Release notes of the newest published release, markdown as stored on GitHub.
        /// </summary>
        string LastCheckChangelog { get; }
        AppUpdateStatus Status { get; }

        event EventHandler<AppUpdaterEventArgs> UpdateChecked;

        Task<AppUpdateStatus> CheckUpdate(int fetchDelay);
        Task<(Uri Url, string FileName, Version AppVersion)> GetLatestRelease(bool isBeta);
        void Start();
        void Stop();
    }
}