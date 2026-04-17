using System;
using System.IO;

namespace LmpCommon.PersistentSync
{
    public sealed class StrategySnapshotInfo
    {
        public string Name = string.Empty;
        public int NumBytes;
        public byte[] Data = new byte[0];
    }

    public static class StrategySnapshotPayloadSerializer
    {
        public static byte[] Serialize(StrategySnapshotInfo[] strategies)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(strategies?.Length ?? 0);
                if (strategies != null)
                {
                    foreach (var strategy in strategies)
                    {
                        writer.Write(strategy?.Name ?? string.Empty);
                        writer.Write(strategy?.NumBytes ?? 0);
                        if (strategy != null && strategy.NumBytes > 0)
                        {
                            writer.Write(strategy.Data, 0, strategy.NumBytes);
                        }
                    }
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }

        public static StrategySnapshotInfo[] Deserialize(byte[] payload)
        {
            using (var memoryStream = new MemoryStream(payload))
            using (var reader = new BinaryReader(memoryStream))
            {
                var count = reader.ReadInt32();
                var strategies = new StrategySnapshotInfo[count];
                for (var i = 0; i < count; i++)
                {
                    var strategy = new StrategySnapshotInfo
                    {
                        Name = reader.ReadString(),
                        NumBytes = reader.ReadInt32()
                    };

                    if (strategy.NumBytes > 0)
                    {
                        strategy.Data = reader.ReadBytes(strategy.NumBytes);
                    }

                    strategies[i] = strategy;
                }

                return strategies;
            }
        }
    }
}
