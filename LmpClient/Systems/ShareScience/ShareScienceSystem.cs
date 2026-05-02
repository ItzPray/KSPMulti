using LmpClient.Events;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareScience
{
    public class ShareScienceSystem : ShareProgressBaseSystem<ShareScienceSystem, ShareScienceMessageSender, ShareScienceMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareScienceSystem);

        private float _lastScience;

        protected override bool ShareSystemReady => ResearchAndDevelopment.Instance != null;

        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Science,
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

            Reverting = false;
            _lastScience = 0;
        }

        /// <summary>
        /// Baseline for <see cref="ShareProgressBaseSystem.StartIgnoringEvents"/> on this system (legacy inbound
        /// ShareProgress messages and peer suppression). Persistent Sync scalar suppression uses
        /// <see cref="SciencePersistentSyncClientDomain"/>.
        /// </summary>
        public override void SaveState()
        {
            base.SaveState();
            _lastScience = ResearchAndDevelopment.Instance.Science;
        }

        /// <inheritdoc cref="SaveState"/>
        public override void RestoreState()
        {
            base.RestoreState();
            ResearchAndDevelopment.Instance.SetScience(_lastScience, TransactionReasons.None);
        }

    }
}
