using LmpClient.Events;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareReputation
{
    public class ShareReputationSystem : ShareProgressBaseSystem<ShareReputationSystem, ShareReputationMessageSender, ShareReputationMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareReputationSystem);

        private float _lastReputation;

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Reputation,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        public bool Reverting { get; set; }

        protected override void OnEnabled()
        {
            base.OnEnabled();
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            _lastReputation = 0;
            Reverting = false;
        }

        /// <summary>
        /// Baseline for <see cref="ShareProgressBaseSystem.StartIgnoringEvents"/> on this system (legacy inbound
        /// ShareProgress messages and peer suppression). Persistent Sync scalar suppression uses
        /// <see cref="ReputationPersistentSyncClientDomain"/>.
        /// </summary>
        public override void SaveState()
        {
            base.SaveState();
            _lastReputation = Reputation.Instance.reputation;
        }

        /// <inheritdoc cref="SaveState"/>
        public override void RestoreState()
        {
            base.RestoreState();
            Reputation.Instance.SetReputation(_lastReputation, TransactionReasons.None);
        }

    }
}
