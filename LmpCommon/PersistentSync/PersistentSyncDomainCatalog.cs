using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Runtime catalog of persistent sync domain definitions (order, applicability, materialization, and stock scenarios).
    /// Definitions are supplied once via <see cref="Configure"/> from <see cref="PersistentSyncDomainRegistrar.BuildDefinitions"/> during server/client startup.
    /// </summary>
    public static class PersistentSyncDomainCatalog
    {
        // One catalog row remains the source of truth for registration, startup applicability, and legacy scenario bypasses.
        // Keep wire ids explicit on each definition; registrar order must not change PersistentSyncDomainId values.
        private static PersistentSyncDomainDefinition[] _definitions = new PersistentSyncDomainDefinition[0];
        private static Dictionary<PersistentSyncDomainId, PersistentSyncDomainDefinition> _byId =
            new Dictionary<PersistentSyncDomainId, PersistentSyncDomainDefinition>();

        public static IReadOnlyList<PersistentSyncDomainDefinition> AllOrdered => _definitions;

        public static void Configure(IEnumerable<PersistentSyncDomainDefinition> definitions)
        {
            var ordered = (definitions ?? Enumerable.Empty<PersistentSyncDomainDefinition>()).ToArray();
            _definitions = ordered;
            _byId = ordered.ToDictionary(d => d.DomainId);
        }

        public static PersistentSyncDomainDefinition Get(PersistentSyncDomainId domainId)
        {
            if (!_byId.TryGetValue(domainId, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(domainId), domainId, "Persistent sync domain is not registered.");
            }

            return definition;
        }

        public static bool TryGet(PersistentSyncDomainId domainId, out PersistentSyncDomainDefinition definition)
        {
            return _byId.TryGetValue(domainId, out definition);
        }

        public static int GetOrder(PersistentSyncDomainId domainId)
        {
            var definition = Get(domainId);
            for (var i = 0; i < _definitions.Length; i++)
            {
                if (_definitions[i].DomainId == definition.DomainId)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        public static bool IsDomainApplicableForInitialSync(
            PersistentSyncDomainId domainId,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            if (!_byId.TryGetValue(domainId, out var definition))
            {
                return false;
            }

            var matchesGameMode = definition.InitialSyncGameModes == GameMode.Sandbox
                ? (serverGameMode & (GameMode.Career | GameMode.Science)) == 0
                : (serverGameMode & definition.InitialSyncGameModes) != 0;

            return matchesGameMode && HasCapabilities(caps, definition.RequiredCapabilities);
        }

        public static bool IsDomainApplicableForShareProducer(
            PersistentSyncDomainId domainId,
            GameMode serverGameMode,
            in PersistentSyncSessionCapabilities caps)
        {
            return IsDomainApplicableForInitialSync(domainId, serverGameMode, in caps)
                   && HasCapabilities(caps, Get(domainId).ProducerRequiredCapabilities);
        }

        public static IEnumerable<PersistentSyncDomainId> GetRequiredDomainsForInitialSync(
            GameMode serverGameMode,
            PersistentSyncSessionCapabilities caps)
        {
            foreach (var definition in _definitions)
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
                _definitions.SelectMany(d => d.ServerScenarioBypasses),
                StringComparer.Ordinal);
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