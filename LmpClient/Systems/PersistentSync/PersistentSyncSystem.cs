using LmpClient;
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
        public override string SystemName { get; } = nameof(PersistentSyncSystem);

        public override int ExecutionOrder => -50;

        protected override ClientState EnableStage => ClientState.ScenariosSynced;

        public PersistentSyncReconciler Reconciler { get; } = new PersistentSyncReconciler();

        public Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain> Domains { get; } =
            new Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain>
            {
                [PersistentSyncDomainId.Funds] = new FundsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Science] = new SciencePersistentSyncClientDomain(),
                [PersistentSyncDomainId.Reputation] = new ReputationPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Strategy] = new StrategyPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Achievements] = new AchievementsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.ScienceSubjects] = new ScienceSubjectsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Technology] = new TechnologyPersistentSyncClientDomain(),
                [PersistentSyncDomainId.ExperimentalParts] = new ExperimentalPartsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.PartPurchases] = new PartPurchasesPersistentSyncClientDomain(),
                [PersistentSyncDomainId.UpgradeableFacilities] = new UpgradeableFacilitiesPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Contracts] = new ContractsPersistentSyncClientDomain()
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
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var requiredDomains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();
            Reconciler.Reset(requiredDomains);

            if (!requiredDomains.Any())
            {
                LunaLog.Log("[PersistentSync] StartInitialSync no required domains for current game mode; advancing to PersistentStateSynced");
                MainSystem.NetworkState = ClientState.PersistentStateSynced;
                return;
            }

            var domainList = string.Join(",", requiredDomains.Select(d => d.ToString()));
            LunaLog.Log($"[PersistentSync] StartInitialSync requesting snapshots for domains=[{domainList}]");
            MessageSender.SendRequest(requiredDomains);
        }

        /// <summary>
        /// Single place to consult whether required persistent-sync domains have their initial snapshots applied.
        /// </summary>
        public bool IsPersistentSnapshotPhaseCompleteForCurrentSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var required = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();
            return !required.Any() || Reconciler.State.AreAllInitialSnapshotsApplied();
        }

        public long GetKnownRevision(PersistentSyncDomainId domainId)
        {
            return Reconciler.GetKnownRevision(domainId);
        }

        /// <summary>
        /// Retries buffered server snapshots first, then asks domains to commit any scalar values
        /// that were deferred until live game objects existed (<see cref="PersistentSyncApplyOutcome"/>).
        /// </summary>
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
