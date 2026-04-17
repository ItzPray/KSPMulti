using System.IO;

namespace LmpCommon.PersistentSync
{
    public sealed class PartPurchaseSnapshotInfo
    {
        public string TechId = string.Empty;
        public string[] PartNames = new string[0];
    }

    public static class PartPurchasesSnapshotPayloadSerializer
    {
        public static byte[] Serialize(PartPurchaseSnapshotInfo[] purchases)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(purchases?.Length ?? 0);
                if (purchases != null)
                {
                    foreach (var purchase in purchases)
                    {
                        writer.Write(purchase?.TechId ?? string.Empty);
                        var parts = purchase?.PartNames ?? new string[0];
                        writer.Write(parts.Length);
                        foreach (var partName in parts)
                        {
                            writer.Write(partName ?? string.Empty);
                        }
                    }
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }

        public static PartPurchaseSnapshotInfo[] Deserialize(byte[] payload)
        {
            using (var memoryStream = new MemoryStream(payload))
            using (var reader = new BinaryReader(memoryStream))
            {
                var count = reader.ReadInt32();
                var purchases = new PartPurchaseSnapshotInfo[count];
                for (var i = 0; i < count; i++)
                {
                    var purchase = new PartPurchaseSnapshotInfo
                    {
                        TechId = reader.ReadString()
                    };

                    var partCount = reader.ReadInt32();
                    purchase.PartNames = new string[partCount];
                    for (var partIndex = 0; partIndex < partCount; partIndex++)
                    {
                        purchase.PartNames[partIndex] = reader.ReadString();
                    }

                    purchases[i] = purchase;
                }

                return purchases;
            }
        }
    }
}
