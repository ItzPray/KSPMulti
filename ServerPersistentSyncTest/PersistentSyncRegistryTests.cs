using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;
using Server.System.PersistentSync;
using System.Globalization;
using System.Linq;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class PersistentSyncRegistryTests
    {
        private static readonly ClientMessageFactory ClientMessageFactory = new ClientMessageFactory();

        [TestInitialize]
        public void Setup()
        {
            PersistentSyncRegistry.Reset();
            ScenarioStoreSystem.CurrentScenarios.Clear();
        }

        [TestMethod]
        public void FundsDomainLoadsAndPersists()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "1200.5");
            var store = new FundsPersistentSyncDomainStore();

            store.LoadFromPersistence(false);
            Assert.AreEqual(1200.5d, FundsSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, sizeof(double)));

            var payload = FundsIntentPayloadSerializer.Serialize(1800.25d, "Contract");
            var result = store.ApplyClientIntent(CreateIntent(PersistentSyncDomainId.Funds, payload, "Contract"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            Assert.AreEqual(1800.25d, FundsSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes));
            Assert.AreEqual(1800.25d, double.Parse(ScenarioStoreSystem.CurrentScenarios["Funding"].GetValue("funds").Value, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void ScienceAndReputationDomainPersistenceMappings()
        {
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "42.5");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "9.5");

            var scienceStore = new SciencePersistentSyncDomainStore();
            scienceStore.LoadFromPersistence(false);
            Assert.AreEqual(42.5f, ScienceSnapshotPayloadSerializer.Deserialize(scienceStore.GetCurrentSnapshot().Payload, sizeof(float)));

            var reputationStore = new ReputationPersistentSyncDomainStore();
            reputationStore.LoadFromPersistence(false);
            Assert.AreEqual(9.5f, ReputationSnapshotPayloadSerializer.Deserialize(reputationStore.GetCurrentSnapshot().Payload, sizeof(float)));

            var sciencePayload = ScienceIntentPayloadSerializer.Serialize(77.25f, "Science lab");
            var scienceResult = scienceStore.ApplyClientIntent(CreateIntent(PersistentSyncDomainId.Science, sciencePayload, "Science lab"));

            var reputationPayload = ReputationIntentPayloadSerializer.Serialize(12.75f, "Contract");
            var reputationResult = reputationStore.ApplyClientIntent(CreateIntent(PersistentSyncDomainId.Reputation, reputationPayload, "Contract"));

            Assert.AreEqual(77.25f, float.Parse(ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].GetValue("sci").Value, CultureInfo.InvariantCulture));
            Assert.AreEqual(12.75f, float.Parse(ScenarioStoreSystem.CurrentScenarios["Reputation"].GetValue("rep").Value, CultureInfo.InvariantCulture));
            Assert.AreEqual(1L, scienceResult.Snapshot.Revision);
            Assert.AreEqual(1L, reputationResult.Snapshot.Revision);
        }

        [TestMethod]
        public void NoRevisionIncrementOnSameValueIntent()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "2500");
            var store = new FundsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var payload = FundsIntentPayloadSerializer.Serialize(2500d, "No-op");
            var result = store.ApplyClientIntent(CreateIntent(PersistentSyncDomainId.Funds, payload, "No-op"));

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.IsFalse(result.ReplyToOriginClient);
            Assert.AreEqual(0L, result.Snapshot.Revision);
        }

        [TestMethod]
        public void RegistryReturnsRequestedSnapshots()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "333");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "44");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "55");
            PersistentSyncRegistry.Initialize(false);

            var snapshots = PersistentSyncRegistry.GetSnapshots(new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Reputation
            }).ToArray();

            Assert.AreEqual(2, snapshots.Length);
            Assert.AreEqual(PersistentSyncDomainId.Funds, snapshots[0].DomainId);
            Assert.AreEqual(333d, FundsSnapshotPayloadSerializer.Deserialize(snapshots[0].Payload, snapshots[0].NumBytes));
            Assert.AreEqual(PersistentSyncDomainId.Reputation, snapshots[1].DomainId);
            Assert.AreEqual(55f, ReputationSnapshotPayloadSerializer.Deserialize(snapshots[1].Payload, snapshots[1].NumBytes));
        }

        [TestMethod]
        public void RegistryServerMutationUpdatesCanonicalState()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            PersistentSyncRegistry.Initialize(false);

            var payload = FundsIntentPayloadSerializer.Serialize(700d, "Server Command");
            var result = PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.Funds, payload, payload.Length, "Server Command");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            Assert.AreEqual(700d, FundsSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes));
            Assert.AreEqual(700d, double.Parse(ScenarioStoreSystem.CurrentScenarios["Funding"].GetValue("funds").Value, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void ApplyClientIntentWithAuthority_AnyClientIntent_AcceptsAndUpdatesCanonicalState()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            PersistentSyncRegistry.Initialize(false);

            var payload = FundsIntentPayloadSerializer.Serialize(700d, "Legit");
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(null, CreateIntent(PersistentSyncDomainId.Funds, payload, "Legit"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            Assert.AreEqual(700d, FundsSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes));
            Assert.AreEqual(700d, double.Parse(ScenarioStoreSystem.CurrentScenarios["Funding"].GetValue("funds").Value, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void ApplyClientIntentWithAuthority_ServerDerived_RejectsWithoutMutatingOrCallingDomainApply()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "333");
            PersistentSyncRegistry.Initialize(false);

            var isolatedStore = new FundsPersistentSyncDomainStore();
            isolatedStore.LoadFromPersistence(false);
            var revisionBefore = isolatedStore.GetCurrentSnapshot().Revision;
            var decorator = new ServerDerivedFundsDecorator(isolatedStore);
            PersistentSyncRegistry.ReplaceRegisteredDomainForTests(PersistentSyncDomainId.Funds, decorator);

            var payload = FundsIntentPayloadSerializer.Serialize(999d, "Denied");
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(null, CreateIntent(PersistentSyncDomainId.Funds, payload, "Denied"));

            Assert.IsFalse(result.Accepted);
            Assert.IsFalse(decorator.ClientApplyInvoked);
            Assert.AreEqual(333d, double.Parse(ScenarioStoreSystem.CurrentScenarios["Funding"].GetValue("funds").Value, CultureInfo.InvariantCulture));
            Assert.AreEqual(revisionBefore, isolatedStore.GetCurrentSnapshot().Revision);
        }

        private sealed class ServerDerivedFundsDecorator : IPersistentSyncServerDomain
        {
            private readonly FundsPersistentSyncDomainStore _inner;

            public ServerDerivedFundsDecorator(FundsPersistentSyncDomainStore inner)
            {
                _inner = inner;
            }

            public bool ClientApplyInvoked { get; private set; }

            public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.ServerDerived;

            public PersistentSyncDomainId DomainId => _inner.DomainId;

            public void LoadFromPersistence(bool createdFromScratch)
            {
            }

            public PersistentSyncDomainSnapshot GetCurrentSnapshot()
            {
                return _inner.GetCurrentSnapshot();
            }

            public PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data)
            {
                ClientApplyInvoked = true;
                return _inner.ApplyClientIntent(data);
            }

            public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
            {
                return _inner.ApplyServerMutation(payload, numBytes, reason);
            }
        }

        private static ConfigNode CreateScenario(string fieldName, string value)
        {
            return new ConfigNode($"{fieldName} = {value}");
        }

        private static PersistentSyncIntentMsgData CreateIntent(PersistentSyncDomainId domainId, byte[] payload, string reason)
        {
            var data = ClientMessageFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            data.DomainId = domainId;
            data.ClientKnownRevision = 0;
            data.Payload = payload;
            data.NumBytes = payload.Length;
            data.Reason = reason;
            return data;
        }
    }
}
