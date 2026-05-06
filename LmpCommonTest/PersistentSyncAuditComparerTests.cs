using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Audit;
using LmpCommon.PersistentSync.Payloads.Achievements;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncAuditComparerTests
    {
        [TestInitialize]
        public void Setup()
        {
            PersistentSyncTestDomainCatalog.Configure();
        }

        [TestMethod]
        public void FundsCompareMatchesWithinTolerance()
        {
            var local = PersistentSyncPayloadSerializer.Serialize(100d);
            var server = PersistentSyncPayloadSerializer.Serialize(100.0000001d);
            var r = Compare(PersistentSyncDomainNames.Funds, local, server, 1, 1);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
            Assert.AreEqual(PersistentSyncAuditSeverity.Ok, r.Severity);
        }

        [TestMethod]
        public void FundsCompare_RevisionMismatchWhenRevisionsDifferButPayloadMatches()
        {
            var p = PersistentSyncPayloadSerializer.Serialize(50d);
            var r = Compare(PersistentSyncDomainNames.Funds, p, p, clientKnownRevision: 1, serverRevision: 2);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.RevisionMismatch, r.PrimaryKind);
            Assert.AreEqual(PersistentSyncAuditSeverity.Warning, r.Severity);
        }

        [TestMethod]
        public void FundsCompareDetectsMismatch()
        {
            var local = PersistentSyncPayloadSerializer.Serialize(10d);
            var server = PersistentSyncPayloadSerializer.Serialize(11d);
            var r = Compare(PersistentSyncDomainNames.Funds, local, server, 2, 2);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.AreEqual(PersistentSyncAuditSeverity.Error, r.Severity);
            Assert.IsTrue(r.Records.Count > 0);
        }

        [TestMethod]
        public void CompareHandlesServerErrorPath()
        {
            var local = PersistentSyncPayloadSerializer.Serialize(1f);
            var r = PersistentSyncAuditComparer.Compare(
                PersistentSyncDomainNames.Science,
                local,
                local.Length,
                Array.Empty<byte>(),
                0,
                0,
                0,
                "Registry:X");
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ServerError, r.PrimaryKind);
            Assert.AreEqual(PersistentSyncAuditSeverity.Error, r.Severity);
            StringAssert.Contains(r.Summary, "Server error");
        }

        [TestMethod]
        public void Science_Match()
        {
            var p = PersistentSyncPayloadSerializer.Serialize(3.5f);
            var r = Compare(PersistentSyncDomainNames.Science, p, p, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Reputation_Match()
        {
            var p = PersistentSyncPayloadSerializer.Serialize(8f);
            var r = Compare(PersistentSyncDomainNames.Reputation, p, p, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void GameLaunchId_Match()
        {
            var p = PersistentSyncPayloadSerializer.Serialize(42u);
            var r = Compare(PersistentSyncDomainNames.GameLaunchId, p, p, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void UpgradeableFacilities_Match()
        {
            var pl = new UpgradeableFacilityLevelPayload { FacilityId = "SpaceCenter/MissionControl", Level = 1 };
            var payload = PersistentSyncPayloadSerializer.Serialize(new UpgradeableFacilitiesPayload { Items = new[] { pl } });
            var r = Compare(PersistentSyncDomainNames.UpgradeableFacilities, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Contracts_Match_Minimal()
        {
            var g = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var c = new ContractSnapshotInfo
            {
                ContractGuid = g,
                ContractState = "Offered",
                Placement = ContractSnapshotPlacement.Current,
                Order = 0,
                Data = Encoding.UTF8.GetBytes("a"),
            };
            var envelope = new ContractsPayload { Snapshot = new ContractSnapshotPayload { Contracts = new List<ContractSnapshotInfo> { c } } };
            var payload = PersistentSyncPayloadSerializer.Serialize(envelope);
            var r = Compare(PersistentSyncDomainNames.Contracts, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Contracts_Mismatch_AddsRecords()
        {
            var g = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var c1 = new ContractSnapshotInfo
            {
                ContractGuid = g,
                ContractState = "Active",
                Placement = ContractSnapshotPlacement.Active,
                Order = 0,
                Data = Encoding.UTF8.GetBytes("left"),
            };
            var c2 = new ContractSnapshotInfo
            {
                ContractGuid = g,
                ContractState = "Completed",
                Placement = ContractSnapshotPlacement.Finished,
                Order = 1,
                Data = Encoding.UTF8.GetBytes("right"),
            };
            var local = PersistentSyncPayloadSerializer.Serialize(new ContractsPayload
            {
                Snapshot = new ContractSnapshotPayload { Contracts = new List<ContractSnapshotInfo> { c1 } }
            });
            var server = PersistentSyncPayloadSerializer.Serialize(new ContractsPayload
            {
                Snapshot = new ContractSnapshotPayload { Contracts = new List<ContractSnapshotInfo> { c2 } }
            });
            var r = Compare(PersistentSyncDomainNames.Contracts, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.IsTrue(r.Records.Count > 0);
        }

        [TestMethod]
        public void Technology_Match()
        {
            var tp = new TechnologyPayload
            {
                Technologies = new[] { new TechnologySnapshotInfo { TechId = "n1", Data = new byte[] { 1 } } },
                PartPurchases = new[]
                {
                    new PartPurchaseSnapshotInfo { TechId = "t1", PartNames = new[] { "a", "b" } }
                }
            };
            var payload = PersistentSyncPayloadSerializer.Serialize(tp);
            var r = Compare(PersistentSyncDomainNames.Technology, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void TechnologyCompareUsesTechAndPartPurchasesProjection()
        {
            var local = PersistentSyncPayloadSerializer.Serialize(new TechnologyPayload
            {
                Technologies = new[]
                {
                    new TechnologySnapshotInfo { TechId = "t1", Data = new byte[] { 1, 2 } }
                },
                PartPurchases = new[]
                {
                    new PartPurchaseSnapshotInfo { TechId = "t1", PartNames = new[] { "partB", "partA" } }
                }
            });

            var server = PersistentSyncPayloadSerializer.Serialize(new TechnologyPayload
            {
                Technologies = new[]
                {
                    new TechnologySnapshotInfo { TechId = "t1", Data = new byte[] { 1, 2 } }
                },
                PartPurchases = new[]
                {
                    new PartPurchaseSnapshotInfo { TechId = "t1", PartNames = new[] { "partA", "partB" } }
                }
            });

            var r = Compare(PersistentSyncDomainNames.Technology, local, server, 1, 1);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Strategy_Match()
        {
            var items = new[] { new StrategySnapshotInfo { Name = "S1", Data = new byte[] { 9 } } };
            var payload = PersistentSyncPayloadSerializer.Serialize(new StrategyPayload { Items = items });
            var r = Compare(PersistentSyncDomainNames.Strategy, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_Match()
        {
            var item = new AchievementSnapshotInfo { Id = "K1", Data = new byte[] { 7 } };
            var payload = PersistentSyncPayloadSerializer.Serialize(new AchievementsPayload { Items = new[] { item } });
            var r = Compare(PersistentSyncDomainNames.Achievements, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_Mismatch_ServerOnlyRow_AddsMissingOnClientRecord()
        {
            var serverItem = new AchievementSnapshotInfo { Id = "OnlyServer", Data = new byte[] { 1 } };
            var serverPl = PersistentSyncPayloadSerializer.Serialize(new AchievementsPayload { Items = new[] { serverItem } });
            var localPl = PersistentSyncPayloadSerializer.Serialize(new AchievementsPayload { Items = Array.Empty<AchievementSnapshotInfo>() });
            var r = Compare(PersistentSyncDomainNames.Achievements, localPl, serverPl, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.IsTrue(r.Records.Any(x => x.Kind == PersistentSyncAuditDifferenceKind.MissingOnClient));
        }

        [TestMethod]
        public void Achievements_SemanticCosmeticCfgDiff_TreatedAsOk_WhenDeepKeysMatch()
        {
            var loose = "name = MileA\nreached = False\ncomplete = False\nextra = 1\n";
            var tight = "name=MileA\nreached=False\ncomplete=False\nextra=1\n";
            var local = PayloadForAchievement("ignored", loose);
            var server = PayloadForAchievement("ignored", tight);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
            Assert.AreEqual(0, r.Records.Count, "benign formatting must not emit diff records");
        }

        [TestMethod]
        public void Achievements_CompletedUtFloatIgnoredForMilestoneWatchKeys()
        {
            var a = "name=MileC\nreached=True\ncomplete=True\ncompleted = 241175.84\n";
            var b = "name=MileC\nreached=True\ncomplete=True\ncompleted = 241183.35\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_SemanticMilestoneDelta_SurfacesReachedContradiction()
        {
            var a = "name=MileB\nreached=True\ncomplete=False\n";
            var b = "name=MileB\nreached=False\ncomplete=False\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("watchKeysMatch=no", StringComparison.Ordinal) >= 0));
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("delta reached", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void Achievements_MapKeyPrefersCfgName_OverWireId()
        {
            var cfg = "name = UnifyMe\nreached = False\ncomplete = False\n";
            var local = PayloadForAchievement("wireA", cfg);
            var server = PayloadForAchievement("wireB", cfg);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_MapKeyPrefersOuterCfgHeader_OverWireId()
        {
            var cfg = "UnifyOuter\n{\n    reached = True\n    complete = False\n}\n";
            var local = PayloadForAchievement("wireA", cfg);
            var server = PayloadForAchievement("wireB", cfg);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_SemanticNestedProgress_WatchKeysMatchDespiteFormatting()
        {
            var loose = "FirstLaunch\n{\n    PROGRESS\n    {\n        reached = True\n        complete = False\n    }\n}\n";
            var tight = "FirstLaunch\n{\nPROGRESS\n{\nreached=True\ncomplete=False\n}\n}\n";
            var local = PayloadForAchievement("ignored", loose);
            var server = PayloadForAchievement("ignored", tight);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_SemanticNestedProgress_SurfacesCompleteDelta()
        {
            var a = "FL\n{\nPROGRESS\n{\nreached=True\ncomplete=False\n}\n}\n";
            var b = "FL\n{\nPROGRESS\n{\nreached=True\ncomplete=True\n}\n}\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("watchKeysMatch=no", StringComparison.Ordinal) >= 0));
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("delta complete", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void Achievements_SemanticNested_IsReachedAlias_WatchKeysMatch()
        {
            var a = "FL\n{\nPROGRESS\n{\nIsReached=True\nIsComplete=False\n}\n}\n";
            var b = "FL\n{\nPROGRESS\n{\nIsReached=True\nIsComplete=False\n}\n}\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_SemanticCompletedUtOnly_SmallSkew_TreatedAsOk()
        {
            var a = "FirstLaunch\n{\ncompleted = 244509.123\n}\n";
            var b = "FirstLaunch\n{\ncompleted = 244519.456\n}\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void Achievements_SemanticCompletedUtOnly_LargeSkew_RemainsMismatch()
        {
            var a = "FirstLaunch\n{\ncompleted = 1000\n}\n";
            var b = "FirstLaunch\n{\ncompleted = 5000\n}\n";
            var local = PayloadForAchievement("x", a);
            var server = PayloadForAchievement("x", b);
            var r = Compare(PersistentSyncDomainNames.Achievements, local, server, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.ValueMismatch, r.PrimaryKind);
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("completedUt", StringComparison.Ordinal) >= 0),
                "expected completed UT line: " + string.Join(" | ", r.SemanticDiagnostics));
            Assert.IsTrue(r.SemanticDiagnostics.Any(x => x.IndexOf("absDiff=", StringComparison.Ordinal) >= 0));
        }

        private static byte[] PayloadForAchievement(string wireId, string cfgUtf8)
        {
            var item = new AchievementSnapshotInfo { Id = wireId ?? string.Empty, Data = Encoding.UTF8.GetBytes(cfgUtf8) };
            return PersistentSyncPayloadSerializer.Serialize(new AchievementsPayload { Items = new[] { item } });
        }

        [TestMethod]
        public void ScienceSubjects_Match()
        {
            var item = new ScienceSubjectSnapshotInfo { Id = "sub@Body", Data = new byte[] { 3 } };
            var payload = PersistentSyncPayloadSerializer.Serialize(new ScienceSubjectsPayload { Items = new[] { item } });
            var r = Compare(PersistentSyncDomainNames.ScienceSubjects, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void ExperimentalParts_Match()
        {
            var items = new[] { new ExperimentalPartSnapshotInfo { PartName = "engine", Count = 2 } };
            var payload = PersistentSyncPayloadSerializer.Serialize(new ExperimentalPartsPayload { Items = items });
            var r = Compare(PersistentSyncDomainNames.ExperimentalParts, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void PartPurchases_Match()
        {
            var items = new[]
            {
                new PartPurchaseSnapshotInfo { TechId = "eng", PartNames = new[] { "fuelTank" } }
            };
            var payload = PersistentSyncPayloadSerializer.Serialize(new PartPurchasesPayload { Items = items });
            var r = Compare(PersistentSyncDomainNames.PartPurchases, payload, payload, 0, 0);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.Ok, r.PrimaryKind);
        }

        [TestMethod]
        public void UnknownDomainIdFallsBackToNoSemanticAdapter()
        {
            var b = PersistentSyncPayloadSerializer.Serialize(1d);
            var r = PersistentSyncAuditComparer.Compare(
                "NotADomain",
                b,
                b.Length,
                b,
                b.Length,
                0,
                0,
                string.Empty);
            Assert.AreEqual(PersistentSyncAuditDifferenceKind.NoSemanticAdapter, r.PrimaryKind);
            Assert.AreEqual(PersistentSyncAuditSeverity.Warning, r.Severity);
        }

        private static PersistentSyncAuditComparisonResult Compare(
            string domainId,
            byte[] local,
            byte[] server,
            long clientKnownRevision,
            long serverRevision)
        {
            return PersistentSyncAuditComparer.Compare(
                domainId,
                local,
                local.Length,
                server,
                server.Length,
                clientKnownRevision,
                serverRevision,
                string.Empty);
        }
    }
}
