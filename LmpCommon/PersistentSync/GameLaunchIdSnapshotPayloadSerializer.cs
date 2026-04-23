namespace LmpCommon.PersistentSync
{
    /// <summary>Wire snapshot for <see cref="PersistentSyncDomainId.GameLaunchId"/> (4-byte unsigned).</summary>
    public static class GameLaunchIdSnapshotPayloadSerializer
    {
        public static byte[] Serialize(uint launchId)
        {
            return System.BitConverter.GetBytes(launchId);
        }

        public static uint Deserialize(byte[] payload, int numBytes)
        {
            if (numBytes == sizeof(uint))
            {
                return System.BitConverter.ToUInt32(payload, 0);
            }

            var copied = new byte[sizeof(uint)];
            System.Buffer.BlockCopy(payload, 0, copied, 0, System.Math.Min(numBytes, sizeof(uint)));
            return System.BitConverter.ToUInt32(copied, 0);
        }
    }
}
