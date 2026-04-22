using LmpClient.Base;
using System;

namespace LmpClient.Systems.Revert
{
    public class RevertEvents : SubSystem<RevertSystem>
    {
        private static bool _revertingToLaunch = false;

        public void OnVesselChange(Vessel data)
        {
            if (_revertingToLaunch)
            {
                _revertingToLaunch = false;
                return;
            }

            if (data == null) return;

            // Still controlling the vessel we launched with
            if (data.id == System.StartingVesselId)
                return;

            // Invalidate revert only when switching to another vessel while the launch vessel still exists (stock behaviour).
            // If the launch vessel was destroyed, keep StartingVesselId so revert-to-launch can stay available after KSP retargets.
            if (System.StartingVesselId != Guid.Empty && FlightGlobals.FindVessel(System.StartingVesselId) != null)
                System.StartingVesselId = Guid.Empty;
        }

        public void VesselAssembled(Vessel vessel, ShipConstruct construct)
        {
            System.StartingVesselId = vessel.id;
        }

        public void OnRevertToLaunch()
        {
            _revertingToLaunch = true;
            if (FlightGlobals.ActiveVessel)
                System.StartingVesselId = FlightGlobals.ActiveVessel.id;
        }

        public void GameSceneLoadRequested(GameScenes data)
        {
            if (data != GameScenes.FLIGHT && _revertingToLaunch)
                _revertingToLaunch = false;
        }
    }
}
