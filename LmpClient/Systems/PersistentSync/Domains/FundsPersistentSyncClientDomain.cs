using LmpClient.Events;
using LmpCommon.PersistentSync;
using System;
using Guid = System.Guid;

namespace LmpClient.Systems.PersistentSync
{
    public class FundsPersistentSyncClientDomain : SyncClientDomain<double>
    {
        private double _lastFunds;

        public bool Reverting { get; private set; }

        public Tuple<Guid, float> CurrentShipCost { get; private set; }

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<FundsPersistentSyncClientDomain>();
        }

        protected override bool CanApplyLiveState()
        {
            return Funding.Instance != null;
        }

        protected override void ApplyLiveState(double value)
        {
            ApplyLiveStateWithLocalSuppression(() =>
                Funding.Instance.SetFunds(value, TransactionReasons.None));
        }

        protected override void OnDomainEnabled()
        {
            GameEvents.OnFundsChanged.Add(OnFundsChanged);
            RevertEvent.onRevertingToLaunch.Add(OnReverting);
            RevertEvent.onReturningToEditor.Add(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            GameEvents.onVesselSwitching.Add(OnVesselSwitching);
            VesselAssemblyEvent.onAssembledVessel.Add(OnVesselAssembled);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnFundsChanged.Remove(OnFundsChanged);
            RevertEvent.onRevertingToLaunch.Remove(OnReverting);
            RevertEvent.onReturningToEditor.Remove(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            GameEvents.onVesselSwitching.Remove(OnVesselSwitching);
            VesselAssemblyEvent.onAssembledVessel.Remove(OnVesselAssembled);

            _lastFunds = 0;
            Reverting = false;
            CurrentShipCost = null;
        }

        protected override void SaveLocalState()
        {
            if (Funding.Instance != null)
            {
                _lastFunds = Funding.Instance.Funds;
            }
        }

        protected override void RestoreLocalState()
        {
            if (Funding.Instance != null)
            {
                Funding.Instance.SetFunds(_lastFunds, TransactionReasons.None);
            }
        }

        private void OnFundsChanged(double funds, TransactionReasons reason)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            LunaLog.Log($"Funds changed to: {funds} reason: {reason}");
            SendScenarioScalar(funds, reason.ToString());
        }

        private void OnReverting()
        {
            Reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnReturningToEditor(EditorFacility data)
        {
            Reverting = true;

            if (CurrentShipCost != null && Funding.Instance != null)
            {
                Funding.Instance.AddFunds(CurrentShipCost.Item2, TransactionReasons.VesselRecovery);
                CurrentShipCost = null;
            }

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

        private void OnVesselSwitching(Vessel data0, Vessel data1)
        {
            CurrentShipCost = null;
        }

        private void OnVesselAssembled(Vessel vessel, ShipConstruct construct)
        {
            CurrentShipCost = new Tuple<Guid, float>(vessel.id, construct.GetShipCosts(out _, out _));
        }

        protected override bool TryBuildLocalAuditPayload(out double payload, out string unavailableReason)
        {
            if (Funding.Instance == null)
            {
                payload = default;
                unavailableReason = "Funding.Instance is null";
                return false;
            }

            payload = Funding.Instance.Funds;
            unavailableReason = null;
            return true;
        }
    }
}
