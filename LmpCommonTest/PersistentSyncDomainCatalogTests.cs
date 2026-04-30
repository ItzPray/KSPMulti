using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncDomainCatalogTests
    {
        [TestMethod]
        public void CatalogContainsEveryDomainIdExactlyOnce()
        {
            var catalogIds = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId).ToArray();
            var enumIds = Enum.GetValues(typeof(PersistentSyncDomainId))
                .Cast<PersistentSyncDomainId>()
                .OrderBy(id => (byte)id)
                .ToArray();

            CollectionAssert.AreEquivalent(enumIds, catalogIds);
            Assert.AreEqual(catalogIds.Length, catalogIds.Distinct().Count(), "Every persistent sync domain must have exactly one catalog row.");
        }

        [TestMethod]
        public void CatalogOrderMatchesCurrentInitialSyncOrder()
        {
            var expected = new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Science,
                PersistentSyncDomainId.Reputation,
                PersistentSyncDomainId.Strategy,
                PersistentSyncDomainId.Achievements,
                PersistentSyncDomainId.ScienceSubjects,
                PersistentSyncDomainId.Technology,
                PersistentSyncDomainId.ExperimentalParts,
                PersistentSyncDomainId.PartPurchases,
                PersistentSyncDomainId.UpgradeableFacilities,
                PersistentSyncDomainId.Contracts,
                PersistentSyncDomainId.GameLaunchId
            };

            var actual = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId).ToArray();
            CollectionAssert.AreEqual(expected, actual);
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
    }
}
