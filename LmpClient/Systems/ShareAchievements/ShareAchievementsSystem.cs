using HarmonyLib;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Linq;

namespace LmpClient.Systems.ShareAchievements
{
    public class ShareAchievementsSystem : ShareProgressBaseSystem<ShareAchievementsSystem, ShareAchievementsMessageSender, ShareAchievementsMessageHandler>
    {
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

        public void ApplyAchievementSnapshotTree(ConfigNode snapshotTree, string source)
        {
            if (snapshotTree == null)
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
                ProgressTracking.Instance.achievementTree.Load(snapshotTree);
                LunaLog.Log($"Achievements snapshot applied from {source}");
            }
        }
    }
}
