using LmpCommon.PersistentSync;

namespace LmpCommonTest
{
    /// <summary>
    /// Test-only catalog configuration mirroring production <see cref="string"/> ordering for unit tests that drive <see cref="PersistentSyncDomainCatalog"/>.
    /// </summary>
    internal static class PersistentSyncTestDomainCatalog
    {
        public static void Configure()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            registrar.Register(PersistentSyncDomain.Define("Funds", 0)).OwnsStockScenario("Funding").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Science", 1)).OwnsStockScenario("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Reputation", 2)).OwnsStockScenario("Reputation").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("UpgradeableFacilities", 3)).OwnsStockScenario("ScenarioUpgradeableFacilities").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Contracts", 4)).OwnsStockScenario("ContractSystem").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Technology", 5)).OwnsStockScenario("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Strategy", 6)).OwnsStockScenario("StrategySystem").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Achievements", 7)).OwnsStockScenario("ProgressTracking").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("ScienceSubjects", 8)).OwnsStockScenario("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("ExperimentalParts", 9)).OwnsStockScenario("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("PartPurchases", 10))
                .OwnsStockScenario("ResearchAndDevelopment")
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(PersistentSyncDomain.Define("Technology", 5))
                .UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("GameLaunchId", 11)).OwnsStockScenario("LmpGameLaunchId").UsesClientDomain<object>();

            PersistentSyncDomainCatalog.Configure(registrar.BuildDefinitions());
        }
    }
}