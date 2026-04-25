using System;
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

        [TestMethod]
        public void AreExactBuildsCompatible_ignores_compiled_suffix_mismatch()
        {
            Assert.IsTrue(SessionAdmission.AreExactBuildsCompatible("0.32.0", "0.32.0-compiled"));
            Assert.IsTrue(SessionAdmission.AreExactBuildsCompatible("0.32.0-compiled", "0.32.0"));
        }

        [TestMethod]
        public void AreExactBuildsCompatible_still_distinguishes_different_releases()
        {
            Assert.IsFalse(SessionAdmission.AreExactBuildsCompatible("0.32.0", "0.31.0"));
        }

        [TestMethod]
        public void EvaluateHandshakeRequest_allows_client_plain_when_server_uses_compiled_info_version()
        {
            var l = SessionAdmission.LocalExactBuild;
            if (!l.EndsWith("-compiled", StringComparison.OrdinalIgnoreCase))
                Assert.Inconclusive("This build does not use an Informational string ending in -compiled; no plain-vs-suffix test.");
            var withoutSuffix = l.Substring(0, l.Length - "-compiled".Length);
            var system = new HandshakeSystem();
            var result = system.EvaluateHandshakeRequest(CreateRequest(SessionAdmission.LocalProtocolForkId, withoutSuffix));
            Assert.IsTrue(result.Allowed, result.Reason);
        }
    }
}
