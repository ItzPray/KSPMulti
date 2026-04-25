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
        /// <summary>
        /// Ordered list of every known domain for iteration / tests.
        /// </summary>
        public static readonly PersistentSyncDomainId[] AllDomainsOrdered =
        {
            PersistentSyncDomainId.Funds,
            PersistentSyncDomainId.Science,
            PersistentSyncDomainId.Reputation,
            PersistentSyncDomainId.Strategy,
            PersistentSyncDomainId.Achievements,
            PersistentSyncDomainId.ScienceSubjects,
            PersistentSyncDomainId.Technology,
            PersistentSyncDomainId.ExperimentalParts,
            PersistentSyncDomainId.PartPurchases,
            PersistentSyncDomainId.UpgradeableFacilities,
            PersistentSyncDomainId.Contracts,
            PersistentSyncDomainId.GameLaunchId
        };

        public static bool IsDomainApplicableForInitialSync(
            PersistentSyncDomainId domain,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            if (domain == PersistentSyncDomainId.GameLaunchId)
            {
                // Sandbox sessions have no stock progression scenarios; they still need the KSPMP launch-id high-water mark.
                return (serverGameMode & (GameMode.Career | GameMode.Science)) == 0;
            }

            if ((serverGameMode & (GameMode.Career | GameMode.Science)) == 0)
            {
                return false;
            }

            var careerLike = (serverGameMode & GameMode.Career) != 0;
            var scienceOrCareerRnD = (serverGameMode & (GameMode.Career | GameMode.Science)) != 0;

            switch (domain)
            {
                case PersistentSyncDomainId.Funds:
                    return careerLike && caps.HasFundingScenario;
                case PersistentSyncDomainId.Reputation:
                    return careerLike && caps.HasReputationScenario;
                case PersistentSyncDomainId.Strategy:
                    return careerLike && caps.HasStrategySystemScenario;
                case PersistentSyncDomainId.UpgradeableFacilities:
                    return careerLike && caps.HasUpgradeableFacilitiesScenario;
                case PersistentSyncDomainId.Contracts:
                    return careerLike && caps.HasContractSystemScenario;
                case PersistentSyncDomainId.Science:
                    return scienceOrCareerRnD && caps.HasResearchAndDevelopmentScenario;
                case PersistentSyncDomainId.Achievements:
                    return scienceOrCareerRnD && caps.HasProgressTrackingScenario;
                case PersistentSyncDomainId.ScienceSubjects:
                case PersistentSyncDomainId.Technology:
                case PersistentSyncDomainId.ExperimentalParts:
                case PersistentSyncDomainId.PartPurchases:
                    return scienceOrCareerRnD && caps.HasResearchAndDevelopmentScenario;
                default:
                    return false;
            }
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
            if (!IsDomainApplicableForInitialSync(domain, serverGameMode, in caps))
            {
                return false;
            }

            if (domain == PersistentSyncDomainId.PartPurchases && !caps.PartPurchaseMechanismEnabled)
            {
                return false;
            }

            return true;
        }

        public static IEnumerable<PersistentSyncDomainId> GetRequiredDomainsForInitialSync(
            GameMode serverGameMode,
            PersistentSyncSessionCapabilities caps)
        {
            foreach (var domain in AllDomainsOrdered)
            {
                if (IsDomainApplicableForInitialSync(domain, serverGameMode, in caps))
                {
                    yield return domain;
                }
            }
        }
    }
}
