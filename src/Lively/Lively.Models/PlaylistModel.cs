using System.Collections.Generic;

namespace Lively.Models
{
    /// <summary>
    /// Day filter of a playlist window.
    /// </summary>
    public enum PlaylistDays
    {
        everyDay = 0,
        weekdays = 1,
        weekends = 2,
    }

    /// <summary>
    /// One wallpaper inside a playlist.
    /// </summary>
    public class PlaylistItemModel
    {
        /// <summary>
        /// Folder that holds lively.info, stored absolute because that is what the library scan returns.
        /// </summary>
        public string LivelyInfoFolderPath { get; set; }

        /// <summary>
        /// Cached title, only used to render the playlist list without touching the disk.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Target screen; null or empty follows the monitor that is currently selected in the app.
        /// </summary>
        public string MonitorDeviceId { get; set; }
    }

    /// <summary>
    /// Timed wallpaper rotation, stored in its own file (Playlist.json) instead of Settings.json
    /// so the core can re-read it while the settings window edits it.
    /// </summary>
    public class PlaylistModel
    {
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Minutes between two switches, clamped by the service.
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;

        public bool IsShuffle { get; set; }

        /// <summary>
        /// Inclusive start hour (0-23).
        /// </summary>
        public int StartHour { get; set; }

        /// <summary>
        /// Exclusive end hour (0-23); equal to <see cref="StartHour"/> means the whole day.
        /// </summary>
        public int EndHour { get; set; }

        public PlaylistDays Days { get; set; } = PlaylistDays.everyDay;

        public List<PlaylistItemModel> Items { get; set; } = new List<PlaylistItemModel>();
    }
}
