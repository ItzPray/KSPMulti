using HarmonyLib;
using LmpClient.Events;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.ShareTechnology;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareExperimentalParts
{
    public class ShareExperimentalPartsSystem : ShareProgressBaseSystem<ShareExperimentalPartsSystem, ShareExperimentalPartsMessageSender, ShareExperimentalPartsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareExperimentalPartsSystem);

        private ShareExperimentalPartsEvents ShareExperimentalPartsEvents { get; } = new ShareExperimentalPartsEvents();

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.ExperimentalParts,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            ExperimentalPartEvent.onExperimentalPartRemoved.Add(ShareExperimentalPartsEvents.ExperimentalPartRemoved);
            ExperimentalPartEvent.onExperimentalPartAdded.Add(ShareExperimentalPartsEvents.ExperimentalPartAdded);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            ExperimentalPartEvent.onExperimentalPartRemoved.Remove(ShareExperimentalPartsEvents.ExperimentalPartRemoved);
            ExperimentalPartEvent.onExperimentalPartAdded.Remove(ShareExperimentalPartsEvents.ExperimentalPartAdded);
        }

        public void ReplaceExperimentalPartsStock(Dictionary<AvailablePart, int> stock, string source)
        {
            Traverse.Create(ResearchAndDevelopment.Instance).Field("experimentalPartsStock").SetValue(stock);
            ShareTechnologySystem.Singleton.RefreshResearchAndDevelopmentPurchasesOnly(source);
            LunaLog.Log($"Experimental parts snapshot applied from {source} count={stock.Count}");
        }
    }
}
