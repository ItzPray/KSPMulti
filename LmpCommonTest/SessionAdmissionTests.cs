using Lidgren.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Handshake;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LmpCommonTest
{
    [TestClass]
    public class SessionAdmissionTests
    {
        [TestMethod]
        public void TryValidateClientHandshake_matching_fork_and_build_succeeds()
        {
            var ok = SessionAdmission.TryValidateClientHandshake(
                SessionAdmission.LocalProtocolForkId,
                SessionAdmission.LocalExactBuild,
                out var reason,
                out var reply);

            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(HandshakeReply.HandshookSuccessfully, reply);
        }

        [TestMethod]
        public void TryValidateClientHandshake_wrong_fork_fails()
        {
            var ok = SessionAdmission.TryValidateClientHandshake(
                "other.fork",
                SessionAdmission.LocalExactBuild,
                out var reason,
                out var reply);

            Assert.IsFalse(ok);
            Assert.AreEqual(HandshakeReply.ProtocolForkMismatch, reply);
            StringAssert.Contains(reason, "other.fork");
        }

        [TestMethod]
        public void TryValidateClientHandshake_same_fork_wrong_build_fails()
        {
            var ok = SessionAdmission.TryValidateClientHandshake(
                SessionAdmission.LocalProtocolForkId,
                "not-the-local-build",
                out var reason,
                out var reply);

            Assert.IsFalse(ok);
            Assert.AreEqual(HandshakeReply.ProtocolBuildMismatch, reply);
            StringAssert.Contains(reason, "not-the-local-build");
        }

        [TestMethod]
        public void TryValidateClientHandshake_empty_fork_fails()
        {
            Assert.IsFalse(SessionAdmission.TryValidateClientHandshake("", "x", out _, out var r1));
            Assert.AreEqual(HandshakeReply.ProtocolForkMismatch, r1);
            Assert.IsFalse(SessionAdmission.TryValidateClientHandshake("   ", "x", out _, out var r2));
            Assert.AreEqual(HandshakeReply.ProtocolForkMismatch, r2);
        }

        [TestMethod]
        public void TryValidateClientHandshake_empty_build_fails()
        {
            Assert.IsFalse(SessionAdmission.TryValidateClientHandshake(SessionAdmission.LocalProtocolForkId, "", out _, out var r1));
            Assert.AreEqual(HandshakeReply.ProtocolBuildMismatch, r1);
            Assert.IsFalse(SessionAdmission.TryValidateClientHandshake(SessionAdmission.LocalProtocolForkId, "  ", out _, out var r2));
            Assert.AreEqual(HandshakeReply.ProtocolBuildMismatch, r2);
        }

        [TestMethod]
        public void IsAdvertisedServerCompatible_uses_same_rule_as_handshake()
        {
            Assert.IsTrue(SessionAdmission.IsAdvertisedServerCompatible(
                SessionAdmission.LocalProtocolForkId,
                SessionAdmission.LocalExactBuild));

            Assert.IsFalse(SessionAdmission.IsAdvertisedServerCompatible("", SessionAdmission.LocalExactBuild));
            Assert.IsFalse(SessionAdmission.IsAdvertisedServerCompatible(SessionAdmission.LocalProtocolForkId, ""));
        }

        [TestMethod]
        public void ClientMessageFactory_never_sets_VersionMismatch_on_wire_version_skew()
        {
            var factory = new ClientMessageFactory();
            var msgData = factory.CreateNewMessageData<HandshakeRequestMsgData>();
            msgData.PlayerName = "n";
            msgData.UniqueIdentifier = "id";
            msgData.ProtocolForkId = "x";
            msgData.ExactClientBuild = "y";
            msgData.MajorVersion = 9999;
            msgData.MinorVersion = 9999;
            msgData.BuildVersion = 9999;

            var msg = factory.CreateNew<HandshakeCliMsg>(msgData);
            var client = new NetClient(new NetPeerConfiguration("TESTS"));
            var lidgrenMsgSend = client.CreateMessage(msg.GetMessageSize());
            msg.Serialize(lidgrenMsgSend);
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;
            msg.Recycle();

            var deserialized = factory.Deserialize(lidgrenMsgRecv, Environment.TickCount);
            Assert.IsFalse(deserialized.VersionMismatch);
        }

        [TestMethod]
        public void HandshakeRequest_round_trips_fork_and_exact_build_fields()
        {
            var factory = new ClientMessageFactory();
            var msgData = factory.CreateNewMessageData<HandshakeRequestMsgData>();
            msgData.PlayerName = "player";
            msgData.UniqueIdentifier = "uid";
            msgData.ProtocolForkId = SessionAdmission.LocalProtocolForkId;
            msgData.ExactClientBuild = SessionAdmission.LocalExactBuild;

            var msg = factory.CreateNew<HandshakeCliMsg>(msgData);
            var client = new NetClient(new NetPeerConfiguration("TESTS"));
            var lidgrenMsgSend = client.CreateMessage(msg.GetMessageSize());
            msg.Serialize(lidgrenMsgSend);
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;
            msg.Recycle();

            var deserialized = factory.Deserialize(lidgrenMsgRecv, Environment.TickCount);
            var roundTrip = (HandshakeRequestMsgData)deserialized.Data;
            Assert.AreEqual(SessionAdmission.LocalProtocolForkId, roundTrip.ProtocolForkId);
            Assert.AreEqual(SessionAdmission.LocalExactBuild, roundTrip.ExactClientBuild);
        }
    }
}
