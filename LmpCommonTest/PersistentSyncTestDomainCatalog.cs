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

            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Funds)).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Science)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Reputation)).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.UpgradeableFacilities)).WithStockScenarioMetadata("ScenarioUpgradeableFacilities").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Contracts)).WithStockScenarioMetadata("ContractSystem").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Technology)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Strategy)).WithStockScenarioMetadata("StrategySystem").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.Achievements)).WithStockScenarioMetadata("ProgressTracking").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.ScienceSubjects)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.ExperimentalParts)).WithStockScenarioMetadata("ResearchAndDevelopment").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.PartPurchases))
                .WithStockScenarioMetadata("ResearchAndDevelopment")
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(PersistentSyncDomainNames.Technology)
                .UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey(PersistentSyncDomainNames.GameLaunchId)).WithStockScenarioMetadata("LmpGameLaunchId").UsesClientDomain<object>();

            PersistentSyncDomainCatalog.Configure(registrar.BuildDefinitions());
        }
    }
}
