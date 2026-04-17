using System;
using System.IO;

namespace LmpCommon.PersistentSync
{
    public sealed class ScienceSubjectSnapshotInfo
    {
        public string Id = string.Empty;
        public int NumBytes;
        public byte[] Data = new byte[0];
    }

    public static class ScienceSubjectSnapshotPayloadSerializer
    {
        public static byte[] Serialize(ScienceSubjectSnapshotInfo[] subjects)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(subjects?.Length ?? 0);
                if (subjects != null)
                {
                    foreach (var subject in subjects)
                    {
                        writer.Write(subject?.Id ?? string.Empty);
                        writer.Write(subject?.NumBytes ?? 0);
                        if (subject != null && subject.NumBytes > 0)
                        {
                            writer.Write(subject.Data, 0, subject.NumBytes);
                        }
                    }
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }

        public static ScienceSubjectSnapshotInfo[] Deserialize(byte[] payload)
        {
            using (var memoryStream = new MemoryStream(payload))
            using (var reader = new BinaryReader(memoryStream))
            {
                var count = reader.ReadInt32();
                var subjects = new ScienceSubjectSnapshotInfo[count];
                for (var i = 0; i < count; i++)
                {
                    var subject = new ScienceSubjectSnapshotInfo
                    {
                        Id = reader.ReadString(),
                        NumBytes = reader.ReadInt32()
                    };

                    if (subject.NumBytes > 0)
                    {
                        subject.Data = reader.ReadBytes(subject.NumBytes);
                    }

                    subjects[i] = subject;
                }

                return subjects;
            }
        }
    }
}
