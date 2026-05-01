using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.PartPurchases
{
    public sealed class PartPurchasesPayload
    {
        public PartPurchaseSnapshotInfo[] Items = Array.Empty<PartPurchaseSnapshotInfo>();
    }

    public sealed class PartPurchasesPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<PartPurchasesPayload>(
                reader => new PartPurchasesPayload
                {
                    Items = (PartPurchaseSnapshotInfo[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(PartPurchaseSnapshotInfo[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(PartPurchaseSnapshotInfo[]),
                    p?.Items ?? Array.Empty<PartPurchaseSnapshotInfo>()));
        }
    }
}
