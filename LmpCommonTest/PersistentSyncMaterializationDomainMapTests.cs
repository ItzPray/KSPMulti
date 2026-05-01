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
            Assert.AreEqual(PersistentSyncMaterializationSlot.Funding, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Funds));
            Assert.AreEqual(PersistentSyncMaterializationSlot.Reputation, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Reputation));
            Assert.AreEqual(PersistentSyncMaterializationSlot.StrategySystem, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Strategy));
        }

        [TestMethod]
        public void Maps_RdRelatedDomains_ToSingleResearchAndDevelopmentSlot()
        {
            var slot = PersistentSyncMaterializationSlot.ResearchAndDevelopment;
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Science));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Technology));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.PartPurchases));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.ExperimentalParts));
            Assert.AreEqual(slot, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.ScienceSubjects));
        }

        [TestMethod]
        public void Maps_Facilities_ToUpgradeableSlot()
        {
            Assert.AreEqual(PersistentSyncMaterializationSlot.UpgradeableFacilities, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.UpgradeableFacilities));
        }

        [TestMethod]
        public void Maps_NonMaterializedDomains_ToNone()
        {
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.GameLaunchId));
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Contracts));
            Assert.AreEqual(PersistentSyncMaterializationSlot.None, PersistentSyncMaterializationDomainMap.GetSlot(PersistentSyncDomainId.Achievements));
        }
    }
}