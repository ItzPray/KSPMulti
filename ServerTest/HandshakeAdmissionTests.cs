using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Handshake;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;

namespace ServerTest
{
    [TestClass]
    public class HandshakeAdmissionTests
    {
        private static HandshakeRequestMsgData CreateRequest(string protocolForkId, string exactClientBuild)
        {
            return new HandshakeRequestMsgData
            {
                PlayerName = "test-player",
                UniqueIdentifier = "test-unique-id",
                ProtocolForkId = protocolForkId,
                ExactClientBuild = exactClientBuild
            };
        }

        [TestMethod]
        public void EvaluateHandshakeRequest_matching_protocol_and_build_allows_authentication()
        {
            var system = new HandshakeSystem();
            var result = system.EvaluateHandshakeRequest(CreateRequest(
                SessionAdmission.LocalProtocolForkId,
                SessionAdmission.LocalExactBuild));

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(HandshakeReply.HandshookSuccessfully, result.Reply);
            Assert.AreEqual(string.Empty, result.Reason);
        }

        [TestMethod]
        public void EvaluateHandshakeRequest_wrong_protocol_rejects_before_authentication()
        {
            var system = new HandshakeSystem();
            var result = system.EvaluateHandshakeRequest(CreateRequest(
                "stock.lmp",
                SessionAdmission.LocalExactBuild));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(HandshakeReply.ProtocolForkMismatch, result.Reply);
            StringAssert.Contains(result.Reason, "Protocol/fork mismatch");
        }

        [TestMethod]
        public void EvaluateHandshakeRequest_wrong_build_rejects_before_authentication()
        {
            var system = new HandshakeSystem();
            var result = system.EvaluateHandshakeRequest(CreateRequest(
                SessionAdmission.LocalProtocolForkId,
                "0.29.0-stock"));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(HandshakeReply.ProtocolBuildMismatch, result.Reply);
            StringAssert.Contains(result.Reason, "Exact build mismatch");
        }
    }
}
