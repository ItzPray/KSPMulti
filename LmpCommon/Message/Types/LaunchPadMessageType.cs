namespace LmpCommon.Message.Types
{
    public enum LaunchPadMessageType : ushort
    {
        OccupancySnapshot = 0,
        LaunchDenied = 1,
        OccupancyDelta = 2,
        ReserveSiteReply = 3,
    }
}
