using LmpCommon.Message;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;
using Server.System.PersistentSync;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class GameLaunchIdPersistentSyncDomainStoreTests
    {
        private static readonly ClientMessageFactory ClientMessageFactory = new ClientMessageFactory();

        [TestInitialize]
        public void Setup()
        {
            PersistentSyncRegistry.CreateRegisteredDomainsForTests(typeof(IPersistentSyncServerDomain).Assembly);
            PersistentSyncRegistry.Reset();
            ScenarioStoreSystem.CurrentScenarios.Clear();
        }

        [TestMethod]
        public void ClientIntentCannotLowerCanonicalLaunchId()
        {
            ScenarioStoreSystem.CurrentScenarios["LmpGameLaunchId"] =
                new ConfigNode("name = LmpGameLaunchId\nlaunchID = 100\n");

            var store = new GameLaunchIdPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var lowPayload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<uint>(5u, "stale"));
            var lowResult = store.ApplyClientIntent(null, CreateIntent(
                PersistentSyncDomainNames.GameLaunchId,
                lowPayload,
                "stale"));

            Assert.IsTrue(lowResult.Accepted);
            Assert.IsFalse(lowResult.Changed);
            Assert.AreEqual(100u, PersistentSyncPayloadSerializer.Deserialize<uint>(lowResult.Snapshot.Payload, lowResult.Snapshot.NumBytes));

            var highPayload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<uint>(150u, "bump"));
            var highResult = store.ApplyClientIntent(null, CreateIntent(
                PersistentSyncDomainNames.GameLaunchId,
                highPayload,
                "bump"));

            Assert.IsTrue(highResult.Accepted);
            Assert.IsTrue(highResult.Changed);
            Assert.AreEqual(150u, PersistentSyncPayloadSerializer.Deserialize<uint>(highResult.Snapshot.Payload, highResult.Snapshot.NumBytes));
        }

        private static PersistentSyncIntentMsgData CreateIntent(string domainId, byte[] payload, string reason)
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
