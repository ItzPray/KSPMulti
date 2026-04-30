using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    public static class PersistentSyncDomainCatalog
    {
        // One catalog row is the source of truth for registration, startup applicability, and legacy scenario bypasses.
        // Keep wire ids explicit. Reordering these entries must not change PersistentSyncDomainId values.
        private static readonly PersistentSyncDomainDefinition[] Definitions =
        {
            Define(PersistentSyncDomainId.Funds, 0, GameMode.Career, PersistentSyncCapabilityFlags.Funding, PersistentSyncMaterializationSlot.Funding, "Funding"),
            Define(PersistentSyncDomainId.Science, 1, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncMaterializationSlot.ResearchAndDevelopment, "ResearchAndDevelopment"),
            Define(PersistentSyncDomainId.Reputation, 2, GameMode.Career, PersistentSyncCapabilityFlags.Reputation, PersistentSyncMaterializationSlot.Reputation, "Reputation"),
            Define(PersistentSyncDomainId.Strategy, 3, GameMode.Career, PersistentSyncCapabilityFlags.StrategySystem, PersistentSyncMaterializationSlot.StrategySystem, "StrategySystem"),
            Define(PersistentSyncDomainId.Achievements, 4, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ProgressTracking, PersistentSyncMaterializationSlot.None, "ProgressTracking"),
            Define(PersistentSyncDomainId.ScienceSubjects, 5, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncMaterializationSlot.ResearchAndDevelopment, "ResearchAndDevelopment"),
            Define(PersistentSyncDomainId.Technology, 6, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncMaterializationSlot.ResearchAndDevelopment, "ResearchAndDevelopment"),
            Define(PersistentSyncDomainId.ExperimentalParts, 7, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncMaterializationSlot.ResearchAndDevelopment, "ResearchAndDevelopment"),
            Define(PersistentSyncDomainId.PartPurchases, 8, GameMode.Career | GameMode.Science, PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncCapabilityFlags.PartPurchaseMechanism, PersistentSyncMaterializationSlot.ResearchAndDevelopment, "ResearchAndDevelopment"),
            Define(PersistentSyncDomainId.UpgradeableFacilities, 9, GameMode.Career, PersistentSyncCapabilityFlags.UpgradeableFacilities, PersistentSyncMaterializationSlot.UpgradeableFacilities, "ScenarioUpgradeableFacilities"),
            Define(PersistentSyncDomainId.Contracts, 10, GameMode.Career, PersistentSyncCapabilityFlags.ContractSystem, PersistentSyncMaterializationSlot.None, "ContractSystem"),
            Define(PersistentSyncDomainId.GameLaunchId, 11, GameMode.Sandbox, PersistentSyncCapabilityFlags.None, PersistentSyncMaterializationSlot.None, "LmpGameLaunchId")
        };

        private static readonly Dictionary<PersistentSyncDomainId, PersistentSyncDomainDefinition> ById =
            Definitions.ToDictionary(d => d.DomainId);

        public static IReadOnlyList<PersistentSyncDomainDefinition> AllOrdered => Definitions;

        public static PersistentSyncDomainDefinition Get(PersistentSyncDomainId domainId)
        {
            if (!ById.TryGetValue(domainId, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(domainId), domainId, "Persistent sync domain is not in the catalog.");
            }

            return definition;
        }

        public static bool TryGet(PersistentSyncDomainId domainId, out PersistentSyncDomainDefinition definition)
        {
            return ById.TryGetValue(domainId, out definition);
        }

        public static int GetOrder(PersistentSyncDomainId domainId)
        {
            return Get(domainId).Order;
        }

        public static bool IsDomainApplicableForInitialSync(
            PersistentSyncDomainId domainId,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            if (!ById.TryGetValue(domainId, out var definition))
            {
                return false;
            }

            var matchesGameMode = definition.InitialSyncGameModes == GameMode.Sandbox
                ? (serverGameMode & (GameMode.Career | GameMode.Science)) == 0
                : (serverGameMode & definition.InitialSyncGameModes) != 0;

            if (!matchesGameMode)
            {
                return false;
            }

            return HasCapabilities(caps, definition.RequiredCapabilities);
        }

        public static bool IsDomainApplicableForShareProducer(
            PersistentSyncDomainId domainId,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            if (!IsDomainApplicableForInitialSync(domainId, serverGameMode, in caps))
            {
                return false;
            }

            return HasCapabilities(caps, Get(domainId).ProducerRequiredCapabilities);
        }

        public static IEnumerable<PersistentSyncDomainId> GetRequiredDomainsForInitialSync(
            GameMode serverGameMode,
            PersistentSyncSessionCapabilities caps)
        {
            foreach (var definition in Definitions)
            {
                if (IsDomainApplicableForInitialSync(definition.DomainId, serverGameMode, in caps))
                {
                    yield return definition.DomainId;
                }
            }
        }

        public static ISet<string> GetServerScenarioBypasses()
        {
            return new HashSet<string>(
                Definitions.SelectMany(d => d.ServerScenarioBypasses),
                StringComparer.Ordinal);
        }

        private static PersistentSyncDomainDefinition Define(
            PersistentSyncDomainId domainId,
            int order,
            GameMode initialSyncGameModes,
            PersistentSyncCapabilityFlags requiredCapabilities,
            PersistentSyncMaterializationSlot materializationSlot,
            params string[] serverScenarioBypasses)
        {
            return Define(domainId, order, initialSyncGameModes, requiredCapabilities, PersistentSyncCapabilityFlags.None, materializationSlot, serverScenarioBypasses);
        }

        private static PersistentSyncDomainDefinition Define(
            PersistentSyncDomainId domainId,
            int order,
            GameMode initialSyncGameModes,
            PersistentSyncCapabilityFlags requiredCapabilities,
            PersistentSyncCapabilityFlags producerRequiredCapabilities,
            PersistentSyncMaterializationSlot materializationSlot,
            params string[] serverScenarioBypasses)
        {
            return new PersistentSyncDomainDefinition(
                domainId,
                order,
                initialSyncGameModes,
                requiredCapabilities,
                producerRequiredCapabilities,
                materializationSlot,
                serverScenarioBypasses);
        }

        private static bool HasCapabilities(PersistentSyncSessionCapabilities caps, PersistentSyncCapabilityFlags required)
        {
            if (required == PersistentSyncCapabilityFlags.None)
            {
                return true;
            }

            if (required.HasFlag(PersistentSyncCapabilityFlags.Funding) && !caps.HasFundingScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.Reputation) && !caps.HasReputationScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.ResearchAndDevelopment) && !caps.HasResearchAndDevelopmentScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.ProgressTracking) && !caps.HasProgressTrackingScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.ContractSystem) && !caps.HasContractSystemScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.UpgradeableFacilities) && !caps.HasUpgradeableFacilitiesScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.StrategySystem) && !caps.HasStrategySystemScenario) return false;
            if (required.HasFlag(PersistentSyncCapabilityFlags.PartPurchaseMechanism) && !caps.PartPurchaseMechanismEnabled) return false;

            return true;
        }
    }
}
