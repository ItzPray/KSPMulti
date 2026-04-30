using LmpCommon.Enums;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Central rules for which persistent-sync domains belong to the current session (initial sync / convergence),
    /// separate from runtime apply readiness handled by client domain appliers (<see cref="PersistentSyncApplyOutcome"/>).
    /// </summary>
    public static class PersistentSyncDomainApplicability
    {
        public static bool IsDomainApplicableForInitialSync(
            PersistentSyncDomainId domain,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            return PersistentSyncDomainCatalog.IsDomainApplicableForInitialSync(domain, serverGameMode, in caps);
        }

        /// <summary>
        /// Share-progress producers that mirror persistent-sync domains should use this gate so local intent
        /// generation matches startup applicability, with extra producer-only constraints (e.g. difficulty).
        /// </summary>
        public static bool IsDomainApplicableForShareProducer(
            PersistentSyncDomainId domain,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            return PersistentSyncDomainCatalog.IsDomainApplicableForShareProducer(domain, serverGameMode, in caps);
        }

        public static IEnumerable<PersistentSyncDomainId> GetRequiredDomainsForInitialSync(
            GameMode serverGameMode,
            PersistentSyncSessionCapabilities caps)
        {
            return PersistentSyncDomainCatalog.GetRequiredDomainsForInitialSync(serverGameMode, caps);
        }
    }
}
