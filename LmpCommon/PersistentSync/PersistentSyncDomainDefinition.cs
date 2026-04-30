using LmpCommon.Enums;
using System;

namespace LmpCommon.PersistentSync
{
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

    public sealed class PersistentSyncDomainDefinition
    {
        public PersistentSyncDomainDefinition(
            PersistentSyncDomainId domainId,
            int order,
            GameMode initialSyncGameModes,
            PersistentSyncCapabilityFlags requiredCapabilities,
            PersistentSyncCapabilityFlags producerRequiredCapabilities,
            PersistentSyncMaterializationSlot materializationSlot,
            params string[] serverScenarioBypasses)
        {
            DomainId = domainId;
            Order = order;
            InitialSyncGameModes = initialSyncGameModes;
            RequiredCapabilities = requiredCapabilities;
            ProducerRequiredCapabilities = producerRequiredCapabilities;
            MaterializationSlot = materializationSlot;
            ServerScenarioBypasses = serverScenarioBypasses ?? new string[0];
        }

        public PersistentSyncDomainId DomainId { get; }
        public int Order { get; }
        public GameMode InitialSyncGameModes { get; }
        public PersistentSyncCapabilityFlags RequiredCapabilities { get; }
        public PersistentSyncCapabilityFlags ProducerRequiredCapabilities { get; }
        public PersistentSyncMaterializationSlot MaterializationSlot { get; }
        public string[] ServerScenarioBypasses { get; }
    }
}
