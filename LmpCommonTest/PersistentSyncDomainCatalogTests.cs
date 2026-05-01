using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncDomainCatalogTests
    {
        [TestInitialize]
        public void Setup()
        {
            PersistentSyncTestDomainCatalog.Configure();
        }

        [TestMethod]
        public void CatalogContainsEveryDomainIdExactlyOnce()
        {
            var catalogIds = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId).ToArray();
            var expectedIds = new[]
            {
                PersistentSyncDomainNames.Funds,
                PersistentSyncDomainNames.Science,
                PersistentSyncDomainNames.Reputation,
                PersistentSyncDomainNames.UpgradeableFacilities,
                PersistentSyncDomainNames.Contracts,
                PersistentSyncDomainNames.Technology,
                PersistentSyncDomainNames.Strategy,
                PersistentSyncDomainNames.Achievements,
                PersistentSyncDomainNames.ScienceSubjects,
                PersistentSyncDomainNames.ExperimentalParts,
                PersistentSyncDomainNames.PartPurchases,
                PersistentSyncDomainNames.GameLaunchId
            };

            CollectionAssert.AreEquivalent(expectedIds, catalogIds);
            Assert.AreEqual(catalogIds.Length, catalogIds.Distinct().Count(), "Every persistent sync domain must have exactly one catalog row.");
        }

        [TestMethod]
        public void CatalogOrderIsDeterministicAndDependencyAware()
        {
            var actual = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId).ToArray();

            Assert.IsTrue(
                Array.IndexOf(actual, PersistentSyncDomainNames.Technology) < Array.IndexOf(actual, PersistentSyncDomainNames.PartPurchases),
                "PartPurchases must sort after its Technology owner.");
        }

        [TestMethod]
        public void CatalogOwnsServerScenarioBypasses()
        {
            var bypasses = PersistentSyncDomainCatalog.GetServerScenarioBypasses().OrderBy(s => s).ToArray();

            var expected = new[]
            {
                "ContractSystem",
                "Funding",
                "LmpGameLaunchId",
                "ProgressTracking",
                "Reputation",
                "ResearchAndDevelopment",
                "ScenarioUpgradeableFacilities",
                "StrategySystem"
            }.OrderBy(s => s).ToArray();

            CollectionAssert.AreEqual(expected, bypasses);
        }

        [TestMethod]
        public void RegistrarRejectsDuplicateDomainNames()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            registrar.Register(new PersistentSyncDomainKey("Duplicate", 100)).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey("Duplicate", 101)).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();

            Assert.ThrowsException<InvalidOperationException>(() => registrar.BuildDefinitions());
        }

        [TestMethod]
        public void RegistrarRejectsDuplicateWireIds()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            registrar.Register(new PersistentSyncDomainKey("First", 100)).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey("Second", 100)).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();

            Assert.ThrowsException<InvalidOperationException>(() => registrar.BuildDefinitions());
        }

        [TestMethod]
        public void UnknownStockScenarioRequiresExplicitMetadata()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            Assert.ThrowsException<InvalidOperationException>(() =>
                registrar.Register(new PersistentSyncDomainKey("Unknown", 100)).WithStockScenarioMetadata("SomeFutureScenario"));
        }
    }
}
