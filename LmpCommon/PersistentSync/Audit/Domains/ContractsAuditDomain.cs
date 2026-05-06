using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.Contracts;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class ContractsAuditDomain
    {
        public static PersistentSyncAuditComparisonResult Compare(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<ContractsPayload>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<ContractsPayload>(serverBytes, serverNumBytes);
            var localRows = local?.Snapshot?.Contracts ?? new List<ContractSnapshotInfo>();
            var serverRows = server?.Snapshot?.Contracts ?? new List<ContractSnapshotInfo>();
            var localByGuid = localRows.Where(c => c != null).ToDictionary(c => c.ContractGuid, c => c);
            var serverByGuid = serverRows.Where(c => c != null).ToDictionary(c => c.ContractGuid, c => c);

            var hadMismatch = false;
            foreach (var g in localByGuid.Keys.Union(serverByGuid.Keys))
            {
                localByGuid.TryGetValue(g, out var lc);
                serverByGuid.TryGetValue(g, out var sc);
                if (lc == null || sc == null)
                {
                    hadMismatch = true;
                    var kind = lc == null
                        ? PersistentSyncAuditDifferenceKind.MissingOnClient
                        : PersistentSyncAuditDifferenceKind.MissingOnServer;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        kind,
                        g.ToString(),
                        lc != null ? "present" : "<missing>",
                        sc != null ? "present" : "<missing>");
                    r.Details.Add($"guid={g} present local={lc != null} server={sc != null}");
                    continue;
                }

                var lt = lc.Data != null ? Encoding.UTF8.GetString(lc.Data, 0, lc.Data.Length) : "";
                var st = sc.Data != null ? Encoding.UTF8.GetString(sc.Data, 0, sc.Data.Length) : "";
                if (!string.Equals(lt, st, StringComparison.Ordinal) ||
                    !string.Equals(lc.ContractState, sc.ContractState, StringComparison.Ordinal))
                {
                    hadMismatch = true;
                    PersistentSyncAuditSeverityMapping.AddRecord(
                        r,
                        PersistentSyncAuditDifferenceKind.ValueMismatch,
                        g.ToString(),
                        lc.ContractState,
                        sc.ContractState);
                    if (r.Details.Count < 16)
                    {
                        r.Details.Add($"guid={g} state local={lc.ContractState} server={sc.ContractState}");
                    }
                }
            }

            r.PrimaryKind = hadMismatch ? PersistentSyncAuditDifferenceKind.ValueMismatch : PersistentSyncAuditDifferenceKind.Ok;
            r.Summary = !hadMismatch
                ? $"Match: contract rows local={localByGuid.Count} server={serverByGuid.Count}"
                : $"Mismatch: contract differences (see Records)";
            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
