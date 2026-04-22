using LmpClient.Base;
using LmpClient.Events;
using LmpClient.VesselUtilities;
using System;

namespace LmpClient.Systems.Revert
{
    /// <summary>
    /// This system takes care of all the reverting logic
    /// </summary>
    public class RevertSystem : System<RevertSystem>
    {
        #region Fields & properties

        public static RevertEvents RevertEvents { get; } = new RevertEvents();

        public Guid StartingVesselId { get; set; } = Guid.Empty;

        /// <summary>
        /// When false, Harmony patches disable stock revert UI (switching away from the launch vessel in MP).
        /// After the launch vessel is destroyed, KSP may focus another craft — we still allow revert in that case.
        /// </summary>
        public bool StockRevertEligible()
        {
            if (VesselCommon.IsSpectating) return false;
            if (StartingVesselId == Guid.Empty) return false;
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.id == StartingVesselId)
                return true;
            return FlightGlobals.FindVessel(StartingVesselId) == null;
        }

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(RevertSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            VesselAssemblyEvent.onAssembledVessel.Add(RevertEvents.VesselAssembled);
            GameEvents.onVesselChange.Add(RevertEvents.OnVesselChange);
            RevertEvent.onRevertedToLaunch.Add(RevertEvents.OnRevertToLaunch);
            GameEvents.onGameSceneLoadRequested.Add(RevertEvents.GameSceneLoadRequested);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            VesselAssemblyEvent.onAssembledVessel.Remove(RevertEvents.VesselAssembled);
            GameEvents.onVesselChange.Remove(RevertEvents.OnVesselChange);
            RevertEvent.onRevertedToLaunch.Remove(RevertEvents.OnRevertToLaunch);
            GameEvents.onGameSceneLoadRequested.Remove(RevertEvents.GameSceneLoadRequested);
        }

        #endregion
    }
}
