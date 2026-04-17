using System.IO;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class ScienceIntentPayloadSerializer
    {
        public static byte[] Serialize(float science, string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(science);
                writer.Write(reason ?? string.Empty);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] payload, int numBytes, out float science, out string reason)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                science = reader.ReadSingle();
                reason = reader.ReadString();
            }
        }
    }
}
