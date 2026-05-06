using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.Technology;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class TechnologyAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<TechnologyPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<TechnologyPayload>(serverBytes, serverNumBytes);

            var techHad = CompareTechnologyTechnologies(r, local?.Technologies, server?.Technologies);
            var partHad = ComparePartPurchaseProjectionRecords(r, local?.PartPurchases, server?.PartPurchases);
            var hadMismatch = techHad || partHad;

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? "Match: TechnologyPayload (technologies + embedded part purchases projection)"
                : "Mismatch: TechnologyPayload (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }

        private static bool CompareTechnologyTechnologies(
            PersistentSyncAuditComparisonResult r,
            TechnologySnapshotInfo[] local,
            TechnologySnapshotInfo[] server)
        {
            var lm = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var t in local ?? Array.Empty<TechnologySnapshotInfo>())
            {
                if (t != null && !string.IsNullOrEmpty(t.TechId))
                {
                    lm[t.TechId] = t.Data ?? Array.Empty<byte>();
                }
            }

            var sm = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var t in server ?? Array.Empty<TechnologySnapshotInfo>())
            {
                if (t != null && !string.IsNullOrEmpty(t.TechId))
                {
                    sm[t.TechId] = t.Data ?? Array.Empty<byte>();
                }
            }

            var had = false;
            foreach (var id in lm.Keys.Union(sm.Keys))
            {
                lm.TryGetValue(id, out var lb);
                sm.TryGetValue(id, out var sb);
                if (lb == null && sb != null)
                {
                    had = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnClient,
                        $"tech:{id}",
                        "<missing>",
                        $"bytes={sb.Length}");
                    continue;
                }

                if (lb != null && sb == null)
                {
                    had = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnServer,
                        $"tech:{id}",
                        $"bytes={lb.Length}",
                        "<missing>");
                    continue;
                }

                var ls = lb != null ? Encoding.UTF8.GetString(lb, 0, lb.Length) : "";
                var ss = sb != null ? Encoding.UTF8.GetString(sb, 0, sb.Length) : "";
                if (!string.Equals(ls, ss, StringComparison.Ordinal))
                {
                    had = true;
                    if (r.Records.Count < 48)
                    {
                        PersistentSyncAuditSeverityMapping.AddRecord(
                            r,
                            PersistentSyncAuditDifferenceKind.ValueMismatch,
                            $"tech:{id}",
                            $"len={lb?.Length ?? 0}",
                            $"len={sb?.Length ?? 0}");
                    }
                }
            }

            return had;
        }

        /// <summary>Shared with <see cref="PartPurchasesAuditDomain"/>.</summary>
        internal static bool ComparePartPurchaseProjectionRecords(
            PersistentSyncAuditComparisonResult r,
            PartPurchaseSnapshotInfo[] local,
            PartPurchaseSnapshotInfo[] server)
        {
            var lm = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in local ?? Array.Empty<PartPurchaseSnapshotInfo>())
            {
                if (p == null || string.IsNullOrEmpty(p.TechId))
                {
                    continue;
                }

                var names = (p.PartNames ?? Array.Empty<string>()).OrderBy(n => n).ToArray();
                lm[p.TechId] = string.Join(",", names);
            }

            var sm = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in server ?? Array.Empty<PartPurchaseSnapshotInfo>())
            {
                if (p == null || string.IsNullOrEmpty(p.TechId))
                {
                    continue;
                }

                var names = (p.PartNames ?? Array.Empty<string>()).OrderBy(n => n).ToArray();
                sm[p.TechId] = string.Join(",", names);
            }

            var had = false;
            foreach (var id in lm.Keys.Union(sm.Keys))
            {
                lm.TryGetValue(id, out var ls);
                sm.TryGetValue(id, out var ss);
                if (!string.Equals(ls, ss, StringComparison.Ordinal))
                {
                    had = true;
                    if (r.Records.Count < 48)
                    {
                        PersistentSyncAuditSeverityMapping.AddRecord(
                            r,
                            PersistentSyncAuditDifferenceKind.ValueMismatch,
                            $"partPurch:{id}",
                            ls ?? "<missing>",
                            ss ?? "<missing>");
                    }
                }
            }

            return had;
        }
    }
}
