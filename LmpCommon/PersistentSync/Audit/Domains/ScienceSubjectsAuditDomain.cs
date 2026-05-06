using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class ScienceSubjectsAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<ScienceSubjectsPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<ScienceSubjectsPayload>(serverBytes, serverNumBytes);
            var lm = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var x in local?.Items ?? Array.Empty<ScienceSubjectSnapshotInfo>())
            {
                if (x != null && !string.IsNullOrEmpty(x.Id))
                {
                    lm[x.Id] = x.Data ?? Array.Empty<byte>();
                }
            }

            var sm = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var x in server?.Items ?? Array.Empty<ScienceSubjectSnapshotInfo>())
            {
                if (x != null && !string.IsNullOrEmpty(x.Id))
                {
                    sm[x.Id] = x.Data ?? Array.Empty<byte>();
                }
            }

            var hadMismatch = false;
            foreach (var id in lm.Keys.Union(sm.Keys))
            {
                lm.TryGetValue(id, out var lb);
                sm.TryGetValue(id, out var sb);
                if (lb == null && sb != null)
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnClient,
                        id,
                        "<missing>",
                        $"len={sb.Length}");
                    continue;
                }

                if (lb != null && sb == null)
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnServer,
                        id,
                        $"len={lb.Length}",
                        "<missing>");
                    continue;
                }

                var ls = lb != null ? Encoding.UTF8.GetString(lb, 0, lb.Length) : "";
                var ss = sb != null ? Encoding.UTF8.GetString(sb, 0, sb.Length) : "";
                if (!string.Equals(ls, ss, StringComparison.Ordinal))
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.ValueMismatch,
                        id,
                        $"len={lb?.Length ?? 0}",
                        $"len={sb?.Length ?? 0}");
                }
            }

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? $"Match: science subject rows local={lm.Count} server={sm.Count}"
                : "Mismatch: subject row(s) (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
