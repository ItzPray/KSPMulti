using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncSystem : MessageSystem<PersistentSyncSystem, PersistentSyncMessageSender, PersistentSyncMessageHandler>
    {
        private static readonly PersistentSyncDomainId[] CareerDomains =
        {
            PersistentSyncDomainId.Funds,
            PersistentSyncDomainId.Science,
            PersistentSyncDomainId.Reputation
        };

        private static readonly PersistentSyncDomainId[] ScienceDomains =
        {
            PersistentSyncDomainId.Science
        };

        public override string SystemName { get; } = nameof(PersistentSyncSystem);

        public override int ExecutionOrder => -50;

        protected override ClientState EnableStage => ClientState.ScenariosSynced;

        public PersistentSyncReconciler Reconciler { get; } = new PersistentSyncReconciler();

        public Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain> Domains { get; } =
            new Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain>
            {
                [PersistentSyncDomainId.Funds] = new FundsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Science] = new SciencePersistentSyncClientDomain(),
                [PersistentSyncDomainId.Reputation] = new ReputationPersistentSyncClientDomain()
            };

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, FlushPendingState));
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneReady);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneReady);
            Reconciler.Reset(new PersistentSyncDomainId[0]);
        }

        public void StartInitialSync()
        {
            var requiredDomains = GetRequiredDomainsForCurrentGameMode().ToArray();
            Reconciler.Reset(requiredDomains);

            if (!requiredDomains.Any())
            {
                MainSystem.NetworkState = ClientState.PersistentStateSynced;
                return;
            }

            MessageSender.SendRequest(requiredDomains);
        }

        public long GetKnownRevision(PersistentSyncDomainId domainId)
        {
            return Reconciler.GetKnownRevision(domainId);
        }

        private static IEnumerable<PersistentSyncDomainId> GetRequiredDomainsForCurrentGameMode()
        {
            switch (SettingsSystem.ServerSettings.GameMode)
            {
                case GameMode.Career:
                    return CareerDomains;
                case GameMode.Science:
                    return ScienceDomains;
                default:
                    return new PersistentSyncDomainId[0];
            }
        }

        private void FlushPendingState()
        {
            Reconciler.RetryDeferredSnapshots();
            Reconciler.FlushPendingState();
        }

        private void OnSceneReady(GameScenes data)
        {
            Reconciler.RetryDeferredSnapshots();
            Reconciler.FlushPendingState();
        }
    }
}
