using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public sealed class TechnologySnapshotInfo
    {
        public string TechId = string.Empty;
        public int NumBytes;
        public byte[] Data = new byte[0];
    }

    public static class TechnologySnapshotPayloadSerializer
    {
        public static byte[] Serialize(IEnumerable<TechnologySnapshotInfo> technologies)
        {
            var records = (technologies ?? Enumerable.Empty<TechnologySnapshotInfo>()).ToArray();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(records.Length);
                foreach (var technology in records)
                {
                    writer.Write(technology?.TechId ?? string.Empty);
                    writer.Write(technology?.NumBytes ?? 0);
                    if (technology != null && technology.NumBytes > 0 && technology.Data != null)
                    {
                        writer.Write(technology.Data, 0, technology.NumBytes);
                    }
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static List<TechnologySnapshotInfo> Deserialize(byte[] payload, int numBytes)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                var count = reader.ReadInt32();
                var technologies = new List<TechnologySnapshotInfo>(count);
                for (var i = 0; i < count; i++)
                {
                    var techId = reader.ReadString();
                    var length = reader.ReadInt32();
                    var data = reader.ReadBytes(length);
                    technologies.Add(new TechnologySnapshotInfo
                    {
                        TechId = techId,
                        NumBytes = length,
                        Data = data
                    });
                }

                return technologies;
            }
        }
    }
}
