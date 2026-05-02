using LmpClient.Events;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using Guid = System.Guid;

namespace LmpClient.Systems.ShareFunds
{
    public class ShareFundsSystem : ShareProgressBaseSystem<ShareFundsSystem, ShareFundsMessageSender, ShareFundsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareFundsSystem);

        private double _lastFunds;

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Funds,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        public bool Reverting { get; set; }

        public Tuple<Guid, float> CurrentShipCost { get; set; }

        protected override void OnEnabled()
        {
            base.OnEnabled();
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            _lastFunds = 0;
            Reverting = false;
        }

        /// <summary>
        /// Baseline for <see cref="ShareProgressBaseSystem.StartIgnoringEvents"/> on this system (legacy inbound
        /// ShareProgress messages and peer suppression during other domains’ applies). Scalar Persistent Sync
        /// revert/snapshot echo suppression uses <see cref="FundsPersistentSyncClientDomain"/> instead.
        /// </summary>
        public override void SaveState()
        {
            base.SaveState();
            _lastFunds = Funding.Instance.Funds;
        }

        /// <inheritdoc cref="SaveState"/>
        public override void RestoreState()
        {
            base.RestoreState();
            Funding.Instance.SetFunds(_lastFunds, TransactionReasons.None);
        }

    }
}
