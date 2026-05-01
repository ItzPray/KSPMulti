using LmpCommon.PersistentSync;

namespace LmpCommonTest
{
    /// <summary>
    /// Test-only catalog configuration mirroring production domain ordering for unit tests that drive <see cref="PersistentSyncDomainCatalog"/>.
    /// </summary>
    internal static class PersistentSyncTestDomainCatalog
    {
        public static void Configure()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Funds, 0)).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Science, 1)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Reputation, 2)).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.UpgradeableFacilities, 3)).WithStockScenarioMetadata("ScenarioUpgradeableFacilities").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Contracts, 4)).WithStockScenarioMetadata("ContractSystem").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Technology, 5)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Strategy, 6)).WithStockScenarioMetadata("StrategySystem").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Achievements, 7)).WithStockScenarioMetadata("ProgressTracking").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.ScienceSubjects, 8)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.ExperimentalParts, 9)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.PartPurchases, 10))
                .WithStockScenarioMetadata("ResearchAndDevelopment")
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(PersistentSyncDomainNames.Technology)
                .UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.GameLaunchId, 11)).WithStockScenarioMetadata("LmpGameLaunchId").UsesClientDomain<object>();

            PersistentSyncDomainCatalog.Configure(registrar.BuildDefinitions());
        }
    }
}
