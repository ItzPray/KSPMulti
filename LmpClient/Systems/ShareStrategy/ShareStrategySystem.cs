using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using KSP.UI.Screens;
using LmpClient.Events;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Strategies;
using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Systems.ShareStrategy
{
    public class ShareStrategySystem : ShareProgressBaseSystem<ShareStrategySystem, ShareStrategyMessageSender, ShareStrategyMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareStrategySystem);

        public readonly string[] OneTimeStrategies = { "BailoutGrant", "researchIPsellout" };

        protected override bool ShareSystemReady => StrategySystem.Instance != null && StrategySystem.Instance.Strategies.Count != 0 && Funding.Instance != null && ResearchAndDevelopment.Instance != null &&
                                                    Reputation.Instance != null && Time.timeSinceLevelLoad > 1f;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Strategy,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            // Strategy activation: StrategyPersistentSyncClientDomain
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
        }

        public void RefreshStrategyUiAdapters(string source)
        {
            if (Administration.Instance)
            {
                Administration.Instance.RedrawPanels();
            }

            // Do not fire GameEvents.Contract.onContractsListChanged: stock treats it as "contract DB changed"
            // and rebuilds default progression offers (tutorial-tier spam, new GUIDs) on top of synced MP state.
            TryRefreshMissionControlContractListUiOnly();
        }

        /// <summary>
        /// Refresh Mission Control list rows only — no global contract events (those re-trigger stock generation).
        /// </summary>
        private static void TryRefreshMissionControlContractListUiOnly()
        {
            if (MissionControl.Instance == null)
            {
                return;
            }

            try
            {
                var t = MissionControl.Instance.GetType();
                t.GetMethod("RebuildContractList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(MissionControl.Instance, null);
                t.GetMethod("RefreshUIControls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(MissionControl.Instance, null);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Mission Control UI refresh after strategy change failed: {e}");
            }
        }

        public bool TryApplyStrategySnapshotMutation(StrategySnapshotInfo strategyInfo, string source)
        {
            var incomingStrategyNode = ShareStrategyMessageHandler.ConvertByteArrayToConfigNode(strategyInfo.Data, strategyInfo.Data.Length);
            if (incomingStrategyNode == null)
            {
                return false;
            }

            var incomingStrategyFactor = float.Parse(incomingStrategyNode.GetValue("factor"), CultureInfo.InvariantCulture);
            var incomingStrategyIsActive = bool.Parse(incomingStrategyNode.GetValue("isActive"));

            try
            {
                var strategyIndex = StrategySystem.Instance.Strategies.FindIndex(s => s.Config.Name == strategyInfo.Name);
                if (strategyIndex == -1)
                {
                    return false;
                }

                StrategySystem.Instance.Strategies[strategyIndex].Factor = incomingStrategyFactor;
                if (incomingStrategyIsActive)
                {
                    StrategySystem.Instance.Strategies[strategyIndex].Activate();
                    LunaLog.Log($"Strategy snapshot applied from {source}: strategy activated: {strategyInfo.Name} - with factor: {incomingStrategyFactor}");
                }
                else
                {
                    StrategySystem.Instance.Strategies[strategyIndex].Deactivate();
                    LunaLog.Log($"Strategy snapshot applied from {source}: strategy deactivated: {strategyInfo.Name} - with factor: {incomingStrategyFactor}");
                }

                return true;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while applying strategy snapshot {strategyInfo.Name} from {source}: {e}");
                return false;
            }
        }

        public bool ApplyStrategySnapshot(StrategySnapshotInfo strategyInfo, string source, bool refreshUi)
        {
            StartIgnoringEvents();
            try
            {
                bool ok;
                using (PersistentSyncDomainSuppressionScope.Begin(
                    PersistentSyncEventSuppressorRegistry.Resolve(
                        PersistentSyncDomainNames.Funds,
                        PersistentSyncDomainNames.Science,
                        PersistentSyncDomainNames.Reputation),
                    restoreOldValueOnDispose: true))
                {
                    ok = TryApplyStrategySnapshotMutation(strategyInfo, source);
                }

                if (ok && refreshUi)
                {
                    RefreshStrategyUiAdapters(source);
                }

                return ok;
            }
            finally
            {
                StopIgnoringEvents();
            }
        }
    }
}

