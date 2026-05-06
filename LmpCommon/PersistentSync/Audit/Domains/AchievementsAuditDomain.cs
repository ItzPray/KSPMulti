using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Audit;
using LmpCommon.PersistentSync.Payloads.Achievements;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class AchievementsAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<AchievementsPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<AchievementsPayload>(serverBytes, serverNumBytes);
            var localMap = ToAchievementMap(local);
            var serverMap = ToAchievementMap(server);
            if (localMap.Count == 0 && serverMap.Count == 0)
            {
                r.PrimaryKind = PersistentSyncAuditDifferenceKind.Ok;
                r.Summary = "Both payloads empty";
                PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
                return r;
            }

            var hadMismatch = false;
            foreach (var id in localMap.Keys.Union(serverMap.Keys).OrderBy(x => x))
            {
                localMap.TryGetValue(id, out var lc);
                serverMap.TryGetValue(id, out var sc);
                if (lc == null && sc != null)
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnClient,
                        id,
                        "<missing>",
                        $"len={sc.Length}");
                    if (r.Details.Count < 24)
                    {
                        r.Details.Add($"id={id} missing on client");
                    }

                    continue;
                }

                if (lc != null && sc == null)
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.MissingOnServer,
                        id,
                        $"len={lc.Length}",
                        "<missing>");
                    if (r.Details.Count < 24)
                    {
                        r.Details.Add($"id={id} missing on server");
                    }

                    continue;
                }

                var ls = lc != null ? Encoding.UTF8.GetString(lc, 0, lc.Length) : "<missing>";
                var ss = sc != null ? Encoding.UTF8.GetString(sc, 0, sc.Length) : "<missing>";
                if (!string.Equals(ls, ss, StringComparison.Ordinal))
                {
                    if (AchievementCfgSemanticDiff.IsSemanticallyEquivalentForAudit(lc, sc))
                    {
                        continue;
                    }

                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.ValueMismatch,
                        id,
                        $"len={lc?.Length ?? 0}",
                        $"len={sc?.Length ?? 0}");
                    if (r.Details.Count < 24)
                    {
                        r.Details.Add($"id={id} mismatch localLen={lc?.Length ?? 0} serverLen={sc?.Length ?? 0}");
                    }

                    AchievementCfgSemanticDiff.AppendDiagnosticsForByteMismatch(r, id, lc, sc);
                }
            }

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? $"Match: achievement rows local={localMap.Count} server={serverMap.Count}"
                : $"Mismatch: achievement row differences (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }

        private static Dictionary<string, byte[]> ToAchievementMap(AchievementsPayload payload)
        {
            var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in payload?.Items ?? Array.Empty<AchievementSnapshotInfo>())
            {
                if (item == null || item.Data == null || item.Data.Length == 0)
                {
                    continue;
                }

                var key = AchievementCfgSemanticDiff.ResolveAchievementRowKey(item);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                map[key] = item.Data;
            }

            return map;
        }
    }
}
