namespace LmpCommon.PersistentSync
{
    public static class FundsSnapshotPayloadSerializer
    {
        public static byte[] Serialize(double funds)
        {
            return System.BitConverter.GetBytes(funds);
        }

        public static double Deserialize(byte[] payload, int numBytes)
        {
            if (numBytes == sizeof(double))
            {
                return System.BitConverter.ToDouble(payload, 0);
            }

            var copiedPayload = new byte[sizeof(double)];
            System.Buffer.BlockCopy(payload, 0, copiedPayload, 0, sizeof(double));
            return System.BitConverter.ToDouble(copiedPayload, 0);
        }
    }
}
