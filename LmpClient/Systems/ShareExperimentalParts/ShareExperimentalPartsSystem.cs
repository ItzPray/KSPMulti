using HarmonyLib;
using LmpClient.Events;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.Enums;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareExperimentalParts
{
    public class ShareExperimentalPartsSystem : ShareProgressBaseSystem<ShareExperimentalPartsSystem, ShareExperimentalPartsMessageSender, ShareExperimentalPartsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareExperimentalPartsSystem);

        private ShareExperimentalPartsEvents ShareExperimentalPartsEvents { get; } = new ShareExperimentalPartsEvents();

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (!CurrentGameModeIsRelevant) return;

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
            ShareTechnologySystem.Singleton.RefreshResearchAndDevelopmentUiAdapters(source);
            LunaLog.Log($"Experimental parts snapshot applied from {source} count={stock.Count}");
        }
    }
}
