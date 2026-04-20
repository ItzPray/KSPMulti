using System;
using System.Runtime.Serialization;
using LmpCommon.Locks;
using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Client;
using Server.System;
using Server.System.PersistentSync;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class PersistentSyncAuthorityHandlingPlaceholderTests
    {
        private sealed class PolicyProbeDomain : IPersistentSyncServerDomain
        {
            public PolicyProbeDomain(PersistentAuthorityPolicy policy, PersistentSyncDomainId domainId = PersistentSyncDomainId.Funds)
            {
                AuthorityPolicy = policy;
                DomainId = domainId;
            }

            public PersistentAuthorityPolicy AuthorityPolicy { get; }
            public PersistentSyncDomainId DomainId { get; }

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

            public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
            {
                throw new InvalidOperationException("ApplyClientIntent must not run when authority rejects.");
            }

            public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            public bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes)
            {
                return PersistentSyncRegistry.ValidateClientMaySubmitIntent(client, this);
            }
        }

        [TestInitialize]
        public void Setup()
        {
            var existingContractLock = LockSystem.LockQuery.ContractLock();
            if (existingContractLock != null)
            {
                LockSystem.ReleaseLock(existingContractLock);
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

        [TestMethod]
        public void LockOwnerIntent_ContractsAllowsCurrentContractLockOwner()
        {
            var domain = new PolicyProbeDomain(PersistentAuthorityPolicy.LockOwnerIntent, PersistentSyncDomainId.Contracts);
            var ownerClient = CreateClient("Owner");
            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, ownerClient.PlayerName), false, out _);

            Assert.IsTrue(PersistentSyncRegistry.ValidateClientMaySubmitIntent(ownerClient, domain));
        }

        [TestMethod]
        public void LockOwnerIntent_ContractsRejectsNonOwner()
        {
            var domain = new PolicyProbeDomain(PersistentAuthorityPolicy.LockOwnerIntent, PersistentSyncDomainId.Contracts);
            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "Owner"), false, out _);

            Assert.IsFalse(PersistentSyncRegistry.ValidateClientMaySubmitIntent(CreateClient("Other"), domain));
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

        private static ClientStructure CreateClient(string playerName)
        {
            var client = (ClientStructure)FormatterServices.GetUninitializedObject(typeof(ClientStructure));
            client.PlayerName = playerName;
            return client;
        }
    }
}
