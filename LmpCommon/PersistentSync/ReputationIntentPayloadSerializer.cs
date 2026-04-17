using System.IO;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class ReputationIntentPayloadSerializer
    {
        public static byte[] Serialize(float reputation, string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(reputation);
                writer.Write(reason ?? string.Empty);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] payload, int numBytes, out float reputation, out string reason)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                reputation = reader.ReadSingle();
                reason = reader.ReadString();
            }
        }
    }
}
