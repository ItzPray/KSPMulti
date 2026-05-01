using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Merges server-authoritative catalog rows with locally registered <see cref="PersistentSyncDomainDefinition"/> rows.
    /// </summary>
    public static class PersistentSyncCatalogMerger
    {
        /// <summary>
        /// Test/harness fallback that uses local wire assignments unchanged. Runtime joins must use server rows.
        /// </summary>
        public static bool TryMergeLocalOnlyForTests(IReadOnlyList<PersistentSyncDomainDefinition> localDefinitions, out PersistentSyncDomainDefinition[] merged, out string failureReason)
        {
            merged = null;
            failureReason = null;
            if (localDefinitions == null || localDefinitions.Count == 0)
            {
                failureReason = "No local persistent sync domain definitions.";
                return false;
            }

            merged = localDefinitions.OrderBy(d => d.WireId).ToArray();
            return true;
        }

        /// <summary>
        /// Applies server catalog rows onto local definitions (matched by domain name). Server metadata overwrites local applicability fields.
        /// </summary>
        public static bool TryMerge(
            IReadOnlyList<PersistentSyncDomainDefinition> localDefinitions,
            PersistentSyncCatalogRowWire[] serverRows,
            out PersistentSyncDomainDefinition[] merged,
            out string failureReason)
        {
            merged = null;
            failureReason = null;

            if (localDefinitions == null || localDefinitions.Count == 0)
            {
                failureReason = "No local persistent sync domain definitions.";
                return false;
            }

            if (serverRows == null || serverRows.Length == 0)
            {
                failureReason = "Server sent an empty persistent sync catalog.";
                return false;
            }

            if (serverRows.Length != localDefinitions.Count)
            {
                failureReason =
                    $"Persistent sync catalog size mismatch: server advertises {serverRows.Length} domains but this client registered {localDefinitions.Count}. Update client or server build.";
                return false;
            }

            var localByName = localDefinitions.ToDictionary(d => d.Name, StringComparer.Ordinal);
            var sorted = serverRows.OrderBy(r => r.WireId).ToArray();

            for (var i = 0; i < sorted.Length; i++)
            {
                if (sorted[i].WireId != i)
                {
                    failureReason =
                        $"Persistent sync catalog wire ids must be contiguous from 0..N-1 (expected {i}, got {sorted[i].WireId} for domain '{sorted[i].DomainName ?? string.Empty}').";
                    return false;
                }
            }

            var wireIds = new HashSet<ushort>();
            var namesSeen = new HashSet<string>(StringComparer.Ordinal);
            merged = new PersistentSyncDomainDefinition[sorted.Length];

            for (var i = 0; i < sorted.Length; i++)
            {
                var row = sorted[i];
                if (!wireIds.Add(row.WireId))
                {
                    failureReason = $"Duplicate persistent sync wire id {row.WireId}.";
                    return false;
                }

                var name = row.DomainName ?? string.Empty;
                if (!namesSeen.Add(name))
                {
                    failureReason = $"Duplicate persistent sync domain name '{name}'.";
                    return false;
                }

                if (!localByName.TryGetValue(name, out var local))
                {
                    failureReason =
                        $"[KSPMP] Persistent sync catalog incompatible: missing client handler for domain '{name}' (server wire {row.WireId}). Update client or server build.";
                    return false;
                }

                merged[i] = local.WithServerCatalogRow(row);
            }

            if (namesSeen.Count != localByName.Count)
            {
                var missing = localByName.Keys.FirstOrDefault(k => !namesSeen.Contains(k));
                failureReason =
                    $"Persistent sync catalog missing server entry for local domain '{missing}'. Update client or server build.";
                return false;
            }

            return true;
        }
    }
}
