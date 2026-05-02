using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Maps wire domain ids to <see cref="IPersistentSyncEventSuppressor"/> (normally <see cref="SyncClientDomain{TPayload}"/>
    /// instances). Populated when client domains are constructed; optional legacy adapters only during staged migration.
    /// </summary>
    public static class PersistentSyncEventSuppressorRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, IPersistentSyncEventSuppressor> ByDomainId =
            new Dictionary<string, IPersistentSyncEventSuppressor>(StringComparer.Ordinal);

        private static readonly Dictionary<string, IPersistentSyncEventSuppressor> LegacyAdapters =
            new Dictionary<string, IPersistentSyncEventSuppressor>(StringComparer.Ordinal);

        /// <summary>
        /// Rebuild registry from live client domains (each <see cref="SyncClientDomain{TPayload}"/> implements
        /// <see cref="IPersistentSyncEventSuppressor"/>). Called when domain dictionary is created.
        /// </summary>
        public static void ReplaceAllFromClientDomains(IReadOnlyDictionary<string, IPersistentSyncClientDomain> domains)
        {
            lock (Gate)
            {
                ByDomainId.Clear();
                if (domains == null)
                {
                    return;
                }

                foreach (var kvp in domains)
                {
                    if (kvp.Value is IPersistentSyncEventSuppressor suppressor)
                    {
                        ByDomainId[kvp.Key] = suppressor;
                    }
                }
            }
        }

        /// <summary>
        /// Temporary bridge for unmigrated Share stacks (remove when domain owns suppression). Prefer domain instances.
        /// </summary>
        public static void RegisterLegacyAdapter(string domainId, IPersistentSyncEventSuppressor adapter)
        {
            if (string.IsNullOrEmpty(domainId) || adapter == null)
            {
                return;
            }

            lock (Gate)
            {
                LegacyAdapters[domainId] = adapter;
            }
        }

        public static void ClearLegacyAdapters()
        {
            lock (Gate)
            {
                LegacyAdapters.Clear();
            }
        }

        public static bool TryGetSuppressor(string domainId, out IPersistentSyncEventSuppressor suppressor)
        {
            suppressor = null;
            if (string.IsNullOrEmpty(domainId))
            {
                return false;
            }

            lock (Gate)
            {
                if (LegacyAdapters.TryGetValue(domainId, out suppressor))
                {
                    return true;
                }

                return ByDomainId.TryGetValue(domainId, out suppressor);
            }
        }

        /// <summary>
        /// Resolves domain ids to suppressors in order; logs a warning for missing ids (release) so misconfiguration is visible.
        /// </summary>
        public static IReadOnlyList<IPersistentSyncEventSuppressor> Resolve(IReadOnlyList<string> domainIds)
        {
            if (domainIds == null || domainIds.Count == 0)
            {
                return Array.Empty<IPersistentSyncEventSuppressor>();
            }

            var list = new List<IPersistentSyncEventSuppressor>(domainIds.Count);
            foreach (var id in domainIds)
            {
                if (TryGetSuppressor(id, out var suppressor) && suppressor != null)
                {
                    list.Add(suppressor);
                }
                else
                {
                    LunaLog.LogWarning($"[KSPMP] PersistentSyncEventSuppressorRegistry: no suppressor registered for domain '{id}'.");
                }
            }

            return list;
        }

        public static IReadOnlyList<IPersistentSyncEventSuppressor> Resolve(params string[] domainIds)
        {
            return Resolve((IReadOnlyList<string>)domainIds ?? Array.Empty<string>());
        }
    }
}
