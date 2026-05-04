using Contracts;
using HarmonyLib;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpClient.Extensions;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.ShareAchievements
{
    public class ShareAchievementsSystem : ShareProgressBaseSystem<ShareAchievementsSystem, ShareAchievementsMessageSender, ShareAchievementsMessageHandler>
    {
        /// <summary>
        /// Early career ExplorationContract tutorials whose finished archive rows imply ProgressTracking
        /// gates stock uses before generating the next tier of Mission Control offers. Keys match
        /// <see cref="ShareContractsSystem.BuildRuntimeContractIdentityKey"/> or short type-name fallback.
        /// Ids are stock ProgressTree node names (see Squad Progression); unknown ids are skipped by
        /// <see cref="EnsureProgressNodeReachedAndComplete"/>.
        /// </summary>
        private static readonly Dictionary<string, string[]> TutorialProgressByContractKey =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExplorationContract\u001fLaunch our first vessel!"] = new[] { "FirstLaunch" },
                ["ExplorationContract\u001fEscape the atmosphere!"] = new[] { "ReachSpace" },
                ["ExplorationContract\u001fOrbit Kerbin!"] = new[] { "Orbit" },
                ["Contracts.ExplorationContract\u001fLaunch our first vessel!"] = new[] { "FirstLaunch" },
                ["Contracts.ExplorationContract\u001fEscape the atmosphere!"] = new[] { "ReachSpace" },
                ["Contracts.ExplorationContract\u001fOrbit Kerbin!"] = new[] { "Orbit" },
            };

        /// <summary>
        /// Stock career gates (tutorial + first orbit) used for <see cref="ReconcileStockTutorialGatesFromFinishedContracts"/>
        /// and for publishing catch-up intents so the server <c>ProgressTracking</c> scenario matches the client.
        /// </summary>
        public static readonly string[] StockTutorialGateNodeIdsForCatchup =
        {
            "FirstLaunch",
            "ReachSpace",
            "Orbit",
        };

        /// <summary>
        /// When the persistent-sync achievements snapshot omits stock tutorial/orbit gates that are already satisfied
        /// locally (e.g. migrated universe + server canonical map never received those rows), we must publish catch-up
        /// intents even though <see cref="ApplyAchievementSnapshotItems"/> did not call <c>Reach</c>/<c>Complete</c>.
        /// Compares only <b>presence</b> of ids in the snapshot (not serialized bytes) so steady-state broadcasts do
        /// not re-open the client↔server feedback loop.
        /// </summary>
        internal bool StockTutorialGateProgressMissingFromAppliedSnapshot(IEnumerable<AchievementSnapshotInfo> appliedItems)
        {
            if (ProgressTracking.Instance == null)
            {
                return false;
            }

            var idsPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in appliedItems ?? Enumerable.Empty<AchievementSnapshotInfo>())
            {
                if (item?.Data == null || item.Data.Length <= 0)
                {
                    continue;
                }

                var id = item.Id;
                try
                {
                    var achievementCfg = item.Data.DeserializeToConfigNode(item.Data.Length);
                    if (achievementCfg != null && !string.IsNullOrEmpty(achievementCfg.name))
                    {
                        id = achievementCfg.name;
                    }
                }
                catch
                {
                    // keep wire id
                }

                if (!string.IsNullOrEmpty(id))
                {
                    idsPresent.Add(id);
                }
            }

            foreach (var gateId in StockTutorialGateNodeIdsForCatchup)
            {
                var node = TryResolveStockTutorialGateNode(gateId);
                if (node == null || (!node.IsReached && !node.IsComplete))
                {
                    continue;
                }

                if (!idsPresent.Contains(gateId))
                {
                    return true;
                }
            }

            return false;
        }

        public override string SystemName { get; } = nameof(ShareAchievementsSystem);

        private ConfigNode _lastAchievements;

        protected override bool ShareSystemReady => ProgressTracking.Instance != null;

        /// <summary>
        /// Unused when <see cref="ShareProgressBaseSystem.UseSessionApplicabilityInsteadOfGameModeMask"/> is true; kept for API completeness.
        /// </summary>
        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Achievements,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        public bool Reverting { get; set; }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            // Achievement progress + revert: AchievementsPersistentSyncClientDomain
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            _lastAchievements = null;
            Reverting = false;
        }

        public override void SaveState()
        {
            base.SaveState();
            _lastAchievements = new ConfigNode();
            if (ProgressTracking.Instance) ProgressTracking.Instance.achievementTree.Save(_lastAchievements);
        }

        public override void RestoreState()
        {
            base.RestoreState();
            if (ProgressTracking.Instance == null)
            {
                //The instance will be null when reverting to editor so we must restore the old data into the proto
                //This happens because in ProgressTracking class, the KSPScenario attribute doesn't include 
                //GameScenes.Editor into it's values :(
                var achievementsScn = HighLogic.CurrentGame.scenarios.FirstOrDefault(s => s.moduleName == "ProgressTracking");
                var moduleValues = Traverse.Create(achievementsScn).Field<ConfigNode>("moduleValues").Value;

                var progressNode = moduleValues.GetNode("Progress");
                progressNode.ClearNodes();

                foreach (var node in _lastAchievements.GetNodes())
                {
                    progressNode.AddNode(node);
                }
            }
            else
            {
                ProgressTracking.Instance.achievementTree.Load(_lastAchievements);
            }
        }

        /// <summary>
        /// Applies a server-authoritative achievements snapshot by merging each deserialized subtree into the live
        /// <see cref="ProgressTracking.Instance.achievementTree"/> by id (same pattern as
        /// <see cref="ShareAchievementsMessageHandler"/> / <c>ProgressNode.Load</c>). A bare
        /// <see cref="ProgressTree.Load(ConfigNode)"/> on a synthetic root is unreliable for sparse/partial payloads
        /// and can leave progression gates like <c>FirstLaunch</c> out of sync with stock contract generation.
        /// Merges are <b>monotonic</b> for <see cref="ProgressNode.IsReached"/> / <see cref="ProgressNode.IsComplete"/> on
        /// existing nodes so a stale server row cannot erase milestones the client (or contracts finished) already
        /// satisfied — that regression gates stock to early-tier contract types (surveys, part tests) regardless of rep.
        /// </summary>
        /// <returns><see langword="true"/> when local progress was repaired (monotonic merge or contract reconcile);
        /// callers should push tutorial gate catch-up intents only in that case to avoid snapshot↔intent feedback loops.</returns>
        public bool ApplyAchievementSnapshotItems(IEnumerable<AchievementSnapshotInfo> items, string source)
        {
            if (items == null || ProgressTracking.Instance == null)
            {
                return false;
            }

            var mergeRepaired = false;

            using (PersistentSyncDomainSuppressionScope.Begin(
                PersistentSyncEventSuppressorRegistry.Resolve(
                    PersistentSyncDomainNames.Funds,
                    PersistentSyncDomainNames.Science,
                    PersistentSyncDomainNames.Reputation,
                    PersistentSyncDomainNames.Achievements),
                restoreOldValueOnDispose: true))
            {
                var tree = ProgressTracking.Instance.achievementTree;

                foreach (var item in items)
                {
                    if (item == null || item.Data == null || item.Data.Length <= 0)
                    {
                        continue;
                    }

                    ConfigNode achievementCfg;
                    try
                    {
                        achievementCfg = item.Data.DeserializeToConfigNode(item.Data.Length);
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[KSPMP] Achievement snapshot deserialize failed ({item.Id}): {e}");
                        continue;
                    }

                    if (achievementCfg == null)
                    {
                        continue;
                    }

                    var id = !string.IsNullOrEmpty(achievementCfg.name)
                        ? achievementCfg.name
                        : item.Id;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var achievementIndex = -1;
                    for (var i = 0; i < tree.Count; i++)
                    {
                        if (tree[i].Id != id)
                        {
                            continue;
                        }

                        achievementIndex = i;
                        break;
                    }

                    try
                    {
                        if (achievementIndex != -1)
                        {
                            // Build a fresh node from the snapshot bytes so IsReached/IsComplete reflect the payload,
                            // then merge into the live node (same idea as ShareAchievementsMessageHandler.AchievementUpdate).
                            //
                            // Monotonic rule: never let a stale server row regress local milestones. live.Load(cfg) can
                            // clear IsReached/IsComplete when the server ProgressTracking payload lags behind what the
                            // player already achieved (or behind CONTRACT_FINISHED). Stock contract generation keys off
                            // these gates — regressing them leaves the offer pool stuck on surveys / part tests even
                            // with high reputation.
                            var incomingTemplate = new ProgressNode(id, false);
                            incomingTemplate.Load(achievementCfg);
                            var live = tree[achievementIndex];
                            var wasReached = live.IsReached;
                            var wasComplete = live.IsComplete;
                            live.Load(achievementCfg);
                            var needReach = (wasReached || incomingTemplate.IsReached) && !live.IsReached;
                            var needComplete = (wasComplete || incomingTemplate.IsComplete) && !live.IsComplete;
                            if (needReach)
                            {
                                live.Reach();
                                mergeRepaired = true;
                            }

                            if (needComplete)
                            {
                                live.Complete();
                                mergeRepaired = true;
                            }
                        }
                        else
                        {
                            var incoming = new ProgressNode(id, false);
                            incoming.Load(achievementCfg);
                            tree.AddNode(incoming);
                        }
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[KSPMP] Achievement snapshot merge failed for '{id}': {e}");
                    }
                }

                // Re-apply known stock tutorial gates from CONTRACT_FINISHED after every merge so orbit/launch flags
                // stay aligned with authoritative contracts even when achievement rows were missing or stale above.
                var reconcileChanged = ReconcileStockTutorialGatesFromFinishedContracts(source + ":PostAchievementMerge");

                var firstLaunch = ProgressTracking.Instance.FindNode("FirstLaunch");
                var firstLaunchDiag = firstLaunch == null
                    ? "FirstLaunch missing"
                    : $"FirstLaunch reached={firstLaunch.IsReached} complete={firstLaunch.IsComplete}";

                LunaLog.Log($"Achievements snapshot applied from {source}; {firstLaunchDiag}");
                return mergeRepaired || reconcileChanged;
            }
        }

        /// <summary>
        /// Aligns selected stock ProgressTree gates with <see cref="ContractSystem.ContractsFinished"/> when tutorial
        /// contracts are <see cref="Contract.State.Completed"/> but achievement snapshot / in-place Load never advanced
        /// <see cref="ProgressNode.IsReached"/>/<see cref="ProgressNode.IsComplete"/> (common after MP reconnect).
        /// Must run while authoritative contracts are already in <see cref="ContractSystem"/> — typically immediately
        /// after <c>ReplaceContractsFromSnapshot</c>, before controlled stock replenish.
        /// </summary>
        public bool ReconcileStockTutorialGatesFromFinishedContracts(string source)
        {
            if (ContractSystem.Instance == null || ProgressTracking.Instance == null)
            {
                return false;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                return false;
            }

            var any = false;
            foreach (var contract in ContractSystem.Instance.ContractsFinished.ToArray())
            {
                if (contract == null || contract.ContractState != Contract.State.Completed)
                {
                    continue;
                }

                if (!TryGetStockTutorialProgressNodeIds(contract, out var nodeIds))
                {
                    continue;
                }

                foreach (var id in nodeIds)
                {
                    if (EnsureProgressNodeReachedAndComplete(id))
                    {
                        any = true;
                    }
                }
            }

            return any;
        }

        private static bool TryGetStockTutorialProgressNodeIds(Contract contract, out string[] nodeIds)
        {
            nodeIds = null;
            var title = ShareContractsSystem.NormalizeOfferTitleForDedupe(contract.Title);
            if (string.IsNullOrEmpty(title))
            {
                return false;
            }

            var fullKey = ShareContractsSystem.BuildRuntimeContractIdentityKey(contract);
            if (!string.IsNullOrEmpty(fullKey) && TutorialProgressByContractKey.TryGetValue(fullKey, out nodeIds))
            {
                return true;
            }

            var shortKey = string.Concat(contract.GetType().Name, "\u001f", title);
            return TutorialProgressByContractKey.TryGetValue(shortKey, out nodeIds);
        }

        /// <summary>Best-effort: missing <paramref name="nodeId"/> is ignored (wrong id for this KSP version).</summary>
        /// <returns><see langword="true"/> if <see cref="ProgressNode.Reach"/> or <see cref="ProgressNode.Complete"/> ran.</returns>
        private static bool EnsureProgressNodeReachedAndComplete(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || ProgressTracking.Instance == null)
            {
                return false;
            }

            var node = TryResolveStockTutorialGateNode(nodeId);
            if (node == null)
            {
                return false;
            }

            var changed = false;
            if (!node.IsReached)
            {
                node.Reach();
                changed = true;
            }

            if (!node.IsComplete)
            {
                node.Complete();
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Resolves a stock <see cref="ProgressNode"/> by id. Orbit-style milestones often live under the home
        /// body's subtree, where <see cref="ProgressTracking.FindNode(string)"/> does not find them — stock contract
        /// generation still consults those nodes, so we must reach them for reconcile/catch-up.
        /// </summary>
        internal static ProgressNode TryResolveStockTutorialGateNode(string targetId)
        {
            if (ProgressTracking.Instance == null || string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            var direct = ProgressTracking.Instance.FindNode(targetId);
            if (direct != null)
            {
                return direct;
            }

            var home = FetchHomeBodySafe();
            if (home == null)
            {
                return null;
            }

            // KSP ProgressTracking.FindNode is string-keyed (body name), not CelestialBody-overloaded in this target.
            var bodyRoot = ProgressTracking.Instance.FindNode(home.name)
                           ?? ProgressTracking.Instance.FindNode("Kerbin");
            if (bodyRoot == null)
            {
                return null;
            }

            return FindDescendantProgressNodeById(bodyRoot, targetId);
        }

        private static CelestialBody FetchHomeBodySafe()
        {
            try
            {
                var b = FlightGlobals.GetHomeBody();
                if (b != null)
                {
                    return b;
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static ProgressNode FindDescendantProgressNodeById(ProgressNode root, string targetId)
        {
            if (root == null || string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            if (string.Equals(root.Id, targetId, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            foreach (var child in EnumerateChildProgressNodes(root))
            {
                var hit = FindDescendantProgressNodeById(child, targetId);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        private static IEnumerable<ProgressNode> EnumerateChildProgressNodes(ProgressNode parent)
        {
            if (parent == null)
            {
                yield break;
            }

            foreach (var fieldName in new[] { "children", "_children", "childNodes", "nodes" })
            {
                object value;
                try
                {
                    value = Traverse.Create(parent).Field(fieldName).GetValue();
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (value is ProgressNode[] arr)
                {
                    foreach (var c in arr)
                    {
                        if (c != null)
                        {
                            yield return c;
                        }
                    }

                    yield break;
                }

                if (value is IList list)
                {
                    foreach (var item in list)
                    {
                        if (item is ProgressNode pn)
                        {
                            yield return pn;
                        }
                    }

                    yield break;
                }
            }
        }
    }
}
