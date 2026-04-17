using System.IO;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class FundsIntentPayloadSerializer
    {
        public static byte[] Serialize(double funds, string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(funds);
                writer.Write(reason ?? string.Empty);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] payload, int numBytes, out double funds, out string reason)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                funds = reader.ReadDouble();
                reason = reader.ReadString();
            }
        }
    }
}
