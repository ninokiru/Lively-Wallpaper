namespace Lively.Models.Enums;

/// <summary>
/// Defines how wallpaper audio is routed across multiple displays.
/// </summary>
public enum DisplayAudioMode
{
    /// <summary>
    /// Play audio only on a user selected display.
    /// </summary>
    selection,
    /// <summary>
    /// Play audio on all active displays.
    /// </summary>
    all
}
