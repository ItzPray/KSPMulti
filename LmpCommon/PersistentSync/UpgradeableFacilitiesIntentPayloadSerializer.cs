using System.IO;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class UpgradeableFacilitiesIntentPayloadSerializer
    {
        public static byte[] Serialize(string facilityId, int level)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(facilityId ?? string.Empty);
                writer.Write(level);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] payload, int numBytes, out string facilityId, out int level)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                facilityId = reader.ReadString();
                level = reader.ReadInt32();
            }
        }
    }
}
