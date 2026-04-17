using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class UpgradeableFacilitiesSnapshotPayloadSerializer
    {
        public static byte[] Serialize(IReadOnlyDictionary<string, int> facilityLevels)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var ordered = (facilityLevels ?? new Dictionary<string, int>()).OrderBy(kvp => kvp.Key).ToArray();
                writer.Write(ordered.Length);
                foreach (var facility in ordered)
                {
                    writer.Write(facility.Key ?? string.Empty);
                    writer.Write(facility.Value);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static Dictionary<string, int> Deserialize(byte[] payload, int numBytes)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var count = reader.ReadInt32();
                var facilityLevels = new Dictionary<string, int>(count);
                for (var i = 0; i < count; i++)
                {
                    facilityLevels[reader.ReadString()] = reader.ReadInt32();
                }

                return facilityLevels;
            }
        }
    }
}
