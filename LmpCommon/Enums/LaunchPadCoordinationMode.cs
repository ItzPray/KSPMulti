namespace LmpCommon.Enums
{
    /// <summary>
    /// Server policy for coordinating multiple launch pads (e.g. KSC Enhanced).
    /// </summary>
    public enum LaunchPadCoordinationMode : byte
    {
        Off = 0,
        /// <summary>Block overlapping pad use; optional UI grey-out on client.</summary>
        LockPads = 1,
        /// <summary>When all known pads are busy, raise effective safety bubble and allow launch.</summary>
        LockAndOverflowBubble = 2,
    }
}
