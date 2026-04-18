namespace LmpCommon.Enums
{
    public enum HandshakeReply
    {
        HandshookSuccessfully = 0,
        PlayerBanned = 1,
        ServerFull = 2,
        InvalidPlayername = 3,
        /// <summary>Client protocol/fork identifier does not match the server.</summary>
        ProtocolForkMismatch = 4,
        /// <summary>Client exact build does not match the server (same fork, different build).</summary>
        ProtocolBuildMismatch = 5,
    }
}
