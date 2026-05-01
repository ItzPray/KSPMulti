using LmpCommon.Enums;
using System;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync
{
    /// <summary>Bitmask of runtime prerequisites a persistent sync domain needs on the client before it can participate.</summary>
    [Flags]
    public enum PersistentSyncCapabilityFlags
    {
        None = 0,
        Funding = 1 << 0,
        Reputation = 1 << 1,
        ResearchAndDevelopment = 1 << 2,
        ProgressTracking = 1 << 3,
        ContractSystem = 1 << 4,
        UpgradeableFacilities = 1 << 5,
        StrategySystem = 1 << 6,
        PartPurchaseMechanism = 1 << 7
    }

    /// <summary>
    /// Immutable snapshot of one persistent sync domain: legacy wire identity, applicability flags, materialization slot, owning scenario node, and optional ordering/projections metadata.
    /// </summary>
    public sealed class PersistentSyncDomainDefinition
    {
        public PersistentSyncDomainDefinition(
            PersistentSyncDomainKey key,
            GameMode initialSyncGameModes,
            PersistentSyncCapabilityFlags requiredCapabilities,
            PersistentSyncCapabilityFlags producerRequiredCapabilities,
            PersistentSyncMaterializationSlot materializationSlot,
            Type domainType,
            IEnumerable<PersistentSyncDomainKey> afterDomains,
            IEnumerable<string> serverScenarioBypasses,
            ushort wireId = 0,
            string scenarioName = null,
            string scalarFieldName = null)
        {
            Key = key;
            InitialSyncGameModes = initialSyncGameModes;
            RequiredCapabilities = requiredCapabilities;
            ProducerRequiredCapabilities = producerRequiredCapabilities;
            MaterializationSlot = materializationSlot;
            DomainType = domainType;
            AfterDomains = new List<PersistentSyncDomainKey>(afterDomains ?? new PersistentSyncDomainKey[0]).ToArray();
            ServerScenarioBypasses = new List<string>(serverScenarioBypasses ?? new string[0]).ToArray();
            WireId = wireId;
            ScenarioName = scenarioName;
            ScalarFieldName = scalarFieldName;
        }

        public PersistentSyncDomainKey Key { get; }
        public string Name => Key.Name;
        public ushort WireId { get; }
        public PersistentSyncDomainId DomainId => Key.LegacyId;
        public GameMode InitialSyncGameModes { get; }
        public PersistentSyncCapabilityFlags RequiredCapabilities { get; }
        public PersistentSyncCapabilityFlags ProducerRequiredCapabilities { get; }
        public PersistentSyncMaterializationSlot MaterializationSlot { get; }
        public Type DomainType { get; }
        public PersistentSyncDomainKey[] AfterDomains { get; }
        public string[] ServerScenarioBypasses { get; }
        public string ScenarioName { get; }
        public string ScalarFieldName { get; }
    }
}
