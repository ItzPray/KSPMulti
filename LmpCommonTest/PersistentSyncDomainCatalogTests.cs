using LmpCommon.Enums;
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
            registrar.Register(new PersistentSyncDomainKey("Duplicate")).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey("Duplicate")).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();

            Assert.ThrowsException<InvalidOperationException>(() => registrar.BuildDefinitions());
        }

        [TestMethod]
        public void RegistrarAssignsSessionWireIdsToExplicitNameRegistrations()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            registrar.Register(new PersistentSyncDomainKey("First")).WithStockScenarioMetadata("Funding").UsesClientDomain<object>();
            registrar.Register(new PersistentSyncDomainKey("Second")).WithStockScenarioMetadata("Reputation").UsesClientDomain<object>();

            var definitions = registrar.BuildDefinitions();

            Assert.AreEqual((ushort)0, definitions.Single(d => d.DomainId == "First").WireId);
            Assert.AreEqual((ushort)1, definitions.Single(d => d.DomainId == "Second").WireId);
        }

        [TestMethod]
        public void RegisterCurrentAssignsSessionWireIdsFromRegistrationOrder()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            registrar.WithCurrentDomainType(typeof(SessionSecondSyncClientDomain), () =>
                registrar.RegisterCurrent().ForGameModes(GameMode.Career));
            registrar.WithCurrentDomainType(typeof(SessionFirstSyncClientDomain), () =>
                registrar.RegisterCurrent().ForGameModes(GameMode.Career));

            var definitions = registrar.BuildDefinitions();

            Assert.AreEqual((ushort)0, definitions.Single(d => d.DomainId == "SessionSecond").WireId);
            Assert.AreEqual((ushort)1, definitions.Single(d => d.DomainId == "SessionFirst").WireId);
        }

        [TestMethod]
        public void ClientRegisterCurrentDoesNotCopyScenarioMetadata()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            registrar.WithCurrentDomainType(typeof(ClientAttributedSyncClientDomain), () =>
                registrar.RegisterCurrent().UsesClientDomain<ClientAttributedSyncClientDomain>());

            var definition = registrar.BuildDefinitions().Single();

            Assert.AreEqual("ClientAttributed", definition.DomainId);
            Assert.IsNull(definition.ScenarioName);
            Assert.IsNull(definition.ScalarFieldName);
            Assert.AreEqual(0, definition.ServerScenarioBypasses.Length);
        }

        [TestMethod]
        public void UnknownStockScenarioRequiresExplicitMetadata()
        {
            var registrar = new PersistentSyncClientDomainRegistrar();

            Assert.ThrowsException<InvalidOperationException>(() =>
                registrar.Register(new PersistentSyncDomainKey("Unknown")).WithStockScenarioMetadata("SomeFutureScenario"));
        }

        private sealed class SessionFirstSyncClientDomain
        {
        }

        private sealed class SessionSecondSyncClientDomain
        {
        }

        [PersistentSyncStockScenario("Funding", ScalarField = "funds")]
        private sealed class ClientAttributedSyncClientDomain
        {
        }
    }
}
