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
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
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

        private ShareStrategyEvents ShareStrategiesEvents { get; } = new ShareStrategyEvents();

        //BailoutGrand - Exchange funds for reputation; researchIPsellout - Exchange funds for science;
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

            StrategyEvent.onStrategyActivated.Add(ShareStrategiesEvents.StrategyActivated);
            StrategyEvent.onStrategyDeactivated.Add(ShareStrategiesEvents.StrategyDeactivated);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            StrategyEvent.onStrategyActivated.Remove(ShareStrategiesEvents.StrategyActivated);
            StrategyEvent.onStrategyDeactivated.Remove(ShareStrategiesEvents.StrategyDeactivated);
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

        public bool ApplyStrategySnapshot(StrategySnapshotInfo strategyInfo, string source, bool refreshUi)
        {
            var incomingStrategyNode = ShareStrategyMessageHandler.ConvertByteArrayToConfigNode(strategyInfo.Data, strategyInfo.Data.Length);
            if (incomingStrategyNode == null) return false;
            var incomingStrategyFactor = float.Parse(incomingStrategyNode.GetValue("factor"), CultureInfo.InvariantCulture);
            var incomingStrategyIsActive = bool.Parse(incomingStrategyNode.GetValue("isActive"));

            StartIgnoringEvents();
            ShareFundsSystem.Singleton.StartIgnoringEvents();
            ShareScienceSystem.Singleton.StartIgnoringEvents();
            ShareReputationSystem.Singleton.StartIgnoringEvents();
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
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while applying strategy snapshot {strategyInfo.Name} from {source}: {e}");
                return false;
            }
            finally
            {
                ShareFundsSystem.Singleton.StopIgnoringEvents(true);
                ShareScienceSystem.Singleton.StopIgnoringEvents(true);
                ShareReputationSystem.Singleton.StopIgnoringEvents(true);
                StopIgnoringEvents();
            }

            if (refreshUi)
            {
                RefreshStrategyUiAdapters(source);
            }

            return true;
        }
    }
}

