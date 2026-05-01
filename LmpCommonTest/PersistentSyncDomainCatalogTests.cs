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
            var enumIds = Enum.GetValues(typeof(string))
                .Cast<string>()
                .OrderBy(id => (byte)id)
                .ToArray();

            CollectionAssert.AreEquivalent(enumIds, catalogIds);
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
            registrar.Register(PersistentSyncDomain.Define("Duplicate", 100)).OwnsStockScenario("Funding").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Duplicate", 101)).OwnsStockScenario("Reputation").UsesClientDomain<object>();

            Assert.ThrowsException<InvalidOperationException>(() => registrar.BuildDefinitions());
        }

        [TestMethod]
        public void RegistrarRejectsDuplicateWireIds()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            registrar.Register(PersistentSyncDomain.Define("First", 100)).OwnsStockScenario("Funding").UsesClientDomain<object>();
            registrar.Register(PersistentSyncDomain.Define("Second", 100)).OwnsStockScenario("Reputation").UsesClientDomain<object>();

            Assert.ThrowsException<InvalidOperationException>(() => registrar.BuildDefinitions());
        }

        [TestMethod]
        public void UnknownStockScenarioRequiresExplicitMetadata()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            Assert.ThrowsException<InvalidOperationException>(() =>
                registrar.Register(PersistentSyncDomain.Define("Unknown", 100)).OwnsStockScenario("SomeFutureScenario"));
        }
    }
}