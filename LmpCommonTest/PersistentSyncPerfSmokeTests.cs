using System;
using System.Diagnostics;
using System.Text;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.Achievements;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    /// <summary>
    /// Non-benchmark smoke checks: CI timing variance means these only guard obvious regressions (throws, extreme slowdown).
    /// </summary>
    [TestClass]
    public class PersistentSyncPerfSmokeTests
    {
        [TestMethod]
        public void CatalogLookup_WallClockCeiling_IsGenerous()
        {
            PersistentSyncTestDomainCatalog.Configure();
            PersistentSyncDomainDefinition def = null;
            var sw = Stopwatch.StartNew();
            var count = Math.Max(1, PersistentSyncDomainCatalog.AllOrdered.Count);
            for (var i = 0; i < 50_000; i++)
            {
                var wire = (ushort)(i % count);
                Assert.IsTrue(PersistentSyncDomainCatalog.TryGetByWireId(wire, out def));
                Assert.IsTrue(PersistentSyncDomainCatalog.TryGet(def.Name, out _));
            }

            sw.Stop();
            Assert.IsTrue(sw.Elapsed.TotalSeconds < 30d, $"catalog hot loop took {sw.Elapsed} (smoke ceiling; not a benchmark)");
            Assert.IsNotNull(def);
        }

        [TestMethod]
        public void RootArrayEnvelopeSerializers_LoopWithoutExceptions()
        {
            // Allocation smoke: GC.GetAllocatedBytesForCurrentThread is not relied on here (net472 availability varies).
            var strategy = new StrategyPayload { Items = new[] { new StrategySnapshotInfo { Name = "s", Data = new byte[] { 1, 2 } } } };
            var achievements = new AchievementsPayload { Items = new[] { new AchievementSnapshotInfo { Id = "a", Data = Encoding.UTF8.GetBytes("{}") } } };
            var subjects = new ScienceSubjectsPayload { Items = new[] { new ScienceSubjectSnapshotInfo { Id = "id", Data = Encoding.UTF8.GetBytes("id=x\n") } } };
            var experimental = new ExperimentalPartsPayload { Items = new[] { new ExperimentalPartSnapshotInfo { PartName = "p", Count = 3 } } };
            var facilities = new UpgradeableFacilitiesPayload { Items = new[] { new UpgradeableFacilityLevelPayload { FacilityId = "SpaceCenter/X", Level = 1 } } };
            var purchases = new PartPurchasesPayload { Items = new[] { new PartPurchaseSnapshotInfo { TechId = "t", PartNames = new[] { "a" } } } };

            for (var i = 0; i < 2000; i++)
            {
                RoundTrip(strategy);
                RoundTrip(achievements);
                RoundTrip(subjects);
                RoundTrip(experimental);
                RoundTrip(facilities);
                RoundTrip(purchases);
            }
        }

        private static void RoundTrip<T>(T payload)
        {
            var bytes = PersistentSyncPayloadSerializer.Serialize(payload);
            PersistentSyncPayloadSerializer.Deserialize<T>(bytes, bytes.Length);
        }
    }
}
