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
