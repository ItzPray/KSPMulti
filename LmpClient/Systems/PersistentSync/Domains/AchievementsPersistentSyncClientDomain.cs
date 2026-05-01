using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    [PersistentSyncStockScenario("ProgressTracking")]
    public class AchievementsPersistentSyncClientDomain : SyncClientDomain<AchievementSnapshotInfo[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<AchievementsPersistentSyncClientDomain>();
        }

        private AchievementSnapshotInfo[] _pendingAchievements;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, AchievementSnapshotInfo[] payload)
        {
            _pendingAchievements = payload ?? new AchievementSnapshotInfo[0];
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingAchievements == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ProgressTracking.Instance == null || Funding.Instance == null || ResearchAndDevelopment.Instance == null || Reputation.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var rootNode = new ConfigNode();
            try
            {
                foreach (var achievement in _pendingAchievements.Where(value => value != null && value.Data.Length > 0))
                {
                    // Payload is a ConfigNodeSerializer-serialized node; `new ConfigNode(string)` would
                    // silently store the entire text as the node name with no child values, so the
                    // achievement tree would receive empty nodes. Use the matching deserializer.
                    var achievementNode = achievement.Data.DeserializeToConfigNode(achievement.Data.Length);
                    if (achievementNode != null)
                    {
                        rootNode.AddNode(achievementNode);
                    }
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            ShareAchievementsSystem.Singleton.ApplyAchievementSnapshotTree(rootNode, "PersistentSyncSnapshotApply");
            _pendingAchievements = null;

            // Stock's contract generator (RefreshContracts inside Replenish) keys off *local* ProgressTracking.
            // When another player completes a progression-gated mission first, the server advances Achievements
            // and Contracts, but snapshot messages often arrive so that this client applies **Contracts** (and
            // runs ReplenishStockOffersAfterPersistentSnapshotApply) **before** the matching Achievements snapshot
            // lands. Replenish then sees FirstLaunch still incomplete here, so no new OfferObserved rows are minted
            // for the server, and non-lock-holders already kill any local stock offers in ContractOffered. Everyone
            // appears "stuck" until this client also completes the mission locally (gameplay fires achievements
            // before Replenish). Queue a deferred controlled refresh now that achievementTree matches the server.
            ShareContractsSystem.Singleton?.RequestControlledStockContractRefresh(
                "PersistentSyncSnapshotApply:AfterAchievementsFlush");

            return PersistentSyncApplyOutcome.Applied;
        }
    }
}
