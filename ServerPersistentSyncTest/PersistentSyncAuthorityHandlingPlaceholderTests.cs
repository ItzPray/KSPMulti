using LmpCommon.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServerPersistentSyncTest
{
    /// <summary>
    /// Placeholders for authority-policy handling once server hooks validate intent vs snapshot policy.
    /// </summary>
    [TestClass]
    public class PersistentSyncAuthorityHandlingPlaceholderTests
    {
        [TestMethod]
        [Ignore("Stage 1+: assert AnyClientIntent intent acceptance rules when authority hooks land.")]
        public void AnyClientIntent_AuthorityHandling_NotYetImplemented()
        {
        }

        [TestMethod]
        [Ignore("Stage 1+: assert ServerDerived mutations bypass client intent gates when hooks land.")]
        public void ServerDerived_AuthorityHandling_NotYetImplemented()
        {
        }

        [TestMethod]
        [Ignore("Stage 1+: assert LockOwnerIntent requires active lock owner when hooks land.")]
        public void LockOwnerIntent_AuthorityHandling_NotYetImplemented()
        {
        }

        [TestMethod]
        [Ignore("Stage 1+: assert DesignatedProducer restricts writers when hooks land.")]
        public void DesignatedProducer_AuthorityHandling_NotYetImplemented()
        {
        }

        [TestMethod]
        public void AuthorityPolicyEnumIncludesExpectedServerContractValues()
        {
            Assert.IsTrue((byte)PersistentAuthorityPolicy.ServerDerived < (byte)PersistentAuthorityPolicy.AnyClientIntent);
            Assert.IsTrue((byte)PersistentAuthorityPolicy.AnyClientIntent < (byte)PersistentAuthorityPolicy.LockOwnerIntent);
            Assert.IsTrue((byte)PersistentAuthorityPolicy.LockOwnerIntent < (byte)PersistentAuthorityPolicy.DesignatedProducer);
        }
    }
}
