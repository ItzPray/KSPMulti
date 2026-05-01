using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
                PersistentSyncDomainNames.Funds,
                PersistentSyncDomainNames.Science,
                PersistentSyncDomainNames.Reputation
            };

            var deserialized = (PersistentSyncCliMsg)RoundTripClientMessage(ClientFactory.CreateNew<PersistentSyncCliMsg>(msgData));
            var roundTripData = (PersistentSyncRequestMsgData)deserialized.Data;

            Assert.AreEqual(3, roundTripData.DomainCount);
            CollectionAssert.AreEqual(msgData.Domains, roundTripData.Domains.Take(roundTripData.DomainCount).ToArray());
        }

        [TestMethod]
        public void TestSerializeDeserializePersistentSyncIntentMsg()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<double>(321.45d, "Career reward"));
            var msgData = ClientFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            msgData.DomainId = PersistentSyncDomainNames.Funds;
            msgData.ClientKnownRevision = 42;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;
            msgData.Reason = "Career reward";

            var deserialized = (PersistentSyncCliMsg)RoundTripClientMessage(ClientFactory.CreateNew<PersistentSyncCliMsg>(msgData));
            var roundTripData = (PersistentSyncIntentMsgData)deserialized.Data;

            Assert.AreEqual(PersistentSyncDomainNames.Funds, roundTripData.DomainId);
            Assert.AreEqual(42L, roundTripData.ClientKnownRevision);
            Assert.AreEqual(payload.Length, roundTripData.NumBytes);
            Assert.AreEqual("Career reward", roundTripData.Reason);

            var funds = PersistentSyncPayloadSerializer.Deserialize<PersistentSyncValueWithReason<double>>(roundTripData.Payload, roundTripData.NumBytes);
            Assert.AreEqual(321.45d, funds.Value);
            Assert.AreEqual("Career reward", funds.Reason);
        }

        [TestMethod]
        public void TestSerializeDeserializePersistentSyncSnapshotMsg()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(12.5f);
            var msgData = ServerFactory.CreateNewMessageData<PersistentSyncSnapshotMsgData>();
            msgData.DomainId = PersistentSyncDomainNames.Reputation;
            msgData.Revision = 7;
            msgData.AuthorityPolicy = PersistentAuthorityPolicy.AnyClientIntent;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;

            var deserialized = (PersistentSyncSrvMsg)RoundTripServerMessage(ServerFactory.CreateNew<PersistentSyncSrvMsg>(msgData));
            var roundTripData = (PersistentSyncSnapshotMsgData)deserialized.Data;

            Assert.AreEqual(PersistentSyncDomainNames.Reputation, roundTripData.DomainId);
            Assert.AreEqual(7L, roundTripData.Revision);
            Assert.AreEqual(PersistentAuthorityPolicy.AnyClientIntent, roundTripData.AuthorityPolicy);
            Assert.AreEqual(12.5f, PersistentSyncPayloadSerializer.Deserialize<float>(roundTripData.Payload, roundTripData.NumBytes));
        }

        [TestMethod]
        public void TestGameLaunchIdPayloadSerializerRoundTrip()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<uint>(4242u, "VesselProto"));
            var launchId = PersistentSyncPayloadSerializer.Deserialize<PersistentSyncValueWithReason<uint>>(payload, payload.Length);
            Assert.AreEqual(4242u, launchId.Value);
            Assert.AreEqual("VesselProto", launchId.Reason);

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(9001u);
            Assert.AreEqual(9001u, PersistentSyncPayloadSerializer.Deserialize<uint>(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestFundsPayloadSerializerRoundTrip()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<double>(999.5d, "Admin"));
            var funds = PersistentSyncPayloadSerializer.Deserialize<PersistentSyncValueWithReason<double>>(payload, payload.Length);
            Assert.AreEqual(999.5d, funds.Value);
            Assert.AreEqual("Admin", funds.Reason);

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(999.5d);
            Assert.AreEqual(999.5d, PersistentSyncPayloadSerializer.Deserialize<double>(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestSciencePayloadSerializerRoundTrip()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<float>(123.25f, "Lab"));
            var science = PersistentSyncPayloadSerializer.Deserialize<PersistentSyncValueWithReason<float>>(payload, payload.Length);
            Assert.AreEqual(123.25f, science.Value);
            Assert.AreEqual("Lab", science.Reason);

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(123.25f);
            Assert.AreEqual(123.25f, PersistentSyncPayloadSerializer.Deserialize<float>(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestReputationPayloadSerializerRoundTrip()
        {
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<float>(5.75f, "Contract"));
            var reputation = PersistentSyncPayloadSerializer.Deserialize<PersistentSyncValueWithReason<float>>(payload, payload.Length);
            Assert.AreEqual(5.75f, reputation.Value);
            Assert.AreEqual("Contract", reputation.Reason);

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(5.75f);
            Assert.AreEqual(5.75f, PersistentSyncPayloadSerializer.Deserialize<float>(snapshotPayload, snapshotPayload.Length));
        }

        [TestMethod]
        public void TestUpgradeableFacilitiesPayloadSerializerRoundTrip()
        {
            var intentPayload = PersistentSyncPayloadSerializer.Serialize(new UpgradeableFacilityLevelPayload { FacilityId = "SpaceCenter/MissionControl", Level = 2 });
            var facilityIntent = PersistentSyncPayloadSerializer.Deserialize<UpgradeableFacilityLevelPayload>(intentPayload, intentPayload.Length);
            Assert.AreEqual("SpaceCenter/MissionControl", facilityIntent.FacilityId);
            Assert.AreEqual(2, facilityIntent.Level);

            var facilities = new[]
            {
                new UpgradeableFacilityLevelPayload { FacilityId = "SpaceCenter/MissionControl", Level = 2 },
                new UpgradeableFacilityLevelPayload { FacilityId = "SpaceCenter/TrackingStation", Level = 1 }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(facilities);
            var roundTripFacilities = PersistentSyncPayloadSerializer.Deserialize<UpgradeableFacilityLevelPayload[]>(snapshotPayload, snapshotPayload.Length)
                .ToDictionary(level => level.FacilityId, level => level.Level);

            Assert.AreEqual(2, roundTripFacilities["SpaceCenter/MissionControl"]);
            Assert.AreEqual(1, roundTripFacilities["SpaceCenter/TrackingStation"]);
            Assert.AreEqual(2, roundTripFacilities.Count);
        }

        [TestMethod]
        public void TestContractSnapshotPayloadRoundTrip()
        {
            var contractPayload = new[]
            {
                new ContractSnapshotInfo
                {
                    ContractGuid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ContractState = "Offered",
                    Placement = ContractSnapshotPlacement.Current,
                    Order = 0,
                    Data = System.Text.Encoding.UTF8.GetBytes("CONTRACT\n{\n guid = 11111111-1111-1111-1111-111111111111\n state = Offered\n}\n"),
                },
                new ContractSnapshotInfo
                {
                    ContractGuid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ContractState = "Completed",
                    Placement = ContractSnapshotPlacement.Finished,
                    Order = 5,
                    Data = System.Text.Encoding.UTF8.GetBytes("CONTRACT\n{\n guid = 22222222-2222-2222-2222-222222222222\n state = Completed\n}\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(new ContractSnapshotPayload { Contracts = contractPayload.ToList() });
            var roundTripContracts = PersistentSyncPayloadSerializer.Deserialize<ContractSnapshotPayload>(snapshotPayload, snapshotPayload.Length).Contracts;

            Assert.AreEqual(2, roundTripContracts.Count);
            Assert.AreEqual(contractPayload[0].ContractGuid, roundTripContracts[0].ContractGuid);
            Assert.AreEqual("Offered", roundTripContracts[0].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Current, roundTripContracts[0].Placement);
            Assert.AreEqual(0, roundTripContracts[0].Order);
            Assert.AreEqual(contractPayload[1].ContractGuid, roundTripContracts[1].ContractGuid);
            Assert.AreEqual("Completed", roundTripContracts[1].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Finished, roundTripContracts[1].Placement);
            Assert.AreEqual(5, roundTripContracts[1].Order);
        }

        [TestMethod]
        public void TestContractSnapshotPayloadFullReplaceRoundTrip()
        {
            var contractPayload = new[]
            {
                new ContractSnapshotInfo
                {
                    ContractGuid = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                    ContractState = "Active",
                    Placement = ContractSnapshotPlacement.Active,
                    Order = 3,
                    Data = System.Text.Encoding.UTF8.GetBytes("guid = aaaaaaaa-1111-1111-1111-111111111111\nstate = Active\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(new ContractSnapshotPayload
            {
                Mode = ContractSnapshotPayloadMode.FullReplace,
                Contracts = contractPayload.ToList()
            });
            var envelope = PersistentSyncPayloadSerializer.Deserialize<ContractSnapshotPayload>(snapshotPayload, snapshotPayload.Length);

            Assert.AreEqual(ContractSnapshotPayloadMode.FullReplace, envelope.Mode);
            Assert.AreEqual(1, envelope.Contracts.Count);
            Assert.AreEqual(contractPayload[0].ContractGuid, envelope.Contracts[0].ContractGuid);
            Assert.AreEqual("Active", envelope.Contracts[0].ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Active, envelope.Contracts[0].Placement);
            Assert.AreEqual(3, envelope.Contracts[0].Order);
        }

        [TestMethod]
        public void TestContractIntentPayloadRoundTrip()
        {
            var contract = new ContractSnapshotInfo
            {
                ContractGuid = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
                ContractState = "Active",
                Placement = ContractSnapshotPlacement.Active,
                Order = 4,
                Data = System.Text.Encoding.UTF8.GetBytes("guid = bbbbbbbb-1111-1111-1111-111111111111\nstate = Active\n"),
            };

            var commandPayload = PersistentSyncPayloadSerializer.Serialize(new ContractIntentPayload
            {
                Kind = ContractIntentPayloadKind.AcceptContract,
                ContractGuid = contract.ContractGuid
            });
            var commandRoundTrip = PersistentSyncPayloadSerializer.Deserialize<ContractIntentPayload>(commandPayload, commandPayload.Length);
            Assert.AreEqual(ContractIntentPayloadKind.AcceptContract, commandRoundTrip.Kind);
            Assert.AreEqual(contract.ContractGuid, commandRoundTrip.ContractGuid);
            Assert.IsNull(commandRoundTrip.Contract);
            Assert.AreEqual(0, commandRoundTrip.Contracts.Length);

            var proposalPayload = PersistentSyncPayloadSerializer.Serialize(new ContractIntentPayload
            {
                Kind = ContractIntentPayloadKind.ParameterProgressObserved,
                ContractGuid = contract.ContractGuid,
                Contract = contract
            });
            var proposalRoundTrip = PersistentSyncPayloadSerializer.Deserialize<ContractIntentPayload>(proposalPayload, proposalPayload.Length);
            Assert.AreEqual(ContractIntentPayloadKind.ParameterProgressObserved, proposalRoundTrip.Kind);
            Assert.AreEqual(contract.ContractGuid, proposalRoundTrip.ContractGuid);
            Assert.IsNotNull(proposalRoundTrip.Contract);
            Assert.AreEqual("Active", proposalRoundTrip.Contract.ContractState);
            Assert.AreEqual(ContractSnapshotPlacement.Active, proposalRoundTrip.Contract.Placement);

            var reconcilePayload = PersistentSyncPayloadSerializer.Serialize(new ContractIntentPayload
            {
                Kind = ContractIntentPayloadKind.FullReconcile,
                Contracts = new[] { contract }
            });
            var reconcileRoundTrip = PersistentSyncPayloadSerializer.Deserialize<ContractIntentPayload>(reconcilePayload, reconcilePayload.Length);
            Assert.AreEqual(ContractIntentPayloadKind.FullReconcile, reconcileRoundTrip.Kind);
            Assert.AreEqual(1, reconcileRoundTrip.Contracts.Length);
            Assert.AreEqual(contract.ContractGuid, reconcileRoundTrip.Contracts[0].ContractGuid);
        }

        [TestMethod]
        public void TypedPayloadSerializerMatchesLegacyWireLayouts()
        {
            var contract = new ContractSnapshotInfo
            {
                ContractGuid = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
                ContractState = "Active",
                Placement = ContractSnapshotPlacement.Active,
                Order = 4,
                Data = Encoding.UTF8.GetBytes("guid = bbbbbbbb-1111-1111-1111-111111111111\nstate = Active\n")
            };

            CollectionAssert.AreEqual(BitConverter.GetBytes(12.5d), PersistentSyncPayloadSerializer.Serialize(12.5d));
            CollectionAssert.AreEqual(BitConverter.GetBytes(12.5f), PersistentSyncPayloadSerializer.Serialize(12.5f));
            CollectionAssert.AreEqual(BitConverter.GetBytes(42u), PersistentSyncPayloadSerializer.Serialize(42u));
            CollectionAssert.AreEqual(LegacyValueWithReason(12.5d, "Funds"), PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<double>(12.5d, "Funds")));
            CollectionAssert.AreEqual(LegacyValueWithReason(12.5f, "Science"), PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<float>(12.5f, "Science")));
            CollectionAssert.AreEqual(LegacyValueWithReason(42u, "Launch"), PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<uint>(42u, "Launch")));
            CollectionAssert.AreEqual(LegacyStringInt("SpaceCenter/MissionControl", 2), PersistentSyncPayloadSerializer.Serialize(new PersistentSyncStringIntPayload("SpaceCenter/MissionControl", 2)));

            CollectionAssert.AreEqual(
                LegacyFacilityLevels(new[] { new UpgradeableFacilityLevelPayload { FacilityId = "A", Level = 1 }, new UpgradeableFacilityLevelPayload { FacilityId = "B", Level = 2 } }),
                PersistentSyncPayloadSerializer.Serialize(
                    new[] { new UpgradeableFacilityLevelPayload { FacilityId = "A", Level = 1 }, new UpgradeableFacilityLevelPayload { FacilityId = "B", Level = 2 } }));
            CollectionAssert.AreEqual(
                LegacyBlobArray("Tech", "basicRocketry", contract.Data, contract.Data.Length),
                PersistentSyncPayloadSerializer.Serialize(new[] { new TechnologySnapshotInfo { TechId = "basicRocketry", Data = contract.Data } }));
            CollectionAssert.AreEqual(
                LegacyBlobArray("Name", "BailoutGrant", contract.Data, contract.Data.Length),
                PersistentSyncPayloadSerializer.Serialize(new[] { new StrategySnapshotInfo { Name = "BailoutGrant", Data = contract.Data } }));
            CollectionAssert.AreEqual(
                LegacyBlobArray("Id", "Kerbin", contract.Data, contract.Data.Length),
                PersistentSyncPayloadSerializer.Serialize(new[] { new AchievementSnapshotInfo { Id = "Kerbin", Data = contract.Data } }));
            CollectionAssert.AreEqual(
                LegacyBlobArray("Id", "crewReport@Kerbin", contract.Data, contract.Data.Length),
                PersistentSyncPayloadSerializer.Serialize(new[] { new ScienceSubjectSnapshotInfo { Id = "crewReport@Kerbin", Data = contract.Data } }));
            CollectionAssert.AreEqual(
                LegacyExperimentalParts(new[] { new ExperimentalPartSnapshotInfo { PartName = "liquidEngine", Count = 2 } }),
                PersistentSyncPayloadSerializer.Serialize(new[] { new ExperimentalPartSnapshotInfo { PartName = "liquidEngine", Count = 2 } }));
            CollectionAssert.AreEqual(
                LegacyPartPurchases(new[] { new PartPurchaseSnapshotInfo { TechId = "engineering101", PartNames = new[] { "radialDecoupler", "stackSeparator" } } }),
                PersistentSyncPayloadSerializer.Serialize(new[] { new PartPurchaseSnapshotInfo { TechId = "engineering101", PartNames = new[] { "radialDecoupler", "stackSeparator" } } }));
            CollectionAssert.AreEqual(
                LegacyContractSnapshot(ContractSnapshotPayloadMode.Delta, new[] { contract }),
                PersistentSyncPayloadSerializer.Serialize(new ContractSnapshotPayload { Contracts = new[] { contract }.ToList() }));
            CollectionAssert.AreEqual(
                LegacyContractSnapshot(ContractSnapshotPayloadMode.FullReplace, new[] { contract }),
                PersistentSyncPayloadSerializer.Serialize(new ContractSnapshotPayload { Mode = ContractSnapshotPayloadMode.FullReplace, Contracts = new[] { contract }.ToList() }));
            CollectionAssert.AreEqual(
                LegacyContractIntent(new ContractIntentPayload
                {
                    Kind = ContractIntentPayloadKind.ParameterProgressObserved,
                    ContractGuid = contract.ContractGuid,
                    Contract = contract
                }),
                PersistentSyncPayloadSerializer.Serialize(new ContractIntentPayload
                {
                    Kind = ContractIntentPayloadKind.ParameterProgressObserved,
                    ContractGuid = contract.ContractGuid,
                    Contract = contract
                }));
            CollectionAssert.AreEqual(
                LegacyContractIntent(new ContractIntentPayload
                {
                    Kind = ContractIntentPayloadKind.FullReconcile,
                    Contracts = new[] { contract }
                }),
                PersistentSyncPayloadSerializer.Serialize(new ContractIntentPayload
                {
                    Kind = ContractIntentPayloadKind.FullReconcile,
                    Contracts = new[] { contract }
                }));
        }

        [TestMethod]
        public void TestContractSnapshotInfoComparerIgnoresWhitespaceOnlyDifferences()
        {
            var guid = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var compact = new ContractSnapshotInfo
            {
                ContractGuid = guid,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 0,
                Data = System.Text.Encoding.UTF8.GetBytes("guid = 33333333-3333-3333-3333-333333333333\nstate = Offered\nPARAM\n{\nstate = Complete\n}\n"),
            };
            var spaced = new ContractSnapshotInfo
            {
                ContractGuid = guid,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 99,
                Data = System.Text.Encoding.UTF8.GetBytes("guid   =   33333333-3333-3333-3333-333333333333\r\nstate = Offered\r\n\r\nPARAM\r\n{\r\n    state = Complete\r\n}\r\n"),
            };

            Assert.IsTrue(ContractSnapshotInfoComparer.AreEquivalent(compact, spaced));
        }

        [TestMethod]
        public void TestContractSnapshotChangeTrackerFiltersEquivalentSnapshotsByGuid()
        {
            var tracker = new ContractSnapshotChangeTracker();
            var guid = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var baseline = new ContractSnapshotInfo
            {
                ContractGuid = guid,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 1,
                Data = System.Text.Encoding.UTF8.GetBytes("guid = 44444444-4444-4444-4444-444444444444\nstate = Offered\nvalues = 1,0,0\n"),
            };
            var equivalent = new ContractSnapshotInfo
            {
                ContractGuid = guid,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 2,
                Data = System.Text.Encoding.UTF8.GetBytes("guid = 44444444-4444-4444-4444-444444444444\r\nstate = Offered\r\nvalues = 1,0,0\r\n"),
            };
            var changed = new ContractSnapshotInfo
            {
                ContractGuid = guid,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 3,
                Data = System.Text.Encoding.UTF8.GetBytes("guid = 44444444-4444-4444-4444-444444444444\nstate = Offered\nvalues = 1,1,0\n"),
            };

            CollectionAssert.AreEqual(new[] { guid }, tracker.FilterChanged(new[] { baseline }).Select(c => c.ContractGuid).ToArray());
            Assert.AreEqual(0, tracker.FilterChanged(new[] { equivalent }).Length);
            CollectionAssert.AreEqual(new[] { guid }, tracker.FilterChanged(new[] { changed }).Select(c => c.ContractGuid).ToArray());
        }

        [TestMethod]
        public void TestTechnologySnapshotPayloadRoundTrip()
        {
            var technologyPayload = new[]
            {
                new TechnologySnapshotInfo
                {
                    TechId = "basicRocketry",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = basicRocketry\nstate = Available\ncost = 5\npart = liquidEngine\n"),
                },
                new TechnologySnapshotInfo
                {
                    TechId = "engineering101",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = engineering101\nstate = Available\ncost = 15\npart = radialDecoupler\npart = stackSeparator\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(technologyPayload);
            var roundTripTechnologies = PersistentSyncPayloadSerializer.Deserialize<TechnologySnapshotInfo[]>(snapshotPayload, snapshotPayload.Length);

            Assert.AreEqual(2, roundTripTechnologies.Length);
            Assert.AreEqual("basicRocketry", roundTripTechnologies[0].TechId);
            Assert.AreEqual(technologyPayload[0].Data.Length, roundTripTechnologies[0].Data.Length);
            Assert.AreEqual("engineering101", roundTripTechnologies[1].TechId);
            Assert.AreEqual(technologyPayload[1].Data.Length, roundTripTechnologies[1].Data.Length);
        }

        [TestMethod]
        public void TestStrategySnapshotPayloadRoundTrip()
        {
            var strategyPayload = new[]
            {
                new StrategySnapshotInfo
                {
                    Name = "BailoutGrant",
                    Data = System.Text.Encoding.UTF8.GetBytes("name = BailoutGrant\nfactor = 0.25\nisActive = True\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(strategyPayload);
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<StrategySnapshotInfo[]>(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("BailoutGrant", roundTrip[0].Name);
            Assert.AreEqual(strategyPayload[0].Data.Length, roundTrip[0].Data.Length);
        }

        [TestMethod]
        public void TestAchievementSnapshotPayloadRoundTrip()
        {
            var achievementPayload = new[]
            {
                new AchievementSnapshotInfo
                {
                    Id = "Kerbin",
                    Data = System.Text.Encoding.UTF8.GetBytes("Kerbin\n{\n state = Complete\n}\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(achievementPayload);
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<AchievementSnapshotInfo[]>(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("Kerbin", roundTrip[0].Id);
            Assert.AreEqual(achievementPayload[0].Data.Length, roundTrip[0].Data.Length);
        }

        [TestMethod]
        public void TestScienceSubjectSnapshotPayloadRoundTrip()
        {
            var subjectPayload = new[]
            {
                new ScienceSubjectSnapshotInfo
                {
                    Id = "crewReport@KerbinSrfLandedLaunchPad",
                    Data = System.Text.Encoding.UTF8.GetBytes("id = crewReport@KerbinSrfLandedLaunchPad\nscience = 1\nscienceCap = 5\n"),
                }
            };

            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(subjectPayload);
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<ScienceSubjectSnapshotInfo[]>(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual(subjectPayload[0].Id, roundTrip[0].Id);
            Assert.AreEqual(subjectPayload[0].Data.Length, roundTrip[0].Data.Length);
        }

        [TestMethod]
        public void TestExperimentalPartsSnapshotPayloadRoundTrip()
        {
            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(new[]
            {
                new ExperimentalPartSnapshotInfo { PartName = "liquidEngine", Count = 2 },
                new ExperimentalPartSnapshotInfo { PartName = "radialDecoupler", Count = 1 }
            });
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<ExperimentalPartSnapshotInfo[]>(snapshotPayload);

            Assert.AreEqual(2, roundTrip.Length);
            Assert.AreEqual("liquidEngine", roundTrip[0].PartName);
            Assert.AreEqual(2, roundTrip[0].Count);
            Assert.AreEqual("radialDecoupler", roundTrip[1].PartName);
            Assert.AreEqual(1, roundTrip[1].Count);
        }

        [TestMethod]
        public void TestPartPurchasesSnapshotPayloadRoundTrip()
        {
            var snapshotPayload = PersistentSyncPayloadSerializer.Serialize(new[]
            {
                new PartPurchaseSnapshotInfo
                {
                    TechId = "engineering101",
                    PartNames = new[] { "radialDecoupler", "stackSeparator" }
                }
            });
            var roundTrip = PersistentSyncPayloadSerializer.Deserialize<PartPurchaseSnapshotInfo[]>(snapshotPayload);

            Assert.AreEqual(1, roundTrip.Length);
            Assert.AreEqual("engineering101", roundTrip[0].TechId);
            CollectionAssert.AreEqual(new[] { "radialDecoupler", "stackSeparator" }, roundTrip[0].PartNames);
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

        private static byte[] LegacyValueWithReason(double value, string reason) => Legacy(writer =>
        {
            writer.Write(value);
            writer.Write(reason ?? string.Empty);
        });

        private static byte[] LegacyValueWithReason(float value, string reason) => Legacy(writer =>
        {
            writer.Write(value);
            writer.Write(reason ?? string.Empty);
        });

        private static byte[] LegacyValueWithReason(uint value, string reason) => Legacy(writer =>
        {
            writer.Write(value);
            writer.Write(reason ?? string.Empty);
        });

        private static byte[] LegacyStringInt(string text, int number) => Legacy(writer =>
        {
            writer.Write(text ?? string.Empty);
            writer.Write(number);
        });

        private static byte[] LegacyFacilityLevels(UpgradeableFacilityLevelPayload[] values) => Legacy(writer =>
        {
            var ordered = (values ?? new UpgradeableFacilityLevelPayload[0]).OrderBy(value => value.FacilityId).ToArray();
            writer.Write(ordered.Length);
            foreach (var value in ordered)
            {
                writer.Write(value.FacilityId ?? string.Empty);
                writer.Write(value.Level);
            }
        });

        private static byte[] LegacyBlobArray(string keyField, string key, byte[] data, int numBytes) => Legacy(writer =>
        {
            writer.Write(1);
            writer.Write(key ?? string.Empty);
            writer.Write(numBytes);
            writer.Write(data ?? new byte[0], 0, numBytes);
        });

        private static byte[] LegacyExperimentalParts(ExperimentalPartSnapshotInfo[] parts) => Legacy(writer =>
        {
            writer.Write(parts?.Length ?? 0);
            foreach (var part in parts ?? new ExperimentalPartSnapshotInfo[0])
            {
                writer.Write(part?.PartName ?? string.Empty);
                writer.Write(part?.Count ?? 0);
            }
        });

        private static byte[] LegacyPartPurchases(PartPurchaseSnapshotInfo[] purchases) => Legacy(writer =>
        {
            writer.Write(purchases?.Length ?? 0);
            foreach (var purchase in purchases ?? new PartPurchaseSnapshotInfo[0])
            {
                writer.Write(purchase?.TechId ?? string.Empty);
                var parts = purchase?.PartNames ?? new string[0];
                writer.Write(parts.Length);
                foreach (var part in parts)
                {
                    writer.Write(part ?? string.Empty);
                }
            }
        });

        private static byte[] LegacyContractSnapshot(ContractSnapshotPayloadMode mode, ContractSnapshotInfo[] contracts) => Legacy(writer =>
        {
            var safeContracts = contracts ?? new ContractSnapshotInfo[0];
            if (mode == ContractSnapshotPayloadMode.Delta)
            {
                writer.Write(safeContracts.Length);
            }
            else
            {
                writer.Write(-1);
                writer.Write((byte)mode);
                writer.Write(safeContracts.Length);
            }

            foreach (var contract in safeContracts)
            {
                WriteLegacyContract(writer, contract);
            }
        });

        private static byte[] LegacyContractIntent(ContractIntentPayload payload) => Legacy(writer =>
        {
            var safePayload = payload ?? new ContractIntentPayload();
            writer.Write(unchecked((int)0x434E5452));
            writer.Write((byte)1);
            writer.Write((byte)safePayload.Kind);
            writer.Write(safePayload.ContractGuid != Guid.Empty);
            if (safePayload.ContractGuid != Guid.Empty)
            {
                writer.Write(safePayload.ContractGuid.ToByteArray());
            }

            writer.Write(safePayload.Contract != null);
            if (safePayload.Contract != null)
            {
                WriteLegacyContract(writer, safePayload.Contract);
            }

            var contracts = safePayload.Contracts ?? new ContractSnapshotInfo[0];
            writer.Write(contracts.Length);
            foreach (var contract in contracts)
            {
                WriteLegacyContract(writer, contract);
            }
        });

        private static void WriteLegacyContract(BinaryWriter writer, ContractSnapshotInfo contract)
        {
            var safeContract = contract ?? new ContractSnapshotInfo();
            writer.Write(safeContract.ContractGuid.ToByteArray());
            writer.Write(safeContract.ContractState ?? string.Empty);
            writer.Write((byte)safeContract.Placement);
            writer.Write(safeContract.Order);
            writer.Write(safeContract.Data.Length);
            writer.Write(safeContract.Data ?? new byte[0], 0, safeContract.Data.Length);
        }

        private static byte[] Legacy(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
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


