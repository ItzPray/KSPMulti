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

        /// <summary>
        /// One-shot upload of the full local achievement tree when the applied snapshot has fewer rows than local
        /// ProgressTracking (server canonical was sparse). Avoids repeating the old "infer missing from wire" storm.
        /// </summary>
        private bool _postedAchievementTreeHydrationIntent;

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
            _postedAchievementTreeHydrationIntent = false;
        }

        public void ReconcileStockTutorialGatesFromFinishedContractsAfterContractsSnapshot(string source)
        {
            var reconcileChanged = false;
            var changedSnapshotRootIds = Array.Empty<string>();
            ApplyLiveStateWithLocalSuppression(() =>
            {
                reconcileChanged = ShareAchievementsSystem.Singleton.ReconcileStockTutorialGatesFromFinishedContracts(
                    source,
                    out changedSnapshotRootIds);
            });
            // Reconcile ran under local suppression (no intents). Push only the top-level ProgressTracking roots that
            // actually changed so server canonical follows stock save shape without a hard-coded tutorial gate list.
            if (reconcileChanged)
            {
                PublishAchievementSnapshotRootCatchup(changedSnapshotRootIds, $"{source}:CatchUp");
            }
        }

        /// <summary>
        /// Sends serialized top-level ProgressTracking rows after completed contract parameters semantically repaired
        /// live achievement milestones under local event suppression.
        /// </summary>
        internal void PublishAchievementSnapshotRootCatchup(string[] snapshotRootIds, string reasonPrefix)
        {
            if (ProgressTracking.Instance == null || !PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                return;
            }

            foreach (var id in snapshotRootIds ?? Array.Empty<string>())
            {
                var node = ProgressTracking.Instance.FindNode(id);
                if (node == null || (!node.IsReached && !node.IsComplete))
                {
                    continue;
                }

                TrySendAchievementPayloadForResolvedNode(node, $"{reasonPrefix}:root={node.Id}");
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

            ShareAchievementsSystem.Singleton.ApplyAchievementSnapshotItems(
                achievementItems,
                "PersistentSyncSnapshotApply");

            // When the server snapshot carries fewer achievement rows than the local ProgressTracking tree, merge once
            // uploads the full wire envelope so dedicated-server canonical state can grow to parity (same idea as
            // accumulating Tech nodes from intents). This is gated to a single intent per domain lifecycle — not a
            // continuous "missing on wire" heuristic.
            TryPostAchievementTreeHydrationAfterSnapshot(achievementItems);

            // Do not publish tutorial-gate "catch-up" intents here. Unconditional catch-up after every contracts snapshot
            // caused intent↔broadcast storms. Gates align via authoritative snapshots + merge; contracts snapshot replace
            // calls ReconcileStockTutorialGatesFromFinishedContractsAfterContractsSnapshot when completed contract
            // ProgressTrackingParameter rows imply stock milestones.

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

        private void TryPostAchievementTreeHydrationAfterSnapshot(AchievementSnapshotInfo[] appliedSnapshotItems)
        {
            if (_postedAchievementTreeHydrationIntent || _reverting)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                return;
            }

            if (ProgressTracking.Instance == null)
            {
                return;
            }

            if (!TryBuildLocalAuditPayload(out var localFull, out _))
            {
                return;
            }

            var localCount = localFull.Items?.Length ?? 0;
            if (localCount == 0)
            {
                return;
            }

            var appliedCount = appliedSnapshotItems?.Length ?? 0;
            if (appliedCount >= localCount)
            {
                return;
            }

            var system = PersistentSyncSystem.Singleton;
            if (system == null)
            {
                return;
            }

            _postedAchievementTreeHydrationIntent = true;
            // SendLocalPayload refuses until Reconciler.MarkApplied sets HasInitialSnapshot — but MarkApplied runs only
            // after this FlushPendingState returns. Tree hydration would be dropped every time; send intent directly.
            PersistentSyncSystem.SendIntent(
                DomainId,
                system.GetKnownRevision(DomainId),
                localFull,
                $"AchievementTreeHydration:SnapshotRows={appliedCount}LocalRows={localCount}");
            LunaLog.Log(
                $"[KSPMP] Achievements: one-shot tree hydration intent (snapshot rows={appliedCount}, local rows={localCount})");
        }

        protected override bool TryBuildLocalAuditPayload(out AchievementsPayload payload, out string unavailableReason)
        {
            payload = new AchievementsPayload { Items = Array.Empty<AchievementSnapshotInfo>() };
            if (ProgressTracking.Instance == null)
            {
                unavailableReason = "ProgressTracking.Instance is null";
                return false;
            }

            var tree = ProgressTracking.Instance.achievementTree;
            var items = new System.Collections.Generic.List<AchievementSnapshotInfo>();
            for (var i = 0; i < tree.Count; i++)
            {
                var node = tree[i];
                if (node == null || string.IsNullOrEmpty(node.Id))
                {
                    continue;
                }

                var cn = ConvertAchievementToConfigNode(node);
                if (cn == null)
                {
                    continue;
                }

                items.Add(new AchievementSnapshotInfo { Id = node.Id, Data = cn.Serialize() });
            }

            items.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            payload.Items = items.ToArray();
            unavailableReason = null;
            return true;
        }
    }
}
