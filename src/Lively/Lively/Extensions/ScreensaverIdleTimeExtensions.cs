using Lively.Models.Enums;

namespace Lively.Extensions
{
    public static class ScreensaverIdleTimeExtensions
    {
        public static uint ToMilliseconds(this ScreensaverIdleTime idleTime)
        {
            return (uint)idleTime * 60 * 1000;
        }
    }
}
