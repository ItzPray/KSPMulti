using LmpCommon.Enums;
using System;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Maps stock <c>ProtoScenarioModule</c> scenario names to default career/science applicability and capability hooks used during domain registration.
    /// </summary>
    internal sealed class PersistentSyncStockScenarioMetadata
    {
        private static readonly Dictionary<string, PersistentSyncStockScenarioMetadata> Known =
            new Dictionary<string, PersistentSyncStockScenarioMetadata>(StringComparer.Ordinal)
            {
                ["Funding"] = Career(PersistentSyncCapabilityFlags.Funding, PersistentSyncMaterializationSlot.Funding),
                ["Reputation"] = Career(PersistentSyncCapabilityFlags.Reputation, PersistentSyncMaterializationSlot.Reputation),
                ["StrategySystem"] = Career(PersistentSyncCapabilityFlags.StrategySystem, PersistentSyncMaterializationSlot.StrategySystem),
                ["ResearchAndDevelopment"] = CareerScience(PersistentSyncCapabilityFlags.ResearchAndDevelopment, PersistentSyncMaterializationSlot.ResearchAndDevelopment),
                ["ScenarioUpgradeableFacilities"] = Career(PersistentSyncCapabilityFlags.UpgradeableFacilities, PersistentSyncMaterializationSlot.UpgradeableFacilities),
                ["ContractSystem"] = Career(PersistentSyncCapabilityFlags.ContractSystem, PersistentSyncMaterializationSlot.None),
                ["ProgressTracking"] = CareerScience(PersistentSyncCapabilityFlags.ProgressTracking, PersistentSyncMaterializationSlot.None),
                ["LmpGameLaunchId"] = new PersistentSyncStockScenarioMetadata(GameMode.Sandbox, PersistentSyncCapabilityFlags.None, PersistentSyncMaterializationSlot.None)
            };

        private PersistentSyncStockScenarioMetadata(
            GameMode gameModes,
            PersistentSyncCapabilityFlags requiredCapabilities,
            PersistentSyncMaterializationSlot materializationSlot)
        {
            GameModes = gameModes;
            RequiredCapabilities = requiredCapabilities;
            MaterializationSlot = materializationSlot;
        }

        public GameMode GameModes { get; }
        public PersistentSyncCapabilityFlags RequiredCapabilities { get; }
        public PersistentSyncMaterializationSlot MaterializationSlot { get; }

        public static PersistentSyncStockScenarioMetadata Get(string scenarioName)
        {
            if (!Known.TryGetValue(scenarioName, out var metadata))
            {
                throw new InvalidOperationException($"Unknown persistent sync stock scenario '{scenarioName}'. Declare capability/materialization explicitly before registering it.");
            }

            return metadata;
        }

        private static PersistentSyncStockScenarioMetadata Career(
            PersistentSyncCapabilityFlags capability,
            PersistentSyncMaterializationSlot materializationSlot)
        {
            return new PersistentSyncStockScenarioMetadata(GameMode.Career, capability, materializationSlot);
        }

        private static PersistentSyncStockScenarioMetadata CareerScience(
            PersistentSyncCapabilityFlags capability,
            PersistentSyncMaterializationSlot materializationSlot)
        {
            return new PersistentSyncStockScenarioMetadata(GameMode.Career | GameMode.Science, capability, materializationSlot);
        }
    }
}