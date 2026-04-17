using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncSerializationTests
    {
        private static readonly ClientMessageFactory ClientFactory = new ClientMessageFactory();
        private static readonly ServerMessageFactory ServerFactory = new ServerMessageFactory();
        private static readonly NetClient Client = new NetClient(new NetPeerConfiguration("PERSISTENT_SYNC_TESTS"));

        [TestMethod]
        public void TestSerializeDeserializePersistentSyncRequestMsg()
        {
            var msgData = ClientFactory.CreateNewMessageData<PersistentSyncRequestMsgData>();
            msgData.DomainCount = 3;
            msgData.Domains = new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Science,
                PersistentSyncDomainId.Reputation
            };

            var deserialized = (PersistentSyncCliMsg)RoundTripClientMessage(ClientFactory.CreateNew<PersistentSyncCliMsg>(msgData));
            var roundTripData = (PersistentSyncRequestMsgData)deserialized.Data;

            Assert.AreEqual(3, roundTripData.DomainCount);
            CollectionAssert.AreEqual(msgData.Domains, roundTripData.Domains.Take(roundTripData.DomainCount).ToArray());
        }

        [TestMethod]
        public void TestSerializeDeserializePersistentSyncIntentMsg()
        {
            var payload = FundsIntentPayloadSerializer.Serialize(321.45d, "Career reward");
            var msgData = ClientFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            msgData.DomainId = PersistentSyncDomainId.Funds;
            msgData.ClientKnownRevision = 42;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;
            msgData.Reason = "Career reward";

            var deserialized = (PersistentSyncCliMsg)RoundTripClientMessage(ClientFactory.CreateNew<PersistentSyncCliMsg>(msgData));
            var roundTripData = (PersistentSyncIntentMsgData)deserialized.Data;

            Assert.AreEqual(PersistentSyncDomainId.Funds, roundTripData.DomainId);
            Assert.AreEqual(42L, roundTripData.ClientKnownRevision);
            Assert.AreEqual(payload.Length, roundTripData.NumBytes);
            Assert.AreEqual("Career reward", roundTripData.Reason);

            FundsIntentPayloadSerializer.Deserialize(roundTripData.Payload, roundTripData.NumBytes, out var funds, out var reason);
            Assert.AreEqual(321.45d, funds);
            Assert.AreEqual("Career reward", reason);
        }

        [TestMethod]
        public void TestSerializeDeserializePersistentSyncSnapshotMsg()
        {
            var payload = ReputationSnapshotPayloadSerializer.Serialize(12.5f);
            var msgData = ServerFactory.CreateNewMessageData<PersistentSyncSnapshotMsgData>();
            msgData.DomainId = PersistentSyncDomainId.Reputation;
            msgData.Revision = 7;
            msgData.AuthorityPolicy = PersistentAuthorityPolicy.AnyClientIntent;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;

            var deserialized = (PersistentSyncSrvMsg)RoundTripServerMessage(ServerFactory.CreateNew<PersistentSyncSrvMsg>(msgData));
            var roundTripData = (PersistentSyncSnapshotMsgData)deserialized.Data;

            Assert.AreEqual(PersistentSyncDomainId.Reputation, roundTripData.DomainId);
            Assert.AreEqual(7L, roundTripData.Revision);
            Assert.AreEqual(PersistentAuthorityPolicy.AnyClientIntent, roundTripData.AuthorityPolicy);
            Assert.AreEqual(12.5f, ReputationSnapshotPayloadSerializer.Deserialize(roundTripData.Payload, roundTripData.NumBytes));
        }

        [TestMethod]
        public void TestFundsPayloadSerializerRoundTrip()
        {
            var payload = FundsIntentPayloadSerializer.Serialize(999.5d, "Admin");
            FundsIntentPayloadSerializer.Deserialize(payload, payload.Length, out var funds, out var reason);
            Assert.AreEqual(999.5d, funds);
            Assert.AreEqual("Admin", reason);

            var snapshotPayload = FundsSnapshotPayloadSerializer.Serialize(999.5d);
            Assert.AreEqual(999.5d, FundsSnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestSciencePayloadSerializerRoundTrip()
        {
            var payload = ScienceIntentPayloadSerializer.Serialize(123.25f, "Lab");
            ScienceIntentPayloadSerializer.Deserialize(payload, payload.Length, out var science, out var reason);
            Assert.AreEqual(123.25f, science);
            Assert.AreEqual("Lab", reason);

            var snapshotPayload = ScienceSnapshotPayloadSerializer.Serialize(123.25f);
            Assert.AreEqual(123.25f, ScienceSnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestReputationPayloadSerializerRoundTrip()
        {
            var payload = ReputationIntentPayloadSerializer.Serialize(5.75f, "Contract");
            ReputationIntentPayloadSerializer.Deserialize(payload, payload.Length, out var reputation, out var reason);
            Assert.AreEqual(5.75f, reputation);
            Assert.AreEqual("Contract", reason);

            var snapshotPayload = ReputationSnapshotPayloadSerializer.Serialize(5.75f);
            Assert.AreEqual(5.75f, ReputationSnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestUpgradeableFacilitiesPayloadSerializerRoundTrip()
        {
            var intentPayload = UpgradeableFacilitiesIntentPayloadSerializer.Serialize("SpaceCenter/MissionControl", 2);
            UpgradeableFacilitiesIntentPayloadSerializer.Deserialize(intentPayload, intentPayload.Length, out var facilityId, out var level);
            Assert.AreEqual("SpaceCenter/MissionControl", facilityId);
            Assert.AreEqual(2, level);

            var facilities = new System.Collections.Generic.Dictionary<string, int>
            {
                ["SpaceCenter/MissionControl"] = 2,
                ["SpaceCenter/TrackingStation"] = 1
            };

            var snapshotPayload = UpgradeableFacilitiesSnapshotPayloadSerializer.Serialize(facilities);
            var roundTripFacilities = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length);

            Assert.AreEqual(2, roundTripFacilities["SpaceCenter/MissionControl"]);
            Assert.AreEqual(1, roundTripFacilities["SpaceCenter/TrackingStation"]);
            Assert.AreEqual(2, roundTripFacilities.Count);
        }

        [TestMethod]
        public void TestContractSnapshotPayloadSerializerRoundTrip()
        {
            var contractPayload = new[]
            {
                new ContractSnapshotInfo
                {
                    ContractGuid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ContractState = "Offered",
                    Placement = ContractSnapshotPlacement.Current,
                    Order = 0,
                    Data = System.Text.Encoding.UTF8.GetBytes("CONTRACT\n{\n guid = 11111111-1111-1111-1111-111111111111\n state = Offered\n}\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("CONTRACT\n{\n guid = 11111111-1111-1111-1111-111111111111\n state = Offered\n}\n")
                },
                new ContractSnapshotInfo
                {
                    ContractGuid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ContractState = "Completed",
                    Placement = ContractSnapshotPlacement.Finished,
                    Order = 5,
                    Data = System.Text.Encoding.UTF8.GetBytes("CONTRACT\n{\n guid = 22222222-2222-2222-2222-222222222222\n state = Completed\n}\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("CONTRACT\n{\n guid = 22222222-2222-2222-2222-222222222222\n state = Completed\n}\n")
                }
            };

            var snapshotPayload = ContractSnapshotPayloadSerializer.Serialize(contractPayload);
            var roundTripContracts = ContractSnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length);

            Assert.AreEqual(2, roundTripContracts.Count);
            Assert.AreEqual(contractPayload[0].ContractGuid, roundTripContracts[0].ContractGuid);
            Assert.AreEqual("Offered", roundTripContracts[0].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Current, roundTripContracts[0].Placement);
            Assert.AreEqual(0, roundTripContracts[0].Order);
            Assert.AreEqual(contractPayload[1].ContractGuid, roundTripContracts[1].ContractGuid);
            Assert.AreEqual("Completed", roundTripContracts[1].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Finished, roundTripContracts[1].Placement);
            Assert.AreEqual(5, roundTripContracts[1].Order);
        }

        [TestMethod]
        public void TestTechnologySnapshotPayloadSerializerRoundTrip()
        {
            var technologyPayload = new[]
            {
                new TechnologySnapshotInfo
                {
                    TechId = "basicRocketry",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = basicRocketry\nstate = Available\ncost = 5\npart = liquidEngine\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("id = basicRocketry\nstate = Available\ncost = 5\npart = liquidEngine\n")
                },
                new TechnologySnapshotInfo
                {
                    TechId = "engineering101",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = engineering101\nstate = Available\ncost = 15\npart = radialDecoupler\npart = stackSeparator\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("id = engineering101\nstate = Available\ncost = 15\npart = radialDecoupler\npart = stackSeparator\n")
                }
            };

            var snapshotPayload = TechnologySnapshotPayloadSerializer.Serialize(technologyPayload);
            var roundTripTechnologies = TechnologySnapshotPayloadSerializer.Deserialize(snapshotPayload, snapshotPayload.Length);

            Assert.AreEqual(2, roundTripTechnologies.Count);
            Assert.AreEqual("basicRocketry", roundTripTechnologies[0].TechId);
            Assert.AreEqual(technologyPayload[0].NumBytes, roundTripTechnologies[0].NumBytes);
            Assert.AreEqual("engineering101", roundTripTechnologies[1].TechId);
            Assert.AreEqual(technologyPayload[1].NumBytes, roundTripTechnologies[1].NumBytes);
        }

        [TestMethod]
        public void TestStrategySnapshotPayloadSerializerRoundTrip()
        {
            var strategyPayload = new[]
            {
                new StrategySnapshotInfo
                {
                    Name = "BailoutGrant",
                    Data = System.Text.Encoding.UTF8.GetBytes("name = BailoutGrant\nfactor = 0.25\nisActive = True\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("name = BailoutGrant\nfactor = 0.25\nisActive = True\n")
                }
            };

            var snapshotPayload = StrategySnapshotPayloadSerializer.Serialize(strategyPayload);
            var roundTrip = StrategySnapshotPayloadSerializer.Deserialize(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("BailoutGrant", roundTrip[0].Name);
            Assert.AreEqual(strategyPayload[0].NumBytes, roundTrip[0].NumBytes);
        }

        [TestMethod]
        public void TestAchievementSnapshotPayloadSerializerRoundTrip()
        {
            var achievementPayload = new[]
            {
                new AchievementSnapshotInfo
                {
                    Id = "Kerbin",
                    Data = System.Text.Encoding.UTF8.GetBytes("Kerbin\n{\n state = Complete\n}\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("Kerbin\n{\n state = Complete\n}\n")
                }
            };

            var snapshotPayload = AchievementSnapshotPayloadSerializer.Serialize(achievementPayload);
            var roundTrip = AchievementSnapshotPayloadSerializer.Deserialize(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("Kerbin", roundTrip[0].Id);
            Assert.AreEqual(achievementPayload[0].NumBytes, roundTrip[0].NumBytes);
        }

        [TestMethod]
        public void TestScienceSubjectSnapshotPayloadSerializerRoundTrip()
        {
            var subjectPayload = new[]
            {
                new ScienceSubjectSnapshotInfo
                {
                    Id = "crewReport@KerbinSrfLandedLaunchPad",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = crewReport@KerbinSrfLandedLaunchPad\nscience = 1\nscienceCap = 5\n"),
                    NumBytes = System.Text.Encoding.UTF8.GetByteCount("id = crewReport@KerbinSrfLandedLaunchPad\nscience = 1\nscienceCap = 5\n")
                }
            };

            var snapshotPayload = ScienceSubjectSnapshotPayloadSerializer.Serialize(subjectPayload);
            var roundTrip = ScienceSubjectSnapshotPayloadSerializer.Deserialize(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual(subjectPayload[0].Id, roundTrip[0].Id);
            Assert.AreEqual(subjectPayload[0].NumBytes, roundTrip[0].NumBytes);
        }

        [TestMethod]
        public void TestExperimentalPartsSnapshotPayloadSerializerRoundTrip()
        {
            var snapshotPayload = ExperimentalPartsSnapshotPayloadSerializer.Serialize(new[]
            {
                new ExperimentalPartSnapshotInfo { PartName = "liquidEngine", Count = 2 },
                new ExperimentalPartSnapshotInfo { PartName = "radialDecoupler", Count = 1 }
            });
            var roundTrip = ExperimentalPartsSnapshotPayloadSerializer.Deserialize(snapshotPayload);

            Assert.AreEqual(2, roundTrip.Length);
            Assert.AreEqual("liquidEngine", roundTrip[0].PartName);
            Assert.AreEqual(2, roundTrip[0].Count);
            Assert.AreEqual("radialDecoupler", roundTrip[1].PartName);
            Assert.AreEqual(1, roundTrip[1].Count);
        }

        [TestMethod]
        public void TestPartPurchasesSnapshotPayloadSerializerRoundTrip()
        {
            var snapshotPayload = PartPurchasesSnapshotPayloadSerializer.Serialize(new[]
            {
                new PartPurchaseSnapshotInfo
                {
                    TechId = "engineering101",
                    PartNames = new[] { "radialDecoupler", "stackSeparator" }
                }
            });
            var roundTrip = PartPurchasesSnapshotPayloadSerializer.Deserialize(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("engineering101", roundTrip[0].TechId);
            CollectionAssert.AreEqual(new[] { "radialDecoupler", "stackSeparator" }, roundTrip[0].PartNames);
        }

        private static LmpCommon.Message.Interface.IMessageBase RoundTripClientMessage(PersistentSyncCliMsg message)
        {
            var outgoing = Client.CreateMessage(message.GetMessageSize());
            message.Serialize(outgoing);
            var data = outgoing.ReadBytes(outgoing.LengthBytes);
            var incoming = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            incoming.LengthBytes = outgoing.LengthBytes;
            message.Recycle();
            return ClientFactory.Deserialize(incoming, Environment.TickCount);
        }

        private static LmpCommon.Message.Interface.IMessageBase RoundTripServerMessage(PersistentSyncSrvMsg message)
        {
            var outgoing = Client.CreateMessage(message.GetMessageSize());
            message.Serialize(outgoing);
            var data = outgoing.ReadBytes(outgoing.LengthBytes);
            var incoming = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            incoming.LengthBytes = outgoing.LengthBytes;
            message.Recycle();
            return ServerFactory.Deserialize(incoming, Environment.TickCount);
        }
    }
}
