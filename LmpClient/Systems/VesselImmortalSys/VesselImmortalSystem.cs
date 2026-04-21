using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.VesselUtilities;

namespace LmpClient.Systems.VesselImmortalSys
{
    /// <summary>
    /// This class makes the other vessels immortal, this way if we crash against them they are not destroyed but we do.
    /// In the other player screens they will be destroyed and they will send their new vessel definition.
    /// </summary>
    public class VesselImmortalSystem : System<VesselImmortalSystem>
    {
        #region Fields & properties

        public static VesselImmortalEvents VesselImmortalEvents { get; } = new VesselImmortalEvents();

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(VesselImmortalSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            RailEvent.onVesselGoneOnRails.Add(VesselImmortalEvents.VesselGoOnRails);
            RailEvent.onVesselGoneOffRails.Add(VesselImmortalEvents.VesselGoOffRails);
            GameEvents.onVesselPartCountChanged.Add(VesselImmortalEvents.PartCountChanged);
            GameEvents.onVesselChange.Add(VesselImmortalEvents.OnVesselChange);
            SpectateEvent.onStartSpectating.Add(VesselImmortalEvents.StartSpectating);
            SpectateEvent.onFinishedSpectating.Add(VesselImmortalEvents.FinishSpectating);
            LockEvent.onLockAcquire.Add(VesselImmortalEvents.OnLockAcquire);
            LockEvent.onLockRelease.Add(VesselImmortalEvents.OnLockRelease);

            VesselInitializeEvent.onVesselInitialized.Add(VesselImmortalEvents.VesselInitialized);
            GameEvents.onVesselCreate.Add(VesselImmortalEvents.OnVesselCreated);

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                SetImmortalStateBasedOnLock(vessel);
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            RailEvent.onVesselGoneOnRails.Remove(VesselImmortalEvents.VesselGoOnRails);
            RailEvent.onVesselGoneOffRails.Remove(VesselImmortalEvents.VesselGoOffRails);
            GameEvents.onVesselPartCountChanged.Remove(VesselImmortalEvents.PartCountChanged);
            GameEvents.onVesselChange.Remove(VesselImmortalEvents.OnVesselChange);
            SpectateEvent.onStartSpectating.Remove(VesselImmortalEvents.StartSpectating);
            SpectateEvent.onFinishedSpectating.Remove(VesselImmortalEvents.FinishSpectating);
            LockEvent.onLockAcquire.Remove(VesselImmortalEvents.OnLockAcquire);
            LockEvent.onLockRelease.Remove(VesselImmortalEvents.OnLockRelease);

            VesselInitializeEvent.onVesselInitialized.Remove(VesselImmortalEvents.VesselInitialized);
            GameEvents.onVesselCreate.Remove(VesselImmortalEvents.OnVesselCreated);

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                vessel.SetImmortal(false);
            }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Sets the immortal state based on the lock you have on that vessel
        /// </summary>
        public void SetImmortalStateBasedOnLock(Vessel vessel)
        {
            if (vessel == null) return;

            if (VesselRemoveSystem.Singleton.VesselWillBeKilled(vessel.id))
            {
                vessel.SetImmortal(true);
                return;
            }

            // Always run full local physics on our own active vessel. If lock rows are briefly missing,
            // DoVesselChecks can fall through to "apply remote stream" and would incorrectly set immortal and
            // disable FlightIntegrator (broken engine plumes / smoke).
            if (FlightGlobals.ActiveVessel != null && vessel.id == FlightGlobals.ActiveVessel.id && !VesselCommon.IsSpectating)
            {
                vessel.SetImmortal(false);
                return;
            }

            // Align with VesselCommon.DoVesselChecks: whenever we consume another player's streamed state for this
            // vessel id, skip local FlightIntegrator so orbit/interpolation/HUD stay consistent. Includes the
            // "another player has control" case and the stale-update-lock + missing control row case fixed there.
            vessel.SetImmortal(VesselCommon.DoVesselChecks(vessel.id));
        }

        #endregion
    }
}
