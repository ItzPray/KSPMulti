using LmpClient.Events;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class SciencePersistentSyncClientDomain : SyncClientDomain<float>
    {
        private float _lastScience;

        public bool Reverting { get; private set; }

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<SciencePersistentSyncClientDomain>();
        }

        protected override bool CanApplyLiveState()
        {
            return ResearchAndDevelopment.Instance != null;
        }

        protected override void ApplyLiveState(float value)
        {
            ApplyLiveStateWithLocalSuppression(() =>
                ResearchAndDevelopment.Instance.SetScience(value, TransactionReasons.None));
        }

        protected override void OnDomainEnabled()
        {
            GameEvents.OnScienceChanged.Add(OnScienceChanged);
            RevertEvent.onRevertingToLaunch.Add(OnReverting);
            RevertEvent.onReturningToEditor.Add(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnScienceChanged.Remove(OnScienceChanged);
            RevertEvent.onRevertingToLaunch.Remove(OnReverting);
            RevertEvent.onReturningToEditor.Remove(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);

            _lastScience = 0;
            Reverting = false;
        }

        protected override void SaveLocalState()
        {
            if (ResearchAndDevelopment.Instance != null)
            {
                _lastScience = ResearchAndDevelopment.Instance.Science;
            }
        }

        protected override void RestoreLocalState()
        {
            if (ResearchAndDevelopment.Instance != null)
            {
                ResearchAndDevelopment.Instance.SetScience(_lastScience, TransactionReasons.None);
            }
        }

        private void OnScienceChanged(float science, TransactionReasons reason)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            SendScenarioScalar(science, reason.ToString());
            LunaLog.Log($"Science changed to: {science} with reason: {reason}");
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
            if (ResearchAndDevelopment.Instance == null)
            {
                payload = default;
                unavailableReason = "ResearchAndDevelopment.Instance is null";
                return false;
            }

            payload = ResearchAndDevelopment.Instance.Science;
            unavailableReason = null;
            return true;
        }
    }
}
