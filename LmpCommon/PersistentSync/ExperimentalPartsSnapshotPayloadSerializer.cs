using System.IO;

namespace LmpCommon.PersistentSync
{
    public sealed class ExperimentalPartSnapshotInfo
    {
        public string PartName = string.Empty;
        public int Count;
    }

    public static class ExperimentalPartsSnapshotPayloadSerializer
    {
        public static byte[] Serialize(ExperimentalPartSnapshotInfo[] parts)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(parts?.Length ?? 0);
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        writer.Write(part?.PartName ?? string.Empty);
                        writer.Write(part?.Count ?? 0);
                    }
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }

        public static ExperimentalPartSnapshotInfo[] Deserialize(byte[] payload)
        {
            using (var memoryStream = new MemoryStream(payload))
            using (var reader = new BinaryReader(memoryStream))
            {
                var count = reader.ReadInt32();
                var parts = new ExperimentalPartSnapshotInfo[count];
                for (var i = 0; i < count; i++)
                {
                    parts[i] = new ExperimentalPartSnapshotInfo
                    {
                        PartName = reader.ReadString(),
                        Count = reader.ReadInt32()
                    };
                }

                return parts;
            }
        }
    }
}
