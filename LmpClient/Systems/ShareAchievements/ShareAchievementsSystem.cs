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
        /// </summary>
        public void ApplyAchievementSnapshotItems(IEnumerable<AchievementSnapshotInfo> items, string source)
        {
            if (items == null || ProgressTracking.Instance == null)
            {
                return;
            }

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
                            // Mirror ShareAchievementsMessageHandler.AchievementUpdate: build a fresh node from the
                            // snapshot bytes so IsReached/IsComplete reflect the payload, then merge into the live node.
                            // In-place ProgressNode.Load on an existing tree entry does not reliably advance gate flags
                            // (e.g. FirstLaunch), which lets stock keep spawning tutorial offers we then suppress as
                            // duplicates of CONTRACT_FINISHED rows.
                            var incomingTemplate = new ProgressNode(id, false);
                            incomingTemplate.Load(achievementCfg);
                            var live = tree[achievementIndex];
                            live.Load(achievementCfg);
                            if (!live.IsReached && incomingTemplate.IsReached)
                            {
                                live.Reach();
                            }

                            if (!live.IsComplete && incomingTemplate.IsComplete)
                            {
                                live.Complete();
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

                var firstLaunch = ProgressTracking.Instance.FindNode("FirstLaunch");
                var firstLaunchDiag = firstLaunch == null
                    ? "FirstLaunch missing"
                    : $"FirstLaunch reached={firstLaunch.IsReached} complete={firstLaunch.IsComplete}";

                LunaLog.Log($"Achievements snapshot applied from {source}; {firstLaunchDiag}");
            }
        }

        /// <summary>
        /// Aligns selected stock ProgressTree gates with <see cref="ContractSystem.ContractsFinished"/> when tutorial
        /// contracts are <see cref="Contract.State.Completed"/> but achievement snapshot / in-place Load never advanced
        /// <see cref="ProgressNode.IsReached"/>/<see cref="ProgressNode.IsComplete"/> (common after MP reconnect).
        /// Must run while authoritative contracts are already in <see cref="ContractSystem"/> — typically immediately
        /// after <c>ReplaceContractsFromSnapshot</c>, before controlled stock replenish.
        /// </summary>
        public void ReconcileStockTutorialGatesFromFinishedContracts(string source)
        {
            if (ContractSystem.Instance == null || ProgressTracking.Instance == null)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Achievements))
            {
                return;
            }

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
                    EnsureProgressNodeReachedAndComplete(id);
                }
            }
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
        private static void EnsureProgressNodeReachedAndComplete(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || ProgressTracking.Instance == null)
            {
                return;
            }

            var node = ProgressTracking.Instance.FindNode(nodeId);
            if (node == null)
            {
                return;
            }

            if (!node.IsReached)
            {
                node.Reach();
            }

            if (!node.IsComplete)
            {
                node.Complete();
            }
        }
    }
}
