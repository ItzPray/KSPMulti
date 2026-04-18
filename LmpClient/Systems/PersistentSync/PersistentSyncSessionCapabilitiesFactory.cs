using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Builds <see cref="PersistentSyncSessionCapabilities"/> from the live KSP save when available,
    /// otherwise falls back to <see cref="PersistentSyncSessionCapabilities.OptimisticForServerGameMode"/>.
    /// </summary>
    public static class PersistentSyncSessionCapabilitiesFactory
    {
        public static PersistentSyncSessionCapabilities CreateForCurrentSession()
        {
            var serverMode = SettingsSystem.ServerSettings.GameMode;
            var optimistic = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(serverMode);
            var game = HighLogic.CurrentGame;
            if (game?.scenarios == null)
            {
                return optimistic;
            }

            bool HasScenario(string moduleName) =>
                game.scenarios.Any(s => s != null && s.moduleName == moduleName);

            var bypassPurchase = game.Parameters?.Difficulty?.BypassEntryPurchaseAfterResearch == true;

            return new PersistentSyncSessionCapabilities
            {
                HasFundingScenario = HasScenario("Funding"),
                HasReputationScenario = HasScenario("Reputation"),
                HasResearchAndDevelopmentScenario = HasScenario("ResearchAndDevelopment"),
                HasProgressTrackingScenario = HasScenario("ProgressTracking"),
                HasContractSystemScenario = HasScenario("ContractSystem"),
                HasUpgradeableFacilitiesScenario = HasScenario("ScenarioUpgradeableFacilities"),
                HasStrategySystemScenario = HasScenario("StrategySystem"),
                PartPurchaseMechanismEnabled = !bypassPurchase
            };
        }
    }
}
