using HarmonyLib;
using LmpClient;
using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareContracts;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Linq;

namespace LmpClient.Systems.PersistentSync.Domains
{
    public class AchievementsPersistentSyncClientDomain : SyncClientDomain<AchievementsPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<AchievementsPersistentSyncClientDomain>();
        }

        private AchievementSnapshotInfo[] _pendingAchievements;

        private bool _reverting;

        protected override void OnDomainEnabled()
        {
            GameEvents.OnProgressReached.Add(OnProgressReached);
            GameEvents.OnProgressComplete.Add(OnProgressComplete);
            GameEvents.OnProgressAchieved.Add(OnProgressAchieved);
            RevertEvent.onRevertingToLaunch.Add(OnRevertingToLaunch);
            RevertEvent.onReturningToEditor.Add(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGuiReady);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnProgressReached.Remove(OnProgressReached);
            GameEvents.OnProgressComplete.Remove(OnProgressComplete);
            GameEvents.OnProgressAchieved.Remove(OnProgressAchieved);
            RevertEvent.onRevertingToLaunch.Remove(OnRevertingToLaunch);
            RevertEvent.onReturningToEditor.Remove(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelWasLoadedGuiReady);

            _reverting = false;
        }

        public void ReconcileStockTutorialGatesFromFinishedContractsAfterContractsSnapshot(string source)
        {
            var reconcileChanged = false;
            ApplyLiveStateWithLocalSuppression(() =>
            {
                reconcileChanged = ShareAchievementsSystem.Singleton.ReconcileStockTutorialGatesFromFinishedContracts(source);
            });
            // Reconcile ran under local suppression (no intents). Push gate nodes only when we actually advanced stock
            // progress — unconditional catch-up after every contracts snapshot caused Achievements intent/snapshot churn.
            if (reconcileChanged)
            {
                PublishTutorialGateProgressCatchup($"{source}:CatchUp");
            }
        }

        /// <summary>
        /// Sends current serialized state for stock tutorial/orbit gate nodes so the server canonical achievements
        /// domain converges with the lock holder after reconcile or snapshot apply.
        /// </summary>
        internal void PublishTutorialGateProgressCatchup(string reasonPrefix)
        {
            if (ProgressTracking.Instance == null || !PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                return;
            }

            foreach (var id in ShareAchievementsSystem.StockTutorialGateNodeIdsForCatchup)
            {
                var node = ShareAchievementsSystem.TryResolveStockTutorialGateNode(id);
                if (node == null || (!node.IsReached && !node.IsComplete))
                {
                    continue;
                }

                TrySendAchievementPayloadForResolvedNode(node, $"{reasonPrefix}:{id}");
            }
        }

        private void TrySendAchievementPayloadForResolvedNode(ProgressNode foundNode, string reason)
        {
            if (foundNode == null)
            {
                return;
            }

            var configNode = ConvertAchievementToConfigNode(foundNode);
            if (configNode == null)
            {
                return;
            }

            SendLocalPayload(
                new AchievementsPayload
                {
                    Items = new[]
                    {
                        new AchievementSnapshotInfo
                        {
                            Id = foundNode.Id,
                            Data = configNode.Serialize()
                        }
                    }
                },
                reason);
        }

        private void OnProgressReached(ProgressNode progressNode)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            TrySendAchievementPayload(progressNode);
            LunaLog.Log($"Achievement reached: {progressNode.Id}");
        }

        private void OnProgressComplete(ProgressNode progressNode)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            TrySendAchievementPayload(progressNode);
            LunaLog.Log($"Achievement completed: {progressNode.Id}");
        }

        private void OnProgressAchieved(ProgressNode progressNode)
        {
            // Stock fires too often (speed/distance records); intentionally no intent flood.
        }

        private void TrySendAchievementPayload(ProgressNode achievement)
        {
            if (ProgressTracking.Instance == null)
            {
                return;
            }

            var foundNode = ProgressTracking.Instance.FindNode(achievement.Id);
            if (foundNode == null)
            {
                var traverse = new Traverse(achievement).Field<CelestialBody>("body");
                var body = traverse.Value ? traverse.Value.name : null;
                if (body != null)
                {
                    foundNode = ProgressTracking.Instance.FindNode(body);
                }
            }

            if (foundNode == null)
            {
                return;
            }

            var configNode = ConvertAchievementToConfigNode(foundNode);
            if (configNode == null)
            {
                return;
            }

            SendLocalPayload(
                new AchievementsPayload
                {
                    Items = new[]
                    {
                        new AchievementSnapshotInfo
                        {
                            Id = foundNode.Id,
                            Data = configNode.Serialize()
                        }
                    }
                },
                $"AchievementUpdate:{foundNode.Id}");
        }

        private static ConfigNode ConvertAchievementToConfigNode(ProgressNode achievement)
        {
            var configNode = new ConfigNode(achievement.Id);
            try
            {
                achievement.Save(configNode);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving achievement: {e}");
                return null;
            }

            return configNode;
        }

        private void OnRevertingToLaunch()
        {
            _reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnReturningToEditor(EditorFacility data)
        {
            _reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnLevelWasLoadedGuiReady(GameScenes data)
        {
            if (!_reverting)
            {
                return;
            }

            _reverting = false;
            StopIgnoringLocalEvents(true);
        }

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, AchievementsPayload payload)
        {
            _pendingAchievements = payload?.Items ?? Array.Empty<AchievementSnapshotInfo>();
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

            var achievementItems = _pendingAchievements.Where(value => value != null && value.Data.Length > 0).ToArray();
            _pendingAchievements = null;

            var needCatchupFromMutation = ShareAchievementsSystem.Singleton.ApplyAchievementSnapshotItems(
                achievementItems,
                "PersistentSyncSnapshotApply");
            var needCatchupFromSparseSnapshot = ShareAchievementsSystem.Singleton
                .StockTutorialGateProgressMissingFromAppliedSnapshot(achievementItems);

            if (needCatchupFromMutation || needCatchupFromSparseSnapshot)
            {
                PublishTutorialGateProgressCatchup("PersistentSyncSnapshotApply:PostAchievementMerge");
            }

            // Progression offer mint keys off ProgressTracking/achievement state. Same ordering concern as
            // ShareContractsEvents.ContractOffered: if contracts Replenish ran before this snapshot, stock may have
            // missed newly-unlocked tiers. Mirror ShareContractsEvents.LockAcquire: queue deferred refresh, sync
            // generateContractIterations policy, then run controlled Replenish immediately when gates allow.
            var share = ShareContractsSystem.Singleton;
            if (share != null)
            {
                share.RequestControlledStockContractRefresh("PersistentSyncSnapshotApply:AfterAchievementsFlush");
                share.ApplyStockContractMutationPolicy("PersistentSyncSnapshotApply:AfterAchievementsFlush");
                share.ReplenishStockOffersAfterPersistentSnapshotApply("PersistentSyncSnapshotApply:AfterAchievementsFlush");
            }

            return PersistentSyncApplyOutcome.Applied;
        }
    }
}
