using System;
using System.Collections.Generic;

namespace Lively.Core
{
    /// <summary>
    /// Limits how often a wallpaper can be relaunched after its player process dies,
    /// so a wallpaper that crashes on start cannot restart forever.
    /// </summary>
    public class WallpaperCrashGuard
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(5);

        private readonly Dictionary<string, (int Attempts, DateTime LastRestart)> records = [];

        /// <summary>
        /// How long to wait before relaunching, or <see langword="null"/> when the wallpaper gave up.
        /// </summary>
        public TimeSpan? TryScheduleRestart(string key)
        {
            if (!records.TryGetValue(key, out var record)
                || DateTime.UtcNow - record.LastRestart > ResetWindow)
                record = default;

            record.LastRestart = DateTime.UtcNow;

            if (record.Attempts >= MaxAttempts)
            {
                records[key] = record;
                return null;
            }

            record.Attempts++;
            records[key] = record;

            // 1s, 3s, 9s
            return TimeSpan.FromSeconds(Math.Pow(3, record.Attempts - 1));
        }

        /// <summary>
        /// Forget past failures once the wallpaper is known to be running.
        /// </summary>
        public void Clear(string key)
        {
            records.Remove(key);
        }
    }
}
