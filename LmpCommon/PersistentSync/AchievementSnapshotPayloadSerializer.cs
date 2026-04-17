using System;
using System.IO;

namespace LmpCommon.PersistentSync
{
    public sealed class AchievementSnapshotInfo
    {
        public string Id = string.Empty;
        public int NumBytes;
        public byte[] Data = new byte[0];
    }

    public static class AchievementSnapshotPayloadSerializer
    {
        public static byte[] Serialize(AchievementSnapshotInfo[] achievements)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(achievements?.Length ?? 0);
                if (achievements != null)
                {
                    foreach (var achievement in achievements)
                    {
                        writer.Write(achievement?.Id ?? string.Empty);
                        writer.Write(achievement?.NumBytes ?? 0);
                        if (achievement != null && achievement.NumBytes > 0)
                        {
                            writer.Write(achievement.Data, 0, achievement.NumBytes);
                        }
                    }
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }

        public static AchievementSnapshotInfo[] Deserialize(byte[] payload)
        {
            using (var memoryStream = new MemoryStream(payload))
            using (var reader = new BinaryReader(memoryStream))
            {
                var count = reader.ReadInt32();
                var achievements = new AchievementSnapshotInfo[count];
                for (var i = 0; i < count; i++)
                {
                    var achievement = new AchievementSnapshotInfo
                    {
                        Id = reader.ReadString(),
                        NumBytes = reader.ReadInt32()
                    };

                    if (achievement.NumBytes > 0)
                    {
                        achievement.Data = reader.ReadBytes(achievement.NumBytes);
                    }

                    achievements[i] = achievement;
                }

                return achievements;
            }
        }
    }
}
