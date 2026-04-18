using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareUpgradeableFacilities
{
    public class ShareUpgradeableFacilitiesSystem : ShareProgressBaseSystem<ShareUpgradeableFacilitiesSystem, ShareUpgradeableFacilitiesMessageSender, ShareUpgradeableFacilitiesMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareUpgradeableFacilitiesSystem);

        private ShareUpgradeableFacilitiesEvents ShareUpgradeableFacilitiesEvents { get; } = new ShareUpgradeableFacilitiesEvents();

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.UpgradeableFacilities,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            // Upgrading fires before the facility's level is committed; intents then match the old server
            // level and are dropped as no-ops. Upgraded carries the final FacilityLevel we must persist.
            GameEvents.OnKSCFacilityUpgraded.Add(ShareUpgradeableFacilitiesEvents.FacilityUpgraded);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.OnKSCFacilityUpgraded.Remove(ShareUpgradeableFacilitiesEvents.FacilityUpgraded);
        }
    }
}
