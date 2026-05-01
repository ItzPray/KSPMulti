using LmpCommon.PersistentSync.Payloads.PartPurchases;

namespace LmpCommon.PersistentSync.Payloads.Technology
{
    public sealed class TechnologyPayload
    {
        public TechnologySnapshotInfo[] Technologies = new TechnologySnapshotInfo[0];
        public PartPurchaseSnapshotInfo[] PartPurchases = new PartPurchaseSnapshotInfo[0];
    }
}
