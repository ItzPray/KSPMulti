using LmpCommon.Enums;
using LmpCommon.Locks;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Client;
using Server.Settings.Structures;
using Server.System;
using Server.System.PersistentSync;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
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

            var existingContractLock = LockSystem.LockQuery.ContractLock();
            if (existingContractLock != null)
            {
                LockSystem.ReleaseLock(existingContractLock);
            }
        }

        [TestMethod]
        public void FundsDomainLoadsAndPersists()
        {
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "1200.5");
            var store = new FundsPersistentSyncDomainStore();

            store.LoadFromPersistence(false);
            Assert.AreEqual(1200.5d, FundsSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, sizeof(double)));

            var payload = FundsIntentPayloadSerializer.Serialize(1800.25d, "Contract");
            var result = store.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.Funds, payload, "Contract"));

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
            var scienceResult = scienceStore.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.Science, sciencePayload, "Science lab"));

            var reputationPayload = ReputationIntentPayloadSerializer.Serialize(12.75f, "Contract");
            var reputationResult = reputationStore.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.Reputation, reputationPayload, "Contract"));

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
            var result = store.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.Funds, payload, "No-op"));

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
        public void UpgradeableFacilitiesDomainLoadsPersistsAndReturnsSnapshot()
        {
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(
                ("SpaceCenter/MissionControl", "0.5"),
                ("SpaceCenter/TrackingStation", "0"));

            var store = new UpgradeableFacilitiesPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var initialSnapshot = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(1, initialSnapshot["SpaceCenter/MissionControl"]);
            Assert.AreEqual(0, initialSnapshot["SpaceCenter/TrackingStation"]);

            var payload = UpgradeableFacilitiesIntentPayloadSerializer.Serialize("SpaceCenter/MissionControl", 2);
            var result = store.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.UpgradeableFacilities, payload, "Mission Control upgrade"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);

            var roundTripSnapshot = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes);
            Assert.AreEqual(2, roundTripSnapshot["SpaceCenter/MissionControl"]);
            Assert.AreEqual("1", ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"].GetNode("SpaceCenter/MissionControl").Value.GetValue("lvl").Value);

            PersistentSyncRegistry.Initialize(false);
            var registrySnapshot = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.UpgradeableFacilities }).Single();
            var registryFacilities = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(registrySnapshot.Payload, registrySnapshot.NumBytes);
            Assert.AreEqual(2, registryFacilities["SpaceCenter/MissionControl"]);
        }

        [TestMethod]
        public void UpgradeableFacilitiesDomainLoadsNestedKspScenarioLayout()
        {
            var nestedText = string.Join("\n", new[]
            {
                "name = ScenarioUpgradeableFacilities",
                "scene = 5, 6, 7, 8",
                "SpaceCenter",
                "{",
                "    LaunchPad",
                "    {",
                "        lvl = 0.5",
                "    }",
                "    MissionControl",
                "    {",
                "        lvl = 0",
                "    }",
                "}"
            });
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = new ConfigNode(nestedText);

            var store = new UpgradeableFacilitiesPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var snap = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(
                store.GetCurrentSnapshot().Payload,
                store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(1, snap["SpaceCenter/LaunchPad"]);
            Assert.AreEqual(0, snap["SpaceCenter/MissionControl"]);
        }

        [TestMethod]
        public void UpgradeableFacilitiesNoRevisionIncrementOnSameStateIntent()
        {
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(
                ("SpaceCenter/MissionControl", "0.5"));

            var store = new UpgradeableFacilitiesPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var payload = UpgradeableFacilitiesIntentPayloadSerializer.Serialize("SpaceCenter/MissionControl", 1);
            var result = store.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.UpgradeableFacilities, payload, "No-op"));

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0L, result.Snapshot.Revision);
            Assert.AreEqual("0.5", ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"].GetNode("SpaceCenter/MissionControl").Value.GetValue("lvl").Value);
        }

        [TestMethod]
        public void UpgradeableFacilitiesIntentCannotDowngradeBelowPersistedLevel()
        {
            // lvl float 1 -> DeserializePersistentLevel => 2 (see UpgradeableFacilitiesPersistentSyncDomainStore)
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(
                ("SpaceCenter/MissionControl", "1"));

            var store = new UpgradeableFacilitiesPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var before = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(
                store.GetCurrentSnapshot().Payload,
                store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(2, before["SpaceCenter/MissionControl"]);

            var downgradePayload = UpgradeableFacilitiesIntentPayloadSerializer.Serialize("SpaceCenter/MissionControl", 0);
            var result = store.ApplyClientIntent(null, CreateIntent(PersistentSyncDomainId.UpgradeableFacilities, downgradePayload, "Spurious KSC init"));

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0L, result.Snapshot.Revision);

            var after = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes);
            Assert.AreEqual(2, after["SpaceCenter/MissionControl"]);
            Assert.AreEqual("1", ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"].GetNode("SpaceCenter/MissionControl").Value.GetValue("lvl").Value);
        }

        [TestMethod]
        public void ContractsDomainLoadsPersistsSemanticChangesAndReturnsSnapshot()
        {
            var contractA = CreateContractSnapshotInfo(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "10,0,1,1,0");
            var contractB = CreateContractSnapshotInfo(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Completed",
                ContractSnapshotPlacement.Finished,
                1,
                "Complete",
                "20,0,2,2,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(contractA, contractB);
            var store = new ContractsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var initialSnapshot = ContractSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(2, initialSnapshot.Count);
            Assert.AreEqual(contractA.ContractGuid, initialSnapshot[0].ContractGuid);
            Assert.AreEqual("Offered", initialSnapshot[0].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Finished, initialSnapshot[1].Placement);

            var changedContractA = CreateContractSnapshotInfo(
                contractA.ContractGuid,
                "Offered",
                ContractSnapshotPlacement.Current,
                -1,
                "Complete",
                "10,1,1,1,0");
            var mutationPayload = ContractSnapshotPayloadSerializer.Serialize(new[] { changedContractA });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Parameter progress");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);

            var persistedScenarioText = ScenarioStoreSystem.CurrentScenarios["ContractSystem"].ToString();
            StringAssert.Contains(persistedScenarioText, contractA.ContractGuid.ToString());
            StringAssert.Contains(persistedScenarioText, contractB.ContractGuid.ToString());
            StringAssert.Contains(persistedScenarioText, "state = Complete");
            StringAssert.Contains(persistedScenarioText, "values = 10,1,1,1,0");

            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);
            PersistentSyncRegistry.ReplaceRegisteredDomainForTests(PersistentSyncDomainId.Contracts, store);
            var registrySnapshot = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();
            var registryContracts = ContractSnapshotPayloadSerializer.Deserialize(registrySnapshot.Payload, registrySnapshot.NumBytes);
            Assert.AreEqual(2, registryContracts.Count);
            var registryContract = registryContracts.Single(c => c.ContractGuid == contractA.ContractGuid);
            Assert.AreEqual("Offered", registryContract.ContractState);
            var registryNode = new ConfigNode(Encoding.UTF8.GetString(registryContract.Data, 0, registryContract.NumBytes));
            Assert.AreEqual("Complete", registryNode.GetNode("PARAM").Value.GetValue("state").Value);
        }

        [TestMethod]
        public void ContractsDomainSeedsStarterOffersWhenPersistenceHasNoReadableContracts_CareerOnly()
        {
            var previousMode = GeneralSettings.SettingsStore.GameMode;
            try
            {
                GeneralSettings.SettingsStore.GameMode = GameMode.Career;

                var emptyScenario = new ConfigNode(@"name = ContractSystem
scene = 7, 8, 5, 6
CONTRACTS
{
}");
                ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = emptyScenario;
                var store = new ContractsPersistentSyncDomainStore();
                store.LoadFromPersistence(false);

                var snapshot = ContractSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);
                Assert.IsTrue(snapshot.Count > 0, "Career server with empty CONTRACTS should seed from embedded template.");
            }
            finally
            {
                GeneralSettings.SettingsStore.GameMode = previousMode;
            }
        }

        [TestMethod]
        public void ContractsDomainLoadFromPersistenceCanonicalizesDuplicateOfferedContracts()
        {
            var firstDuplicate = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("66666666-6666-6666-6666-666666666661"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "SurveyContract",
                "Conduct a focused observational survey of Kerbin.");
            var secondDuplicate = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("66666666-6666-6666-6666-666666666662"),
                "Offered",
                ContractSnapshotPlacement.Current,
                1,
                "SurveyContract",
                "Conduct a focused observational survey of Kerbin.");
            var uniqueOffer = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("66666666-6666-6666-6666-666666666663"),
                "Offered",
                ContractSnapshotPlacement.Current,
                2,
                "PartTest",
                "Test TD-12 Decoupler at the Launch Site.");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(firstDuplicate, secondDuplicate, uniqueOffer);
            var store = new ContractsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var snapshot = ContractSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(2, snapshot.Count, "Duplicate offered rows should be collapsed during startup load.");
            Assert.IsTrue(snapshot.Any(c => c.ContractGuid == secondDuplicate.ContractGuid), "The latest duplicate should be retained.");
            Assert.IsFalse(snapshot.Any(c => c.ContractGuid == firstDuplicate.ContractGuid), "Older duplicate rows should be removed from the canonical snapshot.");
            Assert.IsTrue(snapshot.Any(c => c.ContractGuid == uniqueOffer.ContractGuid));

            var persistedScenarioText = ScenarioStoreSystem.CurrentScenarios["ContractSystem"].ToString();
            StringAssert.Contains(persistedScenarioText, secondDuplicate.ContractGuid.ToString());
            StringAssert.Contains(persistedScenarioText, uniqueOffer.ContractGuid.ToString());
            Assert.IsFalse(persistedScenarioText.Contains(firstDuplicate.ContractGuid.ToString()), "Persistence should be rewritten without the removed duplicate row.");
            Assert.AreEqual(0L, store.GetCurrentSnapshot().Revision, "Startup cleanup should not create a live revision bump.");
        }

        [TestMethod]
        public void ContractsDomainFullReplacePrunesStaleOfferedContracts()
        {
            var staleOffer = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("77777777-7777-7777-7777-777777777771"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "SurveyContract",
                "Conduct a focused observational survey of Kerbin.");
            var currentActive = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("77777777-7777-7777-7777-777777777772"),
                "Active",
                ContractSnapshotPlacement.Active,
                1,
                "ExplorationContract",
                "Launch our first vessel!");
            var currentFinished = CreateContractSnapshotInfoWithTitle(
                Guid.Parse("77777777-7777-7777-7777-777777777773"),
                "Completed",
                ContractSnapshotPlacement.Finished,
                2,
                "ExplorationContract",
                "Orbit Kerbin!");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(staleOffer, currentActive, currentFinished);
            var store = new ContractsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var fullReplacePayload = ContractSnapshotPayloadSerializer.Serialize(
                ContractSnapshotPayloadMode.FullReplace,
                new[] { currentActive, currentFinished });
            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ContractOwner"), false, out _);
            var result = store.ApplyClientIntent(CreateClient("ContractOwner"), CreateIntent(PersistentSyncDomainId.Contracts, fullReplacePayload, "ContractInventoryFull:Test"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);

            var snapshot = ContractSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes);
            Assert.AreEqual(2, snapshot.Count);
            Assert.IsFalse(snapshot.Any(c => c.ContractGuid == staleOffer.ContractGuid), "Full replace should prune missing stale offers.");
            Assert.IsTrue(snapshot.Any(c => c.ContractGuid == currentActive.ContractGuid));
            Assert.IsTrue(snapshot.Any(c => c.ContractGuid == currentFinished.ContractGuid));
        }

        [TestMethod]
        [Ignore("Pending follow-up: normalize contract no-op equivalence beyond payload formatting.")]
        public void ContractsDomainNoRevisionIncrementOnEquivalentMutation()
        {
            var contract = CreateContractSnapshotInfo(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "50,0,3,3,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(contract);
            var store = new ContractsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var mutationPayload = ContractSnapshotPayloadSerializer.Serialize(new[] { CreateContractSnapshotInfo(contract.ContractGuid, "Offered", ContractSnapshotPlacement.Current, -1, "Incomplete", "50,0,3,3,0") });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "No-op");

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0L, result.Snapshot.Revision);
        }

        [TestMethod]
        public void TechnologyDomainLoadsPersistsAndReturnsSnapshot()
        {
            var basicRocketry = CreateTechnologySnapshotInfo("basicRocketry", "Available", 5, "liquidEngine");
            var engineering101 = CreateTechnologySnapshotInfo("engineering101", "Available", 15, "radialDecoupler", "stackSeparator");

            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateResearchAndDevelopmentScenario(basicRocketry, engineering101);
            var store = new TechnologyPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var initialSnapshot = TechnologySnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);
            Assert.AreEqual(2, initialSnapshot.Count);
            Assert.AreEqual("basicRocketry", initialSnapshot[0].TechId);

            var advRocketry = CreateTechnologySnapshotInfo("advRocketry", "Available", 45, "advLiquidEngine");
            var mutationPayload = TechnologySnapshotPayloadSerializer.Serialize(new[] { advRocketry });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Unlock advRocketry");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);

            var persistedScenarioText = ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].ToString();
            StringAssert.Contains(persistedScenarioText, "id = basicRocketry");
            StringAssert.Contains(persistedScenarioText, "id = engineering101");
            StringAssert.Contains(persistedScenarioText, "id = advRocketry");

            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario();
            PersistentSyncRegistry.Initialize(false);
            PersistentSyncRegistry.ReplaceRegisteredDomainForTests(PersistentSyncDomainId.Technology, store);

            var registrySnapshot = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Technology }).Single();
            var registryTechnologies = TechnologySnapshotPayloadSerializer.Deserialize(registrySnapshot.Payload, registrySnapshot.NumBytes);
            Assert.AreEqual(3, registryTechnologies.Count);
            Assert.IsTrue(registryTechnologies.Any(technology => technology.TechId == "advRocketry"));
        }

        [TestMethod]
        public void TechnologyDomainLoadsRealWorldScenarioWithMixedNodesAndSci()
        {
            var scenarioText = @"name = ResearchAndDevelopment
scene = 7, 8, 5, 6
sci = 85.96483
Tech
{
    id = start
    state = Available
    cost = 0
    part = basicFin
    part = mk1pod
}
Science
{
    id = crewReport@KerbinSrfLandedLaunchPad
    title = Crew Report from LaunchPad
    sci = 1.5
}
Tech
{
    id = basicRocketry
    state = Available
    cost = 5
    part = fuelTankSmallFlat
}
Tech
{
    id = engineering101
    state = Available
    cost = 5
    part = SurfAntenna
}
Tech
{
    id = generalRocketry
    state = Available
    cost = 20
}
";
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = new ConfigNode(scenarioText);
            var store = new TechnologyPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var snapshot = TechnologySnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload, store.GetCurrentSnapshot().NumBytes);

            var techIds = snapshot.Select(t => t.TechId).OrderBy(id => id).ToArray();
            CollectionAssert.AreEqual(new[] { "basicRocketry", "engineering101", "generalRocketry", "start" }, techIds);
            foreach (var info in snapshot)
            {
                var node = new ConfigNode(Encoding.UTF8.GetString(info.Data, 0, info.NumBytes));
                Assert.AreEqual("Available", node.GetValue("state")?.Value, $"tech {info.TechId} state");
                Assert.IsFalse(string.IsNullOrEmpty(node.GetValue("cost")?.Value), $"tech {info.TechId} cost missing");
            }
        }

        [TestMethod]
        public void TechnologyDomainNoRevisionIncrementOnEquivalentMutation()
        {
            var basicRocketry = CreateTechnologySnapshotInfo("basicRocketry", "Available", 5, "liquidEngine");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateResearchAndDevelopmentScenario(basicRocketry);
            var store = new TechnologyPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var mutationPayload = TechnologySnapshotPayloadSerializer.Serialize(new[] { CreateTechnologySnapshotInfo("basicRocketry", "Available", 5, "liquidEngine") });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "No-op");

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0L, result.Snapshot.Revision);
        }

        [TestMethod]
        public void TechnologyDomainPersistPreservesPurchasedPartLinesOnTechNodes()
        {
            var basicRocketry = CreateTechnologySnapshotInfo("basicRocketry", "Available", 5, "liquidEngine");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateResearchAndDevelopmentScenario(basicRocketry);
            var store = new TechnologyPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var costUpdate = CreateTechnologySnapshotInfo("basicRocketry", "Available", 9, "liquidEngine");
            var mutationPayload = TechnologySnapshotPayloadSerializer.Serialize(new[] { costUpdate });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Update cost only");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);

            var persisted = ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].ToString();
            StringAssert.Contains(persisted, "part = liquidEngine");
            StringAssert.Contains(persisted, "cost = 9");
        }

        [TestMethod]
        public void StrategyDomainLoadsPersistsAndReturnsSnapshot()
        {
            ScenarioStoreSystem.CurrentScenarios["StrategySystem"] = CreateStrategyScenario(
                CreateStrategySnapshotInfo("BailoutGrant", 0.25f, true),
                CreateStrategySnapshotInfo("recovery", 0.5f, false));

            var store = new StrategyPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var initialSnapshot = StrategySnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload);
            Assert.AreEqual(2, initialSnapshot.Length);

            var mutationPayload = StrategySnapshotPayloadSerializer.Serialize(new[] { CreateStrategySnapshotInfo("recovery", 0.75f, true) });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Activate recovery");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            StringAssert.Contains(ScenarioStoreSystem.CurrentScenarios["StrategySystem"].ToString(), "factor = 0.75");
            StringAssert.Contains(ScenarioStoreSystem.CurrentScenarios["StrategySystem"].ToString(), "isActive = True");
        }

        [TestMethod]
        public void AchievementsDomainLoadsPersistsAndReturnsSnapshot()
        {
            ScenarioStoreSystem.CurrentScenarios["ProgressTracking"] = CreateAchievementsScenario(
                CreateAchievementSnapshotInfo("Kerbin", "Complete"),
                CreateAchievementSnapshotInfo("Mun", "Incomplete"));

            var store = new AchievementsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var mutationPayload = AchievementSnapshotPayloadSerializer.Serialize(new[] { CreateAchievementSnapshotInfo("Duna", "Complete") });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Reach Duna");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            var scenarioText = ScenarioStoreSystem.CurrentScenarios["ProgressTracking"].ToString();
            StringAssert.Contains(scenarioText, "Kerbin");
            StringAssert.Contains(scenarioText, "Mun");
            StringAssert.Contains(scenarioText, "Duna");
        }

        [TestMethod]
        public void ScienceSubjectsDomainLoadsPersistsAndReturnsSnapshot()
        {
            var basicRocketry = CreateTechnologySnapshotInfo("basicRocketry", "Available", 5);
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateResearchAndDevelopmentScenarioWithScience(
                new[] { basicRocketry },
                new[] { CreateScienceSubjectSnapshotInfo("crewReport@KerbinSrfLandedLaunchPad", 1f, 5f) });

            var store = new ScienceSubjectsPersistentSyncDomainStore();
            store.LoadFromPersistence(false);

            var initialSnapshot = ScienceSubjectSnapshotPayloadSerializer.Deserialize(store.GetCurrentSnapshot().Payload);
            Assert.AreEqual(1, initialSnapshot.Length);

            var mutationPayload = ScienceSubjectSnapshotPayloadSerializer.Serialize(new[] { CreateScienceSubjectSnapshotInfo("evaReport@MunInSpaceHigh", 2f, 8f) });
            var result = store.ApplyServerMutation(mutationPayload, mutationPayload.Length, "Mun EVA");

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);
            StringAssert.Contains(ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].ToString(), "evaReport@MunInSpaceHigh");
        }

        [TestMethod]
        public void ExperimentalPartsAndPartPurchasesDomainsPersistSeparateRDOwnership()
        {
            var basicRocketry = CreateTechnologySnapshotInfo("basicRocketry", "Available", 5);
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateResearchAndDevelopmentScenarioWithScience(new[] { basicRocketry }, Array.Empty<ScienceSubjectSnapshotInfo>());
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].AddNode(new ConfigNode("ExpParts\n{\n liquidEngine = 1\n}\n"));
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].GetNode("Tech").Value.CreateValue(new CfgNodeValue<string, string>("part", "liquidEngine"));

            var experimentalStore = new ExperimentalPartsPersistentSyncDomainStore();
            experimentalStore.LoadFromPersistence(false);
            var partPurchasesStore = new PartPurchasesPersistentSyncDomainStore();
            partPurchasesStore.LoadFromPersistence(false);

            var experimentalPayload = ExperimentalPartsSnapshotPayloadSerializer.Serialize(new[] { new ExperimentalPartSnapshotInfo { PartName = "liquidEngine", Count = 2 } });
            var experimentalResult = experimentalStore.ApplyServerMutation(experimentalPayload, experimentalPayload.Length, "More stock");
            Assert.IsTrue(experimentalResult.Accepted);
            Assert.IsTrue(experimentalResult.Changed);

            var purchasePayload = PartPurchasesSnapshotPayloadSerializer.Serialize(new[]
            {
                new PartPurchaseSnapshotInfo
                {
                    TechId = "basicRocketry",
                    PartNames = new[] { "liquidEngine", "solidBooster" }
                }
            });
            var purchaseResult = partPurchasesStore.ApplyServerMutation(purchasePayload, purchasePayload.Length, "Buy part");
            Assert.IsTrue(purchaseResult.Accepted);
            Assert.IsTrue(purchaseResult.Changed);

            var scenarioText = ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"].ToString();
            StringAssert.Contains(scenarioText, "ExpParts");
            StringAssert.Contains(scenarioText, "liquidEngine = 2");
            StringAssert.Contains(scenarioText, "part = liquidEngine");
            StringAssert.Contains(scenarioText, "part = solidBooster");
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

        [TestMethod]
        public void ApplyClientIntentWithAuthority_ContractsProducerProposalOwner_AcceptsAndMutatesCanonicalState()
        {
            var existingContract = CreateContractSnapshotInfo(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "Active",
                ContractSnapshotPlacement.Active,
                0,
                "Incomplete",
                "1,0,0,0,0");
            var changedContract = CreateContractSnapshotInfo(
                existingContract.ContractGuid,
                "Active",
                ContractSnapshotPlacement.Active,
                -1,
                "Complete",
                "1,1,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(existingContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            var ownerClient = CreateClient("ContractOwner");
            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, ownerClient.PlayerName), false, out _);

            var payload = ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ParameterProgressObserved, changedContract);
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(ownerClient, CreateIntent(PersistentSyncDomainId.Contracts, payload, "Parameter progress"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1L, result.Snapshot.Revision);

            var updatedContracts = ContractSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes);
            var updatedContract = updatedContracts.Single(c => c.ContractGuid == existingContract.ContractGuid);
            var updatedNode = new ConfigNode(Encoding.UTF8.GetString(updatedContract.Data, 0, updatedContract.NumBytes));
            Assert.AreEqual("Complete", updatedNode.GetNode("PARAM").Value.GetValue("state").Value);
        }

        [TestMethod]
        public void ApplyClientIntentWithAuthority_ContractsAcceptCommandNonOwner_AcceptsAndTransitionsCanonicalState()
        {
            var existingContract = CreateContractSnapshotInfo(
                Guid.Parse("45454545-4545-4545-4545-454545454545"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "3,0,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(existingContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ContractOwner"), false, out _);

            var payload = ContractIntentPayloadSerializer.SerializeCommand(
                ContractIntentPayloadKind.AcceptContract,
                existingContract.ContractGuid);
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("OtherClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, payload, "Accept command"));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);

            var updatedContracts = ContractSnapshotPayloadSerializer.Deserialize(result.Snapshot.Payload, result.Snapshot.NumBytes);
            var updatedContract = updatedContracts.Single(c => c.ContractGuid == existingContract.ContractGuid);
            Assert.AreEqual("Active", updatedContract.ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Active, updatedContract.Placement);
            var contractText = Encoding.UTF8.GetString(updatedContract.Data, 0, updatedContract.NumBytes);
            StringAssert.Contains(contractText, "state = Active");
        }

        [TestMethod]
        public void ApplyClientIntentWithAuthority_ContractsProducerProposalNonOwner_RejectsWithoutMutation()
        {
            var existingContract = CreateContractSnapshotInfo(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "2,0,0,0,0");
            var changedContract = CreateContractSnapshotInfo(
                existingContract.ContractGuid,
                "Offered",
                ContractSnapshotPlacement.Current,
                -1,
                "Complete",
                "2,1,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(existingContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ContractOwner"), false, out _);
            var domainSnapshotBefore = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();

            var payload = ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ParameterProgressObserved, changedContract);
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(CreateClient("OtherClient"), CreateIntent(PersistentSyncDomainId.Contracts, payload, "Denied progress"));

            Assert.IsFalse(result.Accepted);

            var domainSnapshotAfter = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();
            Assert.AreEqual(domainSnapshotBefore.Revision, domainSnapshotAfter.Revision);

            var contractsAfter = ContractSnapshotPayloadSerializer.Deserialize(domainSnapshotAfter.Payload, domainSnapshotAfter.NumBytes);
            var contractAfter = contractsAfter.Single(c => c.ContractGuid == existingContract.ContractGuid);
            var contractText = Encoding.UTF8.GetString(contractAfter.Data, 0, contractAfter.NumBytes);
            StringAssert.Contains(contractText, "state = Incomplete");
            StringAssert.Contains(contractText, "values = 2,0,0,0,0");
        }

        [TestMethod]
        public void Gate_RequestOfferGeneration_RoutesSnapshotToProducerWhenOfferPoolEmpty()
        {
            // Gate: non-producer issues RequestOfferGeneration against canonical state with no offer-pool rows.
            // Server must not mutate revision (signal-only) and must mark ReplyToProducerClient so the registry
            // routes the canonical snapshot to the current contract lock owner.
            var activeContract = CreateContractSnapshotInfo(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa01"),
                "Active",
                ContractSnapshotPlacement.Active,
                0,
                "Incomplete",
                "1,0,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(activeContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ProducerClient"), false, out _);

            var requestPayload = ContractCommandIntent.RequestOfferGeneration().Serialize();
            var revisionBefore = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single().Revision;
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("RequestorClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, requestPayload, "RequestOfferGeneration"));
            var revisionAfter = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single().Revision;

            Assert.IsTrue(result.Accepted, "RequestOfferGeneration must be accepted as a producer-routing signal.");
            Assert.IsFalse(result.Changed, "RequestOfferGeneration must not mutate canonical state on the server.");
            Assert.IsTrue(result.ReplyToProducerClient, "Empty offer pool must route the snapshot to the producer client.");
            Assert.AreEqual(revisionBefore, revisionAfter, "Revision must not advance for a routing-only signal.");
        }

        [TestMethod]
        public void Gate_RequestOfferGeneration_DoesNotRouteToProducerWhenOffersExist()
        {
            // Gate: when canonical offer pool is non-empty, RequestOfferGeneration must not re-route to the producer
            // (producer-mint pass is unnecessary; convergence already happened).
            var offeredContract = CreateContractSnapshotInfo(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa02"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "1,0,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(offeredContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ProducerClient"), false, out _);

            var requestPayload = ContractCommandIntent.RequestOfferGeneration().Serialize();
            var result = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("RequestorClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, requestPayload, "RequestOfferGeneration"));

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.IsFalse(result.ReplyToProducerClient, "Non-empty offer pool must not trigger producer-directed signal.");
        }

        [TestMethod]
        public void Gate_MissedUpdateRecovery_SnapshotReflectsCanonicalStateAfterMultipleIntents()
        {
            // Gate: a client that missed intermediate intents (did not process intent N-1) should receive a canonical
            // snapshot that reflects the final state after intent N. We simulate this by applying Accept + parameter
            // progress in sequence and confirming the single snapshot we'd ship to a reconnecting client contains
            // both the state transition and the latest parameter progress.
            var offeredContract = CreateContractSnapshotInfo(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "1,0,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(offeredContract);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            var producer = CreateClient("ProducerClient");
            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, producer.PlayerName), false, out _);

            var acceptResult = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("OtherClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, ContractCommandIntent.Accept(offeredContract.ContractGuid).Serialize(), "Accept"));
            Assert.IsTrue(acceptResult.Accepted);
            Assert.IsTrue(acceptResult.Changed);

            // Parameter progress proposal emitted by producer (post-Accept). Simulates a later canonical mutation
            // that a reconnecting client would have missed had it been offline during Accept.
            var progressedContract = CreateContractSnapshotInfo(
                offeredContract.ContractGuid,
                "Active",
                ContractSnapshotPlacement.Active,
                -1,
                "Complete",
                "1,1,0,0,0");
            var progressResult = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                producer,
                CreateIntent(PersistentSyncDomainId.Contracts, ContractProducerProposal.ParameterProgressObserved(progressedContract).Serialize(), "ParamProgress"));
            Assert.IsTrue(progressResult.Accepted);
            Assert.IsTrue(progressResult.Changed);

            // Reconnecting-client simulation: fetch snapshot directly. Both canonical transitions must be present.
            var snapshotForReconnectingClient = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();
            var canonicalContracts = ContractSnapshotPayloadSerializer.Deserialize(snapshotForReconnectingClient.Payload, snapshotForReconnectingClient.NumBytes);
            var canonicalContract = canonicalContracts.Single(c => c.ContractGuid == offeredContract.ContractGuid);

            Assert.AreEqual("Active", canonicalContract.ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Active, canonicalContract.Placement);
            var contractText = Encoding.UTF8.GetString(canonicalContract.Data, 0, canonicalContract.NumBytes);
            StringAssert.Contains(contractText, "state = Active");
            StringAssert.Contains(contractText, "state = Complete"); // parameter state
            StringAssert.Contains(contractText, "values = 1,1,0,0,0");
        }

        [TestMethod]
        public void Gate_ServerRestart_LoadsSameCanonicalStateAsBeforeShutdown()
        {
            // Gate: after any sequence of accepted intents, a cold reload of the domain from the persisted scenario
            // must reproduce the canonical byte-level snapshot (bit-for-bit identical contracts).
            var offeredContract = CreateContractSnapshotInfo(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc01"),
                "Offered",
                ContractSnapshotPlacement.Current,
                0,
                "Incomplete",
                "1,0,0,0,0");
            var otherOffer = CreateContractSnapshotInfo(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc02"),
                "Offered",
                ContractSnapshotPlacement.Current,
                1,
                "Incomplete",
                "2,0,0,0,0");

            ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateContractSystemScenario(offeredContract, otherOffer);
            ScenarioStoreSystem.CurrentScenarios["Funding"] = CreateScenario("funds", "100");
            ScenarioStoreSystem.CurrentScenarios["ResearchAndDevelopment"] = CreateScenario("sci", "1");
            ScenarioStoreSystem.CurrentScenarios["Reputation"] = CreateScenario("rep", "1");
            ScenarioStoreSystem.CurrentScenarios["ScenarioUpgradeableFacilities"] = CreateUpgradeableFacilitiesScenario(("SpaceCenter/MissionControl", "0"));
            PersistentSyncRegistry.Initialize(false);

            LockSystem.AcquireLock(new LockDefinition(LockType.Contract, "ProducerClient"), false, out _);
            var acceptResult = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("AnyClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, ContractCommandIntent.Accept(offeredContract.ContractGuid).Serialize(), "Accept"));
            var declineResult = PersistentSyncRegistry.ApplyClientIntentWithAuthority(
                CreateClient("AnyClient"),
                CreateIntent(PersistentSyncDomainId.Contracts, ContractCommandIntent.Decline(otherOffer.ContractGuid).Serialize(), "Decline"));
            Assert.IsTrue(acceptResult.Accepted && acceptResult.Changed);
            Assert.IsTrue(declineResult.Accepted && declineResult.Changed);

            var liveSnapshot = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();
            var liveContracts = ContractSnapshotPayloadSerializer.Deserialize(liveSnapshot.Payload, liveSnapshot.NumBytes)
                .OrderBy(c => c.ContractGuid)
                .ToList();

            // Simulate server restart: drop the in-memory registry but retain ScenarioStoreSystem (disk-backed).
            PersistentSyncRegistry.Reset();
            PersistentSyncRegistry.Initialize(false);

            var reloadedSnapshot = PersistentSyncRegistry.GetSnapshots(new[] { PersistentSyncDomainId.Contracts }).Single();
            var reloadedContracts = ContractSnapshotPayloadSerializer.Deserialize(reloadedSnapshot.Payload, reloadedSnapshot.NumBytes)
                .OrderBy(c => c.ContractGuid)
                .ToList();

            Assert.AreEqual(liveContracts.Count, reloadedContracts.Count,
                "Post-restart canonical contract count must match pre-restart canonical count.");
            for (var i = 0; i < liveContracts.Count; i++)
            {
                Assert.AreEqual(liveContracts[i].ContractGuid, reloadedContracts[i].ContractGuid);
                Assert.AreEqual(liveContracts[i].ContractState, reloadedContracts[i].ContractState);
                Assert.AreEqual(liveContracts[i].Placement, reloadedContracts[i].Placement);
                var before = Encoding.UTF8.GetString(liveContracts[i].Data, 0, liveContracts[i].NumBytes);
                var after = Encoding.UTF8.GetString(reloadedContracts[i].Data, 0, reloadedContracts[i].NumBytes);
                Assert.AreEqual(before, after,
                    $"Contract {liveContracts[i].ContractGuid} body must be byte-identical across restart.");
            }
        }

        [TestMethod]
        public void Gate_TypedFacades_ProduceWireCompatiblePayloads()
        {
            // Gate: the public CLR facades ContractCommandIntent / ContractProducerProposal are a functional wrapper
            // around the intent payload serializer and must produce byte-identical wire payloads.
            var guid = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
            var facadeAccept = ContractCommandIntent.Accept(guid).Serialize();
            var directAccept = ContractIntentPayloadSerializer.SerializeCommand(ContractIntentPayloadKind.AcceptContract, guid);
            CollectionAssert.AreEqual(directAccept, facadeAccept);

            var facadeRequest = ContractCommandIntent.RequestOfferGeneration().Serialize();
            var directRequest = ContractIntentPayloadSerializer.SerializeRequestOfferGeneration();
            CollectionAssert.AreEqual(directRequest, facadeRequest);

            var contract = CreateContractSnapshotInfo(
                guid,
                "Active",
                ContractSnapshotPlacement.Active,
                0,
                "Complete",
                "1,1,0,0,0");
            var facadeProposal = ContractProducerProposal.ParameterProgressObserved(contract).Serialize();
            var directProposal = ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ParameterProgressObserved, contract);
            CollectionAssert.AreEqual(directProposal, facadeProposal);

            var facadeReconcile = ContractProducerProposal.FullReconcile(new[] { contract }).Serialize();
            var directReconcile = ContractIntentPayloadSerializer.SerializeFullReconcile(new[] { contract });
            CollectionAssert.AreEqual(directReconcile, facadeReconcile);
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

            public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
            {
                ClientApplyInvoked = true;
                return _inner.ApplyClientIntent(client, data);
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

        private static ConfigNode CreateUpgradeableFacilitiesScenario(params (string facilityId, string normalizedLevel)[] facilities)
        {
            var lines = new[]
            {
                "name = ScenarioUpgradeableFacilities",
                "scene = 5, 6, 7, 8"
            }.ToList();

            foreach (var facility in facilities)
            {
                lines.Add(facility.facilityId);
                lines.Add("{");
                lines.Add($"    lvl = {facility.normalizedLevel}");
                lines.Add("}");
            }

            return new ConfigNode(string.Join("\n", lines));
        }

        private static ConfigNode CreateContractSystemScenario(params ContractSnapshotInfo[] contracts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("name = ContractSystem");
            builder.AppendLine("scene = 7, 8, 5, 6");
            builder.AppendLine("CONTRACTS");
            builder.AppendLine("{");

            foreach (var contract in contracts.OrderBy(c => c.Order))
            {
                builder.AppendLine("CONTRACT");
                builder.AppendLine("{");
                builder.Append(IndentBlock(Encoding.UTF8.GetString(contract.Data, 0, contract.NumBytes), "    "));
                builder.AppendLine("}");
            }

            builder.AppendLine("}");
            return new ConfigNode(builder.ToString());
        }

        private static ConfigNode CreateResearchAndDevelopmentScenario(params TechnologySnapshotInfo[] technologies)
        {
            var builder = new StringBuilder();
            builder.AppendLine("name = ResearchAndDevelopment");
            builder.AppendLine("scene = 5, 6, 7, 8");

            foreach (var technology in technologies.OrderBy(t => t.TechId))
            {
                builder.AppendLine("Tech");
                builder.AppendLine("{");
                builder.Append(IndentBlock(Encoding.UTF8.GetString(technology.Data, 0, technology.NumBytes), "    "));
                builder.AppendLine("}");
            }

            return new ConfigNode(builder.ToString());
        }

        private static ConfigNode CreateResearchAndDevelopmentScenarioWithScience(IEnumerable<TechnologySnapshotInfo> technologies, IEnumerable<ScienceSubjectSnapshotInfo> subjects)
        {
            var builder = new StringBuilder();
            builder.AppendLine("name = ResearchAndDevelopment");
            builder.AppendLine("scene = 5, 6, 7, 8");

            foreach (var technology in (technologies ?? Array.Empty<TechnologySnapshotInfo>()).OrderBy(t => t.TechId))
            {
                builder.AppendLine("Tech");
                builder.AppendLine("{");
                builder.Append(IndentBlock(Encoding.UTF8.GetString(technology.Data, 0, technology.NumBytes), "    "));
                builder.AppendLine("}");
            }

            foreach (var subject in (subjects ?? Array.Empty<ScienceSubjectSnapshotInfo>()).OrderBy(s => s.Id))
            {
                builder.AppendLine("Science");
                builder.AppendLine("{");
                builder.Append(IndentBlock(Encoding.UTF8.GetString(subject.Data, 0, subject.NumBytes), "    "));
                builder.AppendLine("}");
            }

            return new ConfigNode(builder.ToString());
        }

        private static ContractSnapshotInfo CreateContractSnapshotInfo(Guid contractGuid, string state, ContractSnapshotPlacement placement, int order, string parameterState, string parameterValues)
        {
            var serializedContract = $@"guid = {contractGuid}
type = ExplorationContract
prestige = 0
seed = 1
state = {state}
viewed = Unseen
agent = Kerbin World-Firsts Record-Keeping Society
PARAM
{{
    name = ProgressTrackingParameter
    state = {parameterState}
    values = {parameterValues}
    targetBody = 1
    targetType = FIRSTLAUNCH
}}
";

            var data = Encoding.UTF8.GetBytes(serializedContract);
            return new ContractSnapshotInfo
            {
                ContractGuid = contractGuid,
                ContractState = state,
                Placement = placement,
                Order = order,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static ContractSnapshotInfo CreateContractSnapshotInfoWithTitle(Guid contractGuid, string state, ContractSnapshotPlacement placement, int order, string type, string title)
        {
            var serializedContract = $@"guid = {contractGuid}
type = {type}
state = {state}
title = {title}
lmpOfferTitle = {title}
";

            var data = Encoding.UTF8.GetBytes(serializedContract);
            return new ContractSnapshotInfo
            {
                ContractGuid = contractGuid,
                ContractState = state,
                Placement = placement,
                Order = order,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static TechnologySnapshotInfo CreateTechnologySnapshotInfo(string techId, string state, int scienceCost, params string[] partsPurchased)
        {
            var lines = new[]
            {
                $"id = {techId}",
                $"state = {state}",
                $"cost = {scienceCost}"
            }.Concat((partsPurchased ?? Array.Empty<string>()).Select(part => $"part = {part}"));

            var serializedTech = string.Join("\n", lines) + "\n";
            var data = Encoding.UTF8.GetBytes(serializedTech);
            return new TechnologySnapshotInfo
            {
                TechId = techId,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static ConfigNode CreateStrategyScenario(params StrategySnapshotInfo[] strategies)
        {
            var builder = new StringBuilder();
            builder.AppendLine("name = StrategySystem");
            builder.AppendLine("scene = 7");
            builder.AppendLine("STRATEGIES");
            builder.AppendLine("{");

            foreach (var strategy in strategies.OrderBy(value => value.Name))
            {
                builder.AppendLine("STRATEGY");
                builder.AppendLine("{");
                builder.Append(IndentBlock(Encoding.UTF8.GetString(strategy.Data, 0, strategy.NumBytes), "    "));
                builder.AppendLine("}");
            }

            builder.AppendLine("}");
            return new ConfigNode(builder.ToString());
        }

        private static StrategySnapshotInfo CreateStrategySnapshotInfo(string name, float factor, bool isActive)
        {
            var serialized = $"name = {name}\nfactor = {factor.ToString(CultureInfo.InvariantCulture)}\nisActive = {isActive}\n";
            var data = Encoding.UTF8.GetBytes(serialized);
            return new StrategySnapshotInfo
            {
                Name = name,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static ConfigNode CreateAchievementsScenario(params AchievementSnapshotInfo[] achievements)
        {
            var builder = new StringBuilder();
            builder.AppendLine("name = ProgressTracking");
            builder.AppendLine("scene = 7");
            builder.AppendLine("Progress");
            builder.AppendLine("{");

            foreach (var achievement in achievements.OrderBy(value => value.Id))
            {
                builder.Append(IndentBlock(Encoding.UTF8.GetString(achievement.Data, 0, achievement.NumBytes), "    "));
            }

            builder.AppendLine("}");
            return new ConfigNode(builder.ToString());
        }

        private static AchievementSnapshotInfo CreateAchievementSnapshotInfo(string id, string state)
        {
            var serialized = $"{id}\n{{\n state = {state}\n}}\n";
            var data = Encoding.UTF8.GetBytes(serialized);
            return new AchievementSnapshotInfo
            {
                Id = id,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static ScienceSubjectSnapshotInfo CreateScienceSubjectSnapshotInfo(string id, float science, float scienceCap)
        {
            var serialized = $"id = {id}\nscience = {science.ToString(CultureInfo.InvariantCulture)}\nscienceCap = {scienceCap.ToString(CultureInfo.InvariantCulture)}\n";
            var data = Encoding.UTF8.GetBytes(serialized);
            return new ScienceSubjectSnapshotInfo
            {
                Id = id,
                NumBytes = data.Length,
                Data = data
            };
        }

        private static string IndentBlock(string value, string indent)
        {
            var lines = value.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Where(line => line.Length > 0).Select(line => indent + line)) + "\n";
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

        private static ClientStructure CreateClient(string playerName)
        {
            var client = (ClientStructure)FormatterServices.GetUninitializedObject(typeof(ClientStructure));
            client.PlayerName = playerName;
            return client;
        }
    }
}
