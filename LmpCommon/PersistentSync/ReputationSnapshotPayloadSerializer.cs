namespace LmpCommon.PersistentSync
{
    public static class ReputationSnapshotPayloadSerializer
    {
        public static byte[] Serialize(float reputation)
        {
            return System.BitConverter.GetBytes(reputation);
        }

        public static float Deserialize(byte[] payload, int numBytes)
        {
            if (numBytes == sizeof(float))
            {
                return System.BitConverter.ToSingle(payload, 0);
            }

            var copiedPayload = new byte[sizeof(float)];
            System.Buffer.BlockCopy(payload, 0, copiedPayload, 0, sizeof(float));
            return System.BitConverter.ToSingle(copiedPayload, 0);
        }
    }
}
