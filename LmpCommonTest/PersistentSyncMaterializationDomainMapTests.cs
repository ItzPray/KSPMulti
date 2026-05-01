using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncMaterializationDomainMapTests
    {
        [TestInitialize]
        public void Setup()
        {
            PersistentSyncTestDomainCatalog.Configure();
        }

        [TestMethod]
        public void Maps_ScalarCareerDomains_ToDistinctScenarioSlots()
        {
            Assert.AreEqual(PersistentSyncMaterializationSlot.Funding, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Funds));
            Assert.AreEqual(PersistentSyncMaterializationSlot.Reputation, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Reputation));
            Assert.AreEqual(PersistentSyncMaterializationSlot.StrategySystem, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Strategy));
        }

        [TestMethod]
        public void Maps_RdRelatedDomains_ToSingleResearchAndDevelopmentSlot()
        {
            var slot = PersistentSyncMaterializationSlot.ResearchAndDevelopment;
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Science));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Technology));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.PartPurchases));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.ExperimentalParts));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.ScienceSubjects));
        }

        [TestMethod]
        public void Maps_Facilities_ToUpgradeableSlot()
        {
            Assert.AreEqual(PersistentSyncMaterializationSlot.UpgradeableFacilities, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.UpgradeableFacilities));
        }

        [TestMethod]
        public void Maps_NonMaterializedDomains_ToNone()
        {
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.GameLaunchId));
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Contracts));
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainNames.Achievements));
        }
    }
}