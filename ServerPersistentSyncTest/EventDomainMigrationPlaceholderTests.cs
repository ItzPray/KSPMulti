using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServerPersistentSyncTest
{
    /// <summary>
    /// Stage E0 placeholders for persistent-sync migration of event-driven career domains (no domain logic yet).
    /// </summary>
    [TestClass]
    public class EventDomainMigrationPlaceholderTests
    {
        [TestMethod]
        [Ignore("E0+: facility persistent snapshot serializer round-trip when Facility domain payload exists in LmpCommon.")]
        public void FacilitySnapshotSerializer_RoundTrip_NotYetImplemented()
        {
        }

        [TestMethod]
        [Ignore("E0+: contract snapshot / store round-trip when Contract domain migrates to PersistentSync.")]
        public void ContractSnapshotStore_RoundTrip_NotYetImplemented()
        {
        }

        [TestMethod]
        [Ignore("E0+: tech / R&D snapshot round-trip when Technology domain migrates to PersistentSync.")]
        public void TechResearchAndDevelopmentSnapshot_RoundTrip_NotYetImplemented()
        {
        }
    }
}
