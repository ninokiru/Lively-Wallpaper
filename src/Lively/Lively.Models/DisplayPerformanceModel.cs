namespace Lively.Models
{
    /// <summary>
    /// Per screen rendering budget, stored in DisplayPerformance.json.
    /// The settings window writes it and the core reads it when a wallpaper is created.
    /// </summary>
    public class DisplayPerformanceModel
    {
        public string DeviceId { get; set; }

        /// <summary>
        /// Friendly monitor label shown in the settings list.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Frames per second the wallpaper is allowed to animate at, 0 removes the limit.
        /// </summary>
        public int FpsLimit { get; set; }
    }
}
