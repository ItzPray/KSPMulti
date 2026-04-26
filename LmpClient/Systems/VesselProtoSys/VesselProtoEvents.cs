using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpClient.Systems.Scenario;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Utilities;
using LmpClient.VesselUtilities;
using Expansions.Serenity.DeployedScience.Runtime;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.VesselProtoSys
{
    public class VesselProtoEvents : SubSystem<VesselProtoSystem>
    {
        private static bool _groundScienceScenarioSyncQueued;

        /// <summary>
        /// When stop warping, spawn the missing vessels
        /// </summary>
        public void WarpStopped()
        {
            System.CheckVesselsToLoad();
        }

        /// <summary>
        /// Sends our vessel just when we start the flight
        /// </summary>
        public void FlightReady()
        {
            if (VesselCommon.IsSpectating || FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.id == Guid.Empty)
                return;

            System.InitializeActiveVesselManeuverTracking("flight ready");
            System.MessageSender.SendVesselMessage(FlightGlobals.ActiveVessel, true);
        }

        public void ManeuverNodeAdded(Vessel vessel, PatchedConicSolver solver)
        {
            SendManeuverNodes(vessel, "added");
        }

        public void ManeuverNodeRemoved(Vessel vessel, PatchedConicSolver solver)
        {
            SendManeuverNodes(vessel, "removed");
        }

        /// <summary>
        /// Event called when switching scene and before reaching the other scene
        /// </summary>
        internal void OnSceneRequested(GameScenes requestedScene)
        {
            if (HighLogic.LoadedSceneIsFlight && requestedScene != GameScenes.FLIGHT && !VesselCommon.IsSpectating)
            {
                //When quitting flight send the vessel one last time
                VesselProtoSystem.Singleton.MessageSender.SendVesselMessage(FlightGlobals.ActiveVessel);
            }
        }

        /// <summary>
        /// Triggered when transmitting science. Science experiment is stored in the vessel so send the definition to the server
        /// </summary>
        public void TriggeredDataTransmission(ScienceData science, Vessel vessel, bool data)
        {
            if (FlightGlobals.ActiveVessel != null && !VesselCommon.IsSpectating)
            {
                //We must send the science subject aswell!
                var subject = ResearchAndDevelopment.GetSubjectByID(science.subjectID);
                if (subject != null)
                {
                    LunaLog.Log("Detected a experiment transmission. Sending vessel definition to the server");
                    System.MessageSender.SendVesselMessage(FlightGlobals.ActiveVessel, true);

                    ShareScienceSubjectSystem.Singleton.MessageSender.SendScienceSubjectMessage(subject);
                }
            }
        }

        /// <summary>
        /// Triggered when storing science. Science experiment is stored in the vessel so send the definition to the server
        /// </summary>
        public void ExperimentStored(ScienceData science)
        {
            if (FlightGlobals.ActiveVessel != null && !VesselCommon.IsSpectating)
            {
                //We must send the science subject aswell!
                var subject = ResearchAndDevelopment.GetSubjectByID(science.subjectID);
                if (subject != null)
                {
                    LunaLog.Log("Detected a experiment stored. Sending vessel definition to the server");
                    System.MessageSender.SendVesselMessage(FlightGlobals.ActiveVessel, true);

                    ShareScienceSubjectSystem.Singleton.MessageSender.SendScienceSubjectMessage(subject);
                }
            }
        }

        /// <summary>
        /// Triggered when resetting a experiment. Science experiment is stored in the vessel so send the definition to the server
        /// </summary>
        public void ExperimentReset(Vessel data)
        {
            if (FlightGlobals.ActiveVessel != null && !VesselCommon.IsSpectating)
            {
                LunaLog.Log("Detected a experiment reset. Sending vessel definition to the server");
                System.MessageSender.SendVesselMessage(FlightGlobals.ActiveVessel, true);
            }
        }

        private void SendManeuverNodes(Vessel vessel, string reason)
        {
            if (VesselCommon.IsSpectating || vessel == null || vessel.id == Guid.Empty)
                return;

            if (!LockSystem.LockQuery.UpdateLockBelongsToPlayer(vessel.id, SettingsSystem.CurrentSettings.PlayerName))
                return;

            LunaLog.Log($"[KSPMP]: Detected maneuver node {reason}. Sending vessel definition {vessel.id} ({vessel.vesselName})");
            System.MessageSender.SendVesselMessage(vessel);
        }

        public void GroundSciencePartDeployed(ModuleGroundSciencePart groundSciencePart)
        {
            QueueGroundScienceVesselProto(groundSciencePart, "deployed");
            QueueGroundScienceScenarioSync();
        }

        public void GroundSciencePartChanged(ModuleGroundSciencePart groundSciencePart)
        {
            QueueGroundScienceVesselProto(groundSciencePart, "changed");
            QueueGroundScienceScenarioSync();
        }

        public void GroundSciencePartRemoved(ModuleGroundSciencePart groundSciencePart)
        {
            if (VesselCommon.IsSpectating)
                return;

            var vessel = groundSciencePart?.part?.vessel;
            if (vessel == null || vessel.id == Guid.Empty)
            {
                QueueGroundScienceScenarioSync();
                return;
            }

            LunaLog.Log($"[KSPMP]: Breaking Ground deployable science vessel removed: {vessel.id} ({vessel.vesselName})");
            VesselRemoveSystem.Singleton.MessageSender.SendVesselRemove(vessel);
            VesselRemoveSystem.Singleton.RemovedVessels.TryAdd(vessel.id, DateTime.Now);
            VesselCommon.RemoveVesselFromSystems(vessel.id);
            QueueGroundScienceScenarioSync();
        }

        public void GroundScienceClusterChanged(ModuleGroundExpControl control, DeployedScienceCluster cluster)
        {
            QueueGroundScienceVesselProto(control, "cluster changed");
            QueueGroundScienceScenarioSync();
        }

        public void GroundScienceControllerChanged(ModuleGroundExpControl control, bool enabled, List<ModuleGroundSciencePart> groundScienceParts)
        {
            QueueGroundScienceVesselProto(control, "controller changed");

            if (groundScienceParts != null)
            {
                foreach (var groundSciencePart in groundScienceParts)
                    QueueGroundScienceVesselProto(groundSciencePart, "controller changed");
            }

            QueueGroundScienceScenarioSync();
        }

        public void GroundScienceGenerated(DeployedScienceExperiment experiment, DeployedSciencePart part, DeployedScienceCluster cluster, float science)
        {
            QueueGroundScienceScenarioSync();
        }

        public void GroundScienceClusterDeregistered(uint clusterId)
        {
            QueueGroundScienceScenarioSync();
        }

        public void PartUndocked(Part part, DockedVesselInfo dockedInfo, Vessel originalVessel)
        {
            if (VesselCommon.IsSpectating) return;
            if (!LockSystem.LockQuery.UpdateLockBelongsToPlayer(originalVessel.id, SettingsSystem.CurrentSettings.PlayerName)) return;

            System.MessageSender.SendVesselMessage(part.vessel);

            //As this method can be called several times in a short period (when staging) we delay the sending of the final vessel
            System.DelayedSendVesselMessage(originalVessel.id, 0.5f);
        }

        public void PartDecoupled(Part part, float breakForce, Vessel originalVessel)
        {
            if (VesselCommon.IsSpectating || originalVessel == null) return;
            if (!LockSystem.LockQuery.UpdateLockBelongsToPlayer(originalVessel.id, SettingsSystem.CurrentSettings.PlayerName)) return;

            System.MessageSender.SendVesselMessage(part.vessel);

            //As this method can be called several times in a short period (when staging) we delay the sending of the final vessel
            System.DelayedSendVesselMessage(originalVessel.id, 0.5f);
        }

        public void PartCoupled(Part partFrom, Part partTo, Guid removedVesselId)
        {
            if (VesselCommon.IsSpectating) return;

            //If neither the vessel 1 or vessel2 locks belong to us, ignore the coupling
            if (!LockSystem.LockQuery.UpdateLockBelongsToPlayer(partFrom.vessel.id, SettingsSystem.CurrentSettings.PlayerName) &&
                !LockSystem.LockQuery.UpdateLockBelongsToPlayer(removedVesselId, SettingsSystem.CurrentSettings.PlayerName)) return;

            System.MessageSender.SendVesselMessage(partFrom.vessel);
        }

        private void QueueGroundScienceVesselProto(PartModule groundScienceModule, string reason)
        {
            if (VesselCommon.IsSpectating)
                return;

            var vessel = groundScienceModule?.part?.vessel;
            if (vessel == null || vessel.id == Guid.Empty)
                return;

            LunaLog.Log($"[KSPMP]: Detected Breaking Ground deployable science {reason}. Sending vessel definition {vessel.id} ({vessel.vesselName})");
            System.DelayedSendVesselMessage(vessel.id, 0.5f, true);
        }

        private static void QueueGroundScienceScenarioSync()
        {
            if (_groundScienceScenarioSyncQueued)
                return;

            _groundScienceScenarioSyncQueued = true;
            CoroutineUtil.StartDelayedRoutine("GroundScienceScenarioSync", () =>
            {
                _groundScienceScenarioSyncQueued = false;
                if (ScenarioSystem.Singleton.Enabled)
                    ScenarioSystem.Singleton.SendScenarioModules();
            }, 1f);
        }
    }
}
