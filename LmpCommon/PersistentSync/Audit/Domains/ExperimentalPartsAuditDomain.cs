using System;
using System.Collections.Generic;
using System.Linq;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class ExperimentalPartsAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<ExperimentalPartsPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<ExperimentalPartsPayload>(serverBytes, serverNumBytes);
            var lm = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in local?.Items ?? Array.Empty<ExperimentalPartSnapshotInfo>())
            {
                if (x != null && !string.IsNullOrEmpty(x.PartName))
                {
                    lm[x.PartName] = x.Count;
                }
            }

            var sm = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in server?.Items ?? Array.Empty<ExperimentalPartSnapshotInfo>())
            {
                if (x != null && !string.IsNullOrEmpty(x.PartName))
                {
                    sm[x.PartName] = x.Count;
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
                ? $"Match: experimental part rows local={lm.Count} server={sm.Count}"
                : "Mismatch: part stock row(s) (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
