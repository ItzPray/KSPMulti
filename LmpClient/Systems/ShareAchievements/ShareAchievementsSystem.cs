using Contracts;
using FinePrint.Contracts.Parameters;
using FinePrint.Utilities;
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
        private static readonly HashSet<string> ProgressParameterResolveWarningsLogged =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Stock career gates (tutorial + first orbit) used for <see cref="ReconcileStockTutorialGatesFromFinishedContracts"/>
        /// and for publishing catch-up intents so the server <c>ProgressTracking</c> scenario matches the client.
        /// </summary>
        public static readonly string[] StockTutorialGateNodeIdsForCatchup =
        {
            "FirstLaunch",
            "ReachedSpace",
            "Orbit",
        };

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
            ProgressParameterResolveWarningsLogged.Clear();
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

                            // Monotonic: restore milestones that Load() dropped vs pre-merge local state or incoming bytes.
                            // Do not set mergeRepaired for this path — when Load regresses flags every snapshot re-apply,
                            // needReach/needComplete used to fire forever → catch-up intent →
                            // broadcast spam (FirstLaunch) even though revisions already converged.
                            var targetReached = wasReached || incomingTemplate.IsReached;
                            var targetComplete = wasComplete || incomingTemplate.IsComplete;
                            if (targetReached && !live.IsReached)
                            {
                                live.Reach();
                            }

                            if (targetComplete && !live.IsComplete)
                            {
                                live.Complete();
                            }

                            if (ApplyProgressFlagsFromCfgTree(live, achievementCfg))
                            {
                                mergeRepaired = true;
                            }

                            // True progress from wire only (snapshot introduced milestones we did not already have locally).
                            if ((incomingTemplate.IsReached && !wasReached) || (incomingTemplate.IsComplete && !wasComplete))
                            {
                                mergeRepaired = true;
                            }
                        }
                        else
                        {
                            var incoming = new ProgressNode(id, false);
                            incoming.Load(achievementCfg);
                            var cfgFlagsRepairedIncoming = ApplyProgressFlagsFromCfgTree(incoming, achievementCfg);
                            tree.AddNode(incoming);
                            if (incoming.IsReached || incoming.IsComplete || cfgFlagsRepairedIncoming)
                            {
                                mergeRepaired = true;
                            }
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
        /// Aligns stock ProgressTree gates with completed <see cref="ProgressTrackingParameter"/> milestones from
        /// <see cref="ContractSystem.ContractsFinished"/> when achievement snapshot / in-place Load never advanced
        /// <see cref="ProgressNode.IsReached"/>/<see cref="ProgressNode.IsComplete"/> (common after MP reconnect).
        /// Must run while authoritative contracts are already in <see cref="ContractSystem"/> — typically immediately
        /// after <c>ReplaceContractsFromSnapshot</c>, before controlled stock replenish.
        /// </summary>
        public bool ReconcileStockTutorialGatesFromFinishedContracts(string source)
        {
            return ReconcileStockTutorialGatesFromFinishedContracts(source, out _);
        }

        public bool ReconcileStockTutorialGatesFromFinishedContracts(string source, out string[] changedSnapshotRootIds)
        {
            if (ContractSystem.Instance == null || ProgressTracking.Instance == null)
            {
                changedSnapshotRootIds = Array.Empty<string>();
                return false;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                changedSnapshotRootIds = Array.Empty<string>();
                return false;
            }

            var any = false;
            var changedRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in ContractSystem.Instance.ContractsFinished.ToArray())
            {
                if (contract == null || contract.ContractState != Contract.State.Completed)
                {
                    continue;
                }

                foreach (var parameter in EnumerateCompletedProgressTrackingParameters(contract))
                {
                    if (!TryResolveProgressTrackingParameterNode(parameter, out var node, out var diagnostic))
                    {
                        LogProgressParameterResolveWarningOnce(contract, parameter, source, diagnostic);
                        continue;
                    }

                    if (EnsureProgressNodeReachedAndComplete(node))
                    {
                        any = true;
                        var root = TryResolveProgressNodeSnapshotRoot(node);
                        if (root != null && !string.IsNullOrEmpty(root.Id))
                        {
                            changedRoots.Add(root.Id);
                        }
                    }
                }
            }

            changedSnapshotRootIds = changedRoots.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return any;
        }

        private static IEnumerable<ProgressTrackingParameter> EnumerateCompletedProgressTrackingParameters(Contract contract)
        {
            if (contract == null || contract.AllParameters == null)
            {
                yield break;
            }

            foreach (var parameter in contract.AllParameters)
            {
                if (parameter is ProgressTrackingParameter progressParameter &&
                    progressParameter.State == ParameterState.Complete)
                {
                    yield return progressParameter;
                }
            }
        }

        private static void LogProgressParameterResolveWarningOnce(
            Contract contract,
            ProgressTrackingParameter parameter,
            string source,
            string diagnostic)
        {
            var key = string.Concat(
                contract?.ContractGuid.ToString("N") ?? "<no-guid>",
                "|",
                GetProgressParameterTargetType(parameter),
                "|",
                GetProgressParameterTargetBodyName(parameter),
                "|",
                diagnostic ?? string.Empty);
            if (!ProgressParameterResolveWarningsLogged.Add(key))
            {
                return;
            }

            LunaLog.Log(
                $"[KSPMP] Achievements: skipped completed ProgressTrackingParameter source={source} " +
                $"contractType={contract?.GetType().Name ?? "<null>"} guid={contract?.ContractGuid.ToString() ?? "<none>"} " +
                $"targetType={GetProgressParameterTargetType(parameter)} targetBody={GetProgressParameterTargetBodyName(parameter)} " +
                $"state={parameter?.State.ToString() ?? "<null>"} diagnostic={diagnostic ?? "<none>"}");
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

        private static bool EnsureProgressNodeReachedAndComplete(ProgressNode node)
        {
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

        internal static bool TryResolveProgressTrackingParameterNode(
            ProgressTrackingParameter parameter,
            out ProgressNode node,
            out string diagnostic)
        {
            node = null;
            diagnostic = null;
            if (ProgressTracking.Instance == null)
            {
                diagnostic = "ProgressTracking.Instance is null";
                return false;
            }

            if (parameter == null)
            {
                diagnostic = "parameter is null";
                return false;
            }

            var milestone = parameter.milestone;
            if (milestone == null)
            {
                diagnostic = "milestone is null";
                return false;
            }

            var targetType = milestone.type;
            if (targetType == ProgressType.NULL)
            {
                diagnostic = "targetType=NULL";
                return false;
            }

            var rootCandidates = EnumerateMilestoneRootCandidates(milestone).ToList();
            foreach (var root in rootCandidates)
            {
                var hit = FindDescendantProgressNode(root, candidate => ProgressNodeMatchesMilestone(candidate, milestone));
                if (hit != null)
                {
                    node = hit;
                    diagnostic = "resolved";
                    return true;
                }
            }

            diagnostic =
                $"unresolved targetType={targetType} targetBody={GetProgressParameterTargetBodyName(parameter)} " +
                $"candidateRoots={string.Join(",", rootCandidates.Select(x => x?.Id ?? "<null>").Distinct().ToArray())}";
            return false;
        }

        internal static IEnumerable<string> EnumerateFinishedContractProgressParameterDiagnosticLines(int maxRows)
        {
            var cs = ContractSystem.Instance;
            if (cs?.ContractsFinished == null)
            {
                yield return "progressParam contractSystemFinished=null";
                yield break;
            }

            var shown = 0;
            foreach (var contract in cs.ContractsFinished.ToArray())
            {
                if (contract == null || contract.ContractState != Contract.State.Completed)
                {
                    continue;
                }

                foreach (var parameter in EnumerateCompletedProgressTrackingParameters(contract))
                {
                    if (shown++ >= maxRows)
                    {
                        yield break;
                    }

                    var resolved = TryResolveProgressTrackingParameterNode(parameter, out var node, out var diagnostic);
                    var root = resolved ? TryResolveProgressNodeSnapshotRoot(node) : null;
                    yield return
                        $"progressParam contractType={contract.GetType().Name} guid={contract.ContractGuid} " +
                        $"targetType={GetProgressParameterTargetType(parameter)} targetBody={GetProgressParameterTargetBodyName(parameter)} " +
                        $"state={parameter.State} resolved={resolved} node={(node == null ? "<none>" : node.Id)} " +
                        $"root={(root == null ? "<none>" : root.Id)} diagnostic={SanitizeDiagnosticValue(diagnostic)}";
                }
            }

            if (shown == 0)
            {
                yield return "progressParam completedRows=0";
            }
        }

        private static IEnumerable<ProgressNode> EnumerateMilestoneRootCandidates(ProgressMilestone milestone)
        {
            if (milestone == null || ProgressTracking.Instance == null)
            {
                yield break;
            }

            var yielded = new HashSet<ProgressNode>();
            if (milestone.progress != null && yielded.Add(milestone.progress))
            {
                yield return milestone.progress;
            }

            var body = milestone.body;
            if (body != null)
            {
                foreach (var name in new[] { body.name, body.bodyName })
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var bodyRoot = ProgressTracking.Instance.FindNode(name);
                    if (bodyRoot != null && yielded.Add(bodyRoot))
                    {
                        yield return bodyRoot;
                    }
                }
            }

            if (milestone.bodySensitive && yielded.Count > 0)
            {
                yield break;
            }

            var tree = ProgressTracking.Instance.achievementTree;
            if (tree == null)
            {
                yield break;
            }

            for (var i = 0; i < tree.Count; i++)
            {
                var root = tree[i];
                if (root != null && yielded.Add(root))
                {
                    yield return root;
                }
            }
        }

        private static bool ProgressNodeMatchesMilestone(ProgressNode node, ProgressMilestone milestone)
        {
            if (node == null || milestone == null)
            {
                return false;
            }

            switch (milestone.type)
            {
                case ProgressType.ALTITUDERECORD:
                    return node is KSPAchievements.RecordsAltitude;
                case ProgressType.BASECONSTRUCTION:
                    return node is KSPAchievements.BaseConstruction;
                case ProgressType.CREWRECOVERY:
                    return node is KSPAchievements.CrewRecovery;
                case ProgressType.DEPTHRECORD:
                    return node is KSPAchievements.RecordsDepth;
                case ProgressType.DISTANCERECORD:
                    return node is KSPAchievements.RecordsDistance;
                case ProgressType.DOCKING:
                    return node is KSPAchievements.Docking;
                case ProgressType.ESCAPE:
                    return node is KSPAchievements.CelestialBodyEscape;
                case ProgressType.FIRSTLAUNCH:
                    return node is KSPAchievements.FirstLaunch;
                case ProgressType.FLAGPLANT:
                    return node is KSPAchievements.FlagPlant;
                case ProgressType.FLIGHT:
                    return node is KSPAchievements.CelestialBodyFlight;
                case ProgressType.FLYBY:
                    return node is KSPAchievements.CelestialBodyFlyby;
                case ProgressType.LANDING:
                    return node is KSPAchievements.CelestialBodyLanding;
                case ProgressType.ORBIT:
                    return node is KSPAchievements.CelestialBodyOrbit;
                case ProgressType.POINTOFINTEREST:
                    return node is KSPAchievements.PointOfInterest;
                case ProgressType.REACHSPACE:
                    return node is KSPAchievements.ReachSpace;
                case ProgressType.RENDEZVOUS:
                    return node is KSPAchievements.Rendezvous;
                case ProgressType.SCIENCE:
                    return node is KSPAchievements.CelestialBodyScience;
                case ProgressType.SPACEWALK:
                    return node is KSPAchievements.Spacewalk;
                case ProgressType.SPEEDRECORD:
                    return node is KSPAchievements.RecordsSpeed;
                case ProgressType.SPLASHDOWN:
                    return node is KSPAchievements.CelestialBodySplashdown;
                case ProgressType.STATIONCONSTRUCTION:
                    return node is KSPAchievements.StationConstruction;
                case ProgressType.SUBORBIT:
                    return node is KSPAchievements.CelestialBodySuborbit;
                case ProgressType.SURFACEEVA:
                    return node is KSPAchievements.SurfaceEVA;
                case ProgressType.FLYBYRETURN:
                case ProgressType.LANDINGRETURN:
                case ProgressType.ORBITRETURN:
                    return node is KSPAchievements.CelestialBodyReturn;
                default:
                    return false;
            }
        }

        private static ProgressNode FindDescendantProgressNode(ProgressNode root, Func<ProgressNode, bool> predicate)
        {
            if (root == null || predicate == null)
            {
                return null;
            }

            if (predicate(root))
            {
                return root;
            }

            foreach (var child in EnumerateChildProgressNodes(root))
            {
                var hit = FindDescendantProgressNode(child, predicate);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        internal static ProgressNode TryResolveProgressNodeSnapshotRoot(ProgressNode target)
        {
            if (ProgressTracking.Instance == null || target == null)
            {
                return null;
            }

            var tree = ProgressTracking.Instance.achievementTree;
            if (tree == null)
            {
                return null;
            }

            for (var i = 0; i < tree.Count; i++)
            {
                var root = tree[i];
                if (FindDescendantProgressNode(root, candidate => ReferenceEquals(candidate, target)) != null)
                {
                    return root;
                }
            }

            return null;
        }

        private static string GetProgressParameterTargetType(ProgressTrackingParameter parameter)
        {
            return parameter?.milestone == null ? "<none>" : parameter.milestone.type.ToString();
        }

        private static string GetProgressParameterTargetBodyName(ProgressTrackingParameter parameter)
        {
            var body = parameter?.milestone?.body;
            if (body == null)
            {
                return "<none>";
            }

            return !string.IsNullOrEmpty(body.name) ? body.name : body.bodyName ?? "<unnamed>";
        }

        private static string SanitizeDiagnosticValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }

        /// <summary>
        /// KSP's saved ProgressTracking cfg often records completed milestones as <c>completed = UT</c> without
        /// explicit reached/complete booleans. <see cref="ProgressNode.Load"/> does not reliably restore those
        /// booleans, but stock contract generation gates on <see cref="ProgressNode.IsReached"/> /
        /// <see cref="ProgressNode.IsComplete"/>. Replay the semantic flags from the cfg tree into the live tree.
        /// </summary>
        private static bool ApplyProgressFlagsFromCfgTree(ProgressNode live, ConfigNode cfg)
        {
            if (live == null || cfg == null)
            {
                return false;
            }

            var changed = false;
            if (CfgImpliesReachedOrComplete(cfg))
            {
                if (!live.IsReached)
                {
                    live.Reach();
                    changed = true;
                }

                if (!live.IsComplete)
                {
                    live.Complete();
                    changed = true;
                }
            }

            foreach (var childCfg in cfg.GetNodes())
            {
                if (childCfg == null || string.IsNullOrEmpty(childCfg.name))
                {
                    continue;
                }

                var childLive = FindDescendantProgressNodeById(live, childCfg.name);
                if (childLive != null && ApplyProgressFlagsFromCfgTree(childLive, childCfg))
                {
                    changed = true;
                }
            }

            return changed;
        }

        private static bool CfgImpliesReachedOrComplete(ConfigNode cfg)
        {
            return CfgHasTruthyValue(cfg, "reached") ||
                   CfgHasTruthyValue(cfg, "IsReached") ||
                   CfgHasTruthyValue(cfg, "complete") ||
                   CfgHasTruthyValue(cfg, "IsComplete") ||
                   CfgHasTruthyValue(cfg, "finished") ||
                   CfgHasTruthyValue(cfg, "unlocked") ||
                   CfgHasTruthyValue(cfg, "completedOnce") ||
                   cfg.HasValue("completed");
        }

        private static bool CfgHasTruthyValue(ConfigNode cfg, string key)
        {
            if (cfg == null || string.IsNullOrEmpty(key) || !cfg.HasValue(key))
            {
                return false;
            }

            var value = cfg.GetValue(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value.Trim(), out var boolValue))
            {
                return boolValue;
            }

            return int.TryParse(value.Trim(), out var intValue) && intValue != 0;
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

            var actualId = NormalizeStockTutorialGateNodeId(targetId);
            var direct = ProgressTracking.Instance.FindNode(actualId);
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

            return FindDescendantProgressNodeById(bodyRoot, actualId);
        }

        /// <summary>
        /// Resolves the top-level ProgressTracking row that owns a stock gate. Nested gates such as Kerbin/Orbit must
        /// be serialized as their owning body root so the dedicated server canonical matches stock save layout.
        /// </summary>
        internal static ProgressNode TryResolveStockTutorialGateSnapshotRoot(string targetId)
        {
            if (ProgressTracking.Instance == null || string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            var actualId = NormalizeStockTutorialGateNodeId(targetId);
            var tree = ProgressTracking.Instance.achievementTree;
            if (tree == null)
            {
                return null;
            }

            for (var i = 0; i < tree.Count; i++)
            {
                var root = tree[i];
                if (FindDescendantProgressNodeById(root, actualId) != null)
                {
                    return root;
                }
            }

            return null;
        }

        private static string NormalizeStockTutorialGateNodeId(string targetId)
        {
            return string.Equals(targetId, "ReachSpace", StringComparison.OrdinalIgnoreCase)
                ? "ReachedSpace"
                : targetId;
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

            var subtree = parent.Subtree;
            if (subtree != null)
            {
                for (var i = 0; i < subtree.Count; i++)
                {
                    var child = subtree[i];
                    if (child != null)
                    {
                        yield return child;
                    }
                }
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
