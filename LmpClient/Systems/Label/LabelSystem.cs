using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.SettingsSys;

namespace LmpClient.Systems.Label
{
    /// <summary>
    /// This class display the player names in the labels
    /// </summary>
    public class LabelSystem : System<LabelSystem>
    {
        private static LabelEvents LabelEvents { get; } = new LabelEvents();

        #region Base overrides

        public override string SystemName { get; } = nameof(LabelSystem);

        protected override void OnEnabled()
        {
            LabelEvent.onLabelProcessed.Add(LabelEvents.OnLabelProcessed);
            LabelEvent.onMapLabelProcessed.Add(LabelEvents.OnMapLabelProcessed);
            LabelEvent.onMapWidgetTextProcessed.Add(LabelEvents.OnMapWidgetTextProcessed);

            LockEvent.onLockAcquire.Add(LabelEvents.OnLockAcquire);
            LockEvent.onLockRelease.Add(LabelEvents.OnLockRelease);
            LockEvent.onLockListApplied.Add(LabelEvents.OnLockListApplied);

            SetupRoutine(new RoutineDefinition(0, RoutineExecution.LateUpdate, LabelEvents.RefreshFlightLabelsLateUpdate));

            LabelEvents.RefreshFlightLabelsAfterLabelSystemEnabled();

            if (SettingsSystem.CurrentSettings.Debug8)
                VesselLabelLockDiagnostics.OnLabelSystemEnabled();
        }

        protected override void OnDisabled()
        {
            LabelEvent.onLabelProcessed.Remove(LabelEvents.OnLabelProcessed);
            LabelEvent.onMapLabelProcessed.Remove(LabelEvents.OnMapLabelProcessed);
            LabelEvent.onMapWidgetTextProcessed.Remove(LabelEvents.OnMapWidgetTextProcessed);

            LockEvent.onLockAcquire.Remove(LabelEvents.OnLockAcquire);
            LockEvent.onLockRelease.Remove(LabelEvents.OnLockRelease);
            LockEvent.onLockListApplied.Remove(LabelEvents.OnLockListApplied);
        }

        #endregion
    }
}
