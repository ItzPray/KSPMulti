using LmpClient.Events;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class ReputationPersistentSyncClientDomain : SyncClientDomain<float>
    {
        private float _lastReputation;

        public bool Reverting { get; private set; }

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<ReputationPersistentSyncClientDomain>();
        }

        protected override bool CanApplyLiveState()
        {
            return Reputation.Instance != null;
        }

        protected override void ApplyLiveState(float value)
        {
            ApplyLiveStateWithLocalSuppression(() =>
                Reputation.Instance.SetReputation(value, TransactionReasons.None));
        }

        protected override void OnDomainEnabled()
        {
            GameEvents.OnReputationChanged.Add(OnReputationChanged);
            RevertEvent.onRevertingToLaunch.Add(OnReverting);
            RevertEvent.onReturningToEditor.Add(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnReputationChanged.Remove(OnReputationChanged);
            RevertEvent.onRevertingToLaunch.Remove(OnReverting);
            RevertEvent.onReturningToEditor.Remove(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);

            _lastReputation = 0;
            Reverting = false;
        }

        protected override void SaveLocalState()
        {
            if (Reputation.Instance != null)
            {
                _lastReputation = Reputation.Instance.reputation;
            }
        }

        protected override void RestoreLocalState()
        {
            if (Reputation.Instance != null)
            {
                Reputation.Instance.SetReputation(_lastReputation, TransactionReasons.None);
            }
        }

        private void OnReputationChanged(float reputation, TransactionReasons reason)
        {
            if (IgnoreLocalEvents)
            {
                LunaLog.Log($"[KSPMP] Reputation event suppressed (IgnoreLocalEvents=true) reputation={reputation} reason={reason}");
                return;
            }

            LunaLog.Log($"Reputation changed to: {reputation} reason: {reason}");
            SendScenarioScalar(reputation, reason.ToString());
        }

        private void OnReverting()
        {
            Reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnReturningToEditor(EditorFacility data)
        {
            Reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnLevelLoaded(GameScenes data)
        {
            if (!Reverting)
            {
                return;
            }

            Reverting = false;
            StopIgnoringLocalEvents(true);
        }

        protected override bool TryBuildLocalAuditPayload(out float payload, out string unavailableReason)
        {
            if (Reputation.Instance == null)
            {
                payload = default;
                unavailableReason = "Reputation.Instance is null";
                return false;
            }

            payload = Reputation.Instance.reputation;
            unavailableReason = null;
            return true;
        }
    }
}
