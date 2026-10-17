using Lively.Models.LivelyControls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Lively.Common.Helpers
{
    /// <summary>
    /// Helpers for the shared media LivelyProperties template.
    /// </summary>
    public static class MediaPropertyUtil
    {
        /// <summary>
        /// Sliders that can only be rendered by the player, the shell wallpaper api
        /// paints the file as it is and cannot filter, move or react to anything.
        /// </summary>
        private static readonly HashSet<string> interactiveKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "blur", "vignette", "chromatic", "ripple", "parallax", "audio", "temperature", "gamma", "hue", "saturation", "brightness", "contrast"
        };

        /// <summary>
        /// True for the sliders the shell wallpaper api cannot honour, only the player can.
        /// </summary>
        public static bool IsInteractive(string name) =>
            !string.IsNullOrEmpty(name) && interactiveKeys.Contains(name);

        /// <summary>
        /// True when a media wallpaper asks for a value only the host page can apply.
        /// </summary>
        public static bool RequiresPlayer(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath) || !File.Exists(propertyPath))
                return false;

            try
            {
                foreach (var control in LivelyPropertyUtil.GetControls(propertyPath).Values)
                {
                    if (control is SliderModel slider
                        && IsInteractive(slider.Name)
                        && Math.Abs(slider.Value) > 0.001)
                        return true;
                }
            }
            catch (Exception ex)
            {
                // A broken property file must not stop the wallpaper from starting.
                Debug.WriteLine($"Failed to read media properties: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Adds the entries a wallpaper copy is missing after the shipped template gains
        /// new controls, keeping every value the user already set.
        /// </summary>
        public static int MergeMissingEntries(string templatePath, IEnumerable<string> copyPaths)
        {
            if (!File.Exists(templatePath))
                return 0;

            var template = File.ReadAllText(templatePath);
            var missing = Newtonsoft.Json.Linq.JObject.Parse(template);
            var updated = 0;
            foreach (var path in copyPaths)
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var copy = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    var changed = false;
                    foreach (var entry in missing.Properties())
                    {
                        if (copy.ContainsKey(entry.Name))
                            continue;

                        copy[entry.Name] = entry.Value;
                        changed = true;
                    }

                    if (changed)
                    {
                        File.WriteAllText(path, copy.ToString(Newtonsoft.Json.Formatting.Indented));
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to refresh {path}: {ex.Message}");
                }
            }

            return updated;
        }
    }
}
