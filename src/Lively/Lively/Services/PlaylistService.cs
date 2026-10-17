using Lively.Common;
using Lively.Common.Factories;
using Lively.Common.Helpers.Storage;
using Lively.Core;
using Lively.Core.Display;
using Lively.Helpers;
using Lively.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace Lively.Services
{
    public interface IPlaylistService : IDisposable
    {
        void Start();
        void Stop();
    }

    /// <summary>
    /// Rotates the wallpapers of a playlist on a clock.
    /// The schedule lives in its own file so the settings window can edit it while
    /// this service keeps reading it, without any extra rpc round trip.
    /// </summary>
    public class PlaylistService : IPlaylistService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IDesktopCore desktopCore;
        private readonly IDisplayManager displayManager;
        private readonly IWallpaperLibraryFactory wallpaperLibraryFactory;

        private readonly Timer timer;
        private readonly Random random = new Random();

        private DateTime fileStamp = DateTime.MinValue;
        private PlaylistModel config;
        // Rotations only happen on a new slot, so a wallpaper picked by hand between
        // two switches is left running until the next one.
        private int lastAppliedSlot = -1;
        private bool isBusy;

        public PlaylistService(IDesktopCore desktopCore,
            IDisplayManager displayManager,
            IWallpaperLibraryFactory wallpaperLibraryFactory)
        {
            this.desktopCore = desktopCore;
            this.displayManager = displayManager;
            this.wallpaperLibraryFactory = wallpaperLibraryFactory;

            timer = new Timer(30 * 1000)
            {
                AutoReset = true
            };
            timer.Elapsed += (s, e) => _ = TryRotateAsync();
        }

        public void Start()
        {
            // The schedule is re-read on every tick, the timer itself only starts once.
            timer.Start();
        }

        public void Stop()
        {
            timer.Stop();
        }

        private async Task TryRotateAsync()
        {
            if (isBusy)
                return;

            isBusy = true;
            try
            {
                await RotateAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
            finally
            {
                isBusy = false;
            }
        }

        private async Task RotateAsync()
        {
            var playlist = Load();
            if (playlist is null || !playlist.IsEnabled || playlist.Items is null || playlist.Items.Count == 0)
            {
                lastAppliedSlot = -1;
                return;
            }

            if (!IsWithinSchedule(DateTime.Now, playlist, out var minutesFromStart))
                return;

            var interval = Math.Clamp(playlist.IntervalMinutes, 1, 24 * 60);
            var slot = minutesFromStart / interval;
            if (slot == lastAppliedSlot)
                return;

            // A game owns the display right now, try again on the next tick.
            if (SystemIdleUtil.IsExclusiveFullScreenAppRunning())
                return;

            lastAppliedSlot = slot;
            var groups = playlist.Items
                .Where(x => !string.IsNullOrWhiteSpace(x?.LivelyInfoFolderPath))
                .GroupBy(x => x.MonitorDeviceId);

            foreach (var group in groups)
            {
                var display = ResolveDisplay(group.Key);
                if (display is null)
                    continue;

                var items = group.ToList();
                var index = playlist.IsShuffle
                    ? random.Next(items.Count)
                    : slot % items.Count;

                try
                {
                    var wallpaper = wallpaperLibraryFactory.CreateFromDirectory(items[index].LivelyInfoFolderPath);
                    await desktopCore.SetWallpaperAsync(wallpaper, display);
                    Logger.Info($"Playlist switched to {wallpaper.Title} on {display.DeviceId}.");
                }
                catch (Exception ex)
                {
                    // A removed wallpaper must not stop the rest of the playlist.
                    Logger.Error(ex);
                }
            }
        }

        /// <summary>
        /// True when the moment is inside the configured hours and days. The returned value
        /// counts minutes from the start of the window, which keeps slot numbers stable
        /// across a restart of the app.
        /// </summary>
        internal static bool IsWithinSchedule(DateTime now, PlaylistModel playlist, out int minutesFromStart)
        {
            minutesFromStart = 0;

            var day = now.DayOfWeek;
            switch (playlist.Days)
            {
                case PlaylistDays.weekdays:
                    if (day is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        return false;
                    break;
                case PlaylistDays.weekends:
                    if (day is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                        return false;
                    break;
            }

            var start = Math.Clamp(playlist.StartHour, 0, 23);
            var end = Math.Clamp(playlist.EndHour, 0, 23);
            var nowMinutes = now.Hour * 60 + now.Minute;

            // Equal hours means no window at all, the playlist runs around the clock.
            if (start == end)
            {
                minutesFromStart = nowMinutes;
                return true;
            }

            var length = (end * 60) - (start * 60);
            if (length < 0)
                length += 24 * 60;

            var offset = nowMinutes - (start * 60);
            if (offset < 0)
                offset += 24 * 60;

            if (offset >= length)
                return false;

            minutesFromStart = offset;
            return true;
        }

        private DisplayMonitor ResolveDisplay(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return displayManager.PrimaryDisplayMonitor;

            return displayManager.DisplayMonitors
                    .FirstOrDefault(x => string.Equals(x.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                ?? displayManager.PrimaryDisplayMonitor;
        }

        private PlaylistModel Load()
        {
            try
            {
                var path = Constants.CommonPaths.PlaylistPath;
                if (!File.Exists(path))
                    return null;

                var stamp = File.GetLastWriteTimeUtc(path);
                if (config is not null && fileStamp == stamp)
                    return config;

                config = JsonStorage<PlaylistModel>.LoadData(path);
                fileStamp = stamp;
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return null;
            }
        }

        public void Dispose()
        {
            timer?.Stop();
            timer?.Dispose();
        }
    }
}
