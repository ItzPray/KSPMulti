using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.ShareTechnology;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Collections.Generic;

namespace LmpClient.Systems.SharePurchaseParts
{
    public class SharePurchasePartsSystem : ShareProgressBaseSystem<SharePurchasePartsSystem, SharePurchasePartsMessageSender, SharePurchasePartsMessageHandler>
    {
        public override string SystemName { get; } = nameof(SharePurchasePartsSystem);

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.PartPurchases,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            // OnPartPurchased: PartPurchasesPersistentSyncClientDomain
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
        }

        public void RefreshPurchaseUiAdapters(string source)
        {
            ShareTechnologySystem.Singleton.RefreshResearchAndDevelopmentPurchasesOnly(source);
        }
    }
}
