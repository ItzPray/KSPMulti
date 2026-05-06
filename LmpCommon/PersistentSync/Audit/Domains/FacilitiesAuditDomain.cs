using System;
using System.Collections.Generic;
using System.Linq;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class FacilitiesAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<UpgradeableFacilitiesPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<UpgradeableFacilitiesPayload>(serverBytes, serverNumBytes);
            var lm = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in local?.Items ?? Array.Empty<UpgradeableFacilityLevelPayload>())
            {
                if (x != null && !string.IsNullOrEmpty(x.FacilityId))
                {
                    lm[x.FacilityId] = x.Level;
                }
            }

            var sm = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in server?.Items ?? Array.Empty<UpgradeableFacilityLevelPayload>())
            {
                if (x != null && !string.IsNullOrEmpty(x.FacilityId))
                {
                    sm[x.FacilityId] = x.Level;
                }
            }

            var hadMismatch = false;
            foreach (var id in lm.Keys.Union(sm.Keys))
            {
                lm.TryGetValue(id, out var lv);
                sm.TryGetValue(id, out var sv);
                if (!lm.ContainsKey(id))
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnClient,
                        id,
                        "<missing>",
                        sv.ToString());
                    continue;
                }

                if (!sm.ContainsKey(id))
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnServer,
                        id,
                        lv.ToString(),
                        "<missing>");
                    continue;
                }

                if (lv != sv)
                {
                    hadMismatch = true;
                    if (r.Details.Count < 24)
                    {
                        r.Details.Add($"facility={id} local={lv} server={sv}");
                    }

                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.ValueMismatch,
                        id,
                        lv.ToString(),
                        sv.ToString());
                }
            }

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? $"Match: facility rows local={lm.Count} server={sm.Count}"
                : "Mismatch: facility level(s) (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
