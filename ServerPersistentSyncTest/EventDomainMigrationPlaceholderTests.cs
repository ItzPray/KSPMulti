using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ServerPersistentSyncTest
{
    /// <summary>
    /// Migration-era placeholders removed; shared payload envelopes are covered here at the serializer/store boundary.
    /// </summary>
    [TestClass]
    public class PersistentSyncPayloadEnvelopeTests
    {
        [TestMethod]
        public void TechnologyPayloadEnvelope_RoundTripsThroughSerializer()
        {
            var payload = new TechnologyPayload
            {
                Technologies = new[] { new TechnologySnapshotInfo { TechId = "basicRocketry", Data = new byte[] { 1, 2, 3 } } },
                PartPurchases = Array.Empty<PartPurchaseSnapshotInfo>()
            };
            var bytes = PersistentSyncPayloadSerializer.Serialize(payload);
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<TechnologyPayload>(bytes, bytes.Length);
            Assert.AreEqual(1, roundTrip.Technologies.Length);
            Assert.AreEqual("basicRocketry", roundTrip.Technologies[0].TechId);
            Assert.AreEqual(0, roundTrip.PartPurchases.Length);
        }

        [TestMethod]
        public void ContractsPayloadEnvelope_RoundTripsSnapshotMode()
        {
            var payload = new ContractsPayload
            {
                Snapshot = new ContractSnapshotPayload
                {
                    Mode = ContractSnapshotPayloadMode.Delta,
                    Contracts = new System.Collections.Generic.List<ContractSnapshotInfo>()
                }
            };
            var bytes = PersistentSyncPayloadSerializer.Serialize(payload);
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<ContractsPayload>(bytes, bytes.Length);
            Assert.IsNotNull(roundTrip.Snapshot);
            Assert.AreEqual(ContractSnapshotPayloadMode.Delta, roundTrip.Snapshot.Mode);
        }
    }
}
