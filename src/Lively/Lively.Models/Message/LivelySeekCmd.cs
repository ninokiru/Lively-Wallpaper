using System;

namespace Lively.Models.Message
{
    [Serializable]
    public class LivelySeekCmd : IpcMessage
    {
        /// <summary>
        /// Playback position, range 0 - 100.
        /// </summary>
        public float Percent { get; set; }
        /// <summary>
        /// Offset from the current position instead of an absolute one.
        /// </summary>
        public bool IsRelative { get; set; }
        public LivelySeekCmd() : base(MessageType.cmd_seek)
        {
        }
    }
}
