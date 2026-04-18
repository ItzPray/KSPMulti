using LmpCommon.Enums;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Describes which stock KSP persistence subsystems are present for the active save/session.
    /// Drives which persistent-sync domains participate in startup convergence and local production,
    /// instead of scattering ad hoc <see cref="GameMode"/> checks across client systems.
    /// </summary>
    public struct PersistentSyncSessionCapabilities
    {
        public bool HasFundingScenario;
        public bool HasReputationScenario;
        public bool HasResearchAndDevelopmentScenario;
        public bool HasProgressTrackingScenario;
        public bool HasContractSystemScenario;
        public bool HasUpgradeableFacilitiesScenario;
        public bool HasStrategySystemScenario;

        /// <summary>
        /// When false, stock difficulty has <c>BypassEntryPurchaseAfterResearch</c> (no per-part entry purchase).
        /// Share producers skip mirroring individual purchases; R&amp;D still converges via PersistentSync.
        /// </summary>
        public bool PartPurchaseMechanismEnabled;

        /// <summary>
        /// Conservative optimistic defaults when the save cannot be inspected yet: mirrors stock career/science expectations.
        /// </summary>
        public static PersistentSyncSessionCapabilities OptimisticForServerGameMode(GameMode serverGameMode)
        {
            var careerLike = (serverGameMode & GameMode.Career) != 0;
            var progression = (serverGameMode & (GameMode.Career | GameMode.Science)) != 0;

            return new PersistentSyncSessionCapabilities
            {
                HasFundingScenario = careerLike,
                HasReputationScenario = careerLike,
                HasResearchAndDevelopmentScenario = progression,
                HasProgressTrackingScenario = progression,
                HasContractSystemScenario = careerLike,
                HasUpgradeableFacilitiesScenario = careerLike,
                HasStrategySystemScenario = careerLike,
                PartPurchaseMechanismEnabled = true
            };
        }
    }
}
