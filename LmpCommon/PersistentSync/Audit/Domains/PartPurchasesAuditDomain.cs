using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.PartPurchases;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class PartPurchasesAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<PartPurchasesPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<PartPurchasesPayload>(serverBytes, serverNumBytes);
            var hadMismatch = TechnologyAuditDomain.ComparePartPurchaseProjectionRecords(r, local?.Items, server?.Items);

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? "Match: PartPurchasesPayload"
                : "Mismatch: purchased-part row(s) (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
