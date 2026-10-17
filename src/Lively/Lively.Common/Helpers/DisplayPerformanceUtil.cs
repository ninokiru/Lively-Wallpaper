using Lively.Common.Helpers.Storage;
using Lively.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Lively.Common.Helpers
{
    /// <summary>
    /// Reads the per screen rendering budget written by the settings window.
    /// The file lives outside Settings.json so the ui can edit it while the core
    /// keeps answering wallpaper creation requests.
    /// </summary>
    public static class DisplayPerformanceUtil
    {
        private static readonly object fileLock = new object();
        private static List<DisplayPerformanceModel> cache;
        private static DateTime cacheStamp = DateTime.MinValue;

        public static List<DisplayPerformanceModel> Load()
        {
            lock (fileLock)
            {
                var path = Constants.CommonPaths.DisplayPerformancePath;
                if (!File.Exists(path))
                    return new List<DisplayPerformanceModel>();

                try
                {
                    var stamp = File.GetLastWriteTimeUtc(path);
                    if (cache is not null && cacheStamp == stamp)
                        return cache;

                    cache = JsonStorage<List<DisplayPerformanceModel>>.LoadData(path)
                            ?? new List<DisplayPerformanceModel>();
                    cacheStamp = stamp;
                    return cache;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to read display performance config: {ex.Message}");
                    cache = new List<DisplayPerformanceModel>();
                    return cache;
                }
            }
        }

        public static void Save(List<DisplayPerformanceModel> rules)
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(Constants.CommonPaths.AppDataDir);
                    JsonStorage<List<DisplayPerformanceModel>>.StoreData(Constants.CommonPaths.DisplayPerformancePath, rules ?? new List<DisplayPerformanceModel>());
                    // Force the next read to pick the new file up.
                    cacheStamp = DateTime.MinValue;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to write display performance config: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 0 means unlimited.
        /// </summary>
        public static int GetFpsLimit(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return 0;

            foreach (var rule in Load())
            {
                if (string.Equals(rule?.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    return Math.Clamp(rule.FpsLimit, 0, 240);
            }

            return 0;
        }
    }
}
