using System;
using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System.PersistentSync;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class PersistentSyncAuthorityHandlingPlaceholderTests
    {
        private sealed class PolicyProbeDomain : IPersistentSyncServerDomain
        {
            public PolicyProbeDomain(PersistentAuthorityPolicy policy)
            {
                AuthorityPolicy = policy;
            }

            public PersistentAuthorityPolicy AuthorityPolicy { get; }
            public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Funds;

            public void LoadFromPersistence(bool createdFromScratch)
            {
            }

            public PersistentSyncDomainSnapshot GetCurrentSnapshot()
            {
                return new PersistentSyncDomainSnapshot
                {
                    DomainId = DomainId,
                    Revision = 0,
                    AuthorityPolicy = AuthorityPolicy,
                    Payload = new byte[0],
                    NumBytes = 0
                };
            }

            public PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data)
            {
                throw new InvalidOperationException("ApplyClientIntent must not run when authority rejects.");
            }

            public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }
        }

        [TestMethod]
        public void AnyClientIntent_ValidateClientMaySubmitIntent_AllowsClient()
        {
            var store = new FundsPersistentSyncDomainStore();
            Assert.AreEqual(PersistentAuthorityPolicy.AnyClientIntent, store.AuthorityPolicy);
            Assert.IsTrue(PersistentSyncRegistry.ValidateClientMaySubmitIntent(null, store));
        }

        [TestMethod]
        public void ServerDerived_ValidateClientMaySubmitIntent_RejectsClient()
        {
            var domain = new PolicyProbeDomain(PersistentAuthorityPolicy.ServerDerived);
            Assert.IsFalse(PersistentSyncRegistry.ValidateClientMaySubmitIntent(null, domain));
        }

        /// <summary>
        /// Registry currently stubs <see cref="PersistentAuthorityPolicy.LockOwnerIntent"/> as reject-all until lock wiring exists.
        /// </summary>
        [TestMethod]
        public void LockOwnerIntent_StubRejectsUntilLockOwnerWiringExists()
        {
            var domain = new PolicyProbeDomain(PersistentAuthorityPolicy.LockOwnerIntent);
            Assert.IsFalse(PersistentSyncRegistry.ValidateClientMaySubmitIntent(null, domain));
        }

        /// <summary>
        /// Registry currently stubs <see cref="PersistentAuthorityPolicy.DesignatedProducer"/> as reject-all until producer wiring exists.
        /// </summary>
        [TestMethod]
        public void DesignatedProducer_StubRejectsUntilProducerElectionWiringExists()
        {
            var domain = new PolicyProbeDomain(PersistentAuthorityPolicy.DesignatedProducer);
            Assert.IsFalse(PersistentSyncRegistry.ValidateClientMaySubmitIntent(null, domain));
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
