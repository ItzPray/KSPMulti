using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Utilities;
using LmpClient.VesselUtilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LmpClient.Systems.VesselProtoSys
{
    /// <summary>
    /// This system handles the vessel loading into the game and sending our vessel structure to other players.
    /// </summary>
    public class VesselProtoSystem : MessageSystem<VesselProtoSystem, VesselProtoMessageSender, VesselProtoMessageHandler>
    {
        #region Fields & properties

        private static readonly HashSet<Guid> QueuedVesselsToSend = new HashSet<Guid>();

        public readonly HashSet<Guid> VesselsUnableToLoad = new HashSet<Guid>();

        public ConcurrentDictionary<Guid, VesselProtoQueue> VesselProtos { get; } = new ConcurrentDictionary<Guid, VesselProtoQueue>();

        public bool ProtoSystemReady => Enabled && FlightGlobals.ready && HighLogic.LoadedScene == GameScenes.FLIGHT &&
            FlightGlobals.ActiveVessel != null && !VesselCommon.IsSpectating;

        public VesselProtoEvents VesselProtoEvents { get; } = new VesselProtoEvents();

        public VesselRemoveSystem VesselRemoveSystem => VesselRemoveSystem.Singleton;

        /// <summary>
        /// Cap how many incoming protos we fully deserialize + load per Update. Server join sync sends many
        /// <see cref="VesselProtoMsgData"/> with GameTime default 0, so all are eligible at once; loading every
        /// vessel in a single frame spikes RAM (ConfigNode + parts). Spreading loads removes most of that peak.
        /// </summary>
        private const int MaxVesselProtoLoadsPerUpdate = 4;

        private static readonly object ManeuverNodeLogLock = new object();
        private static readonly string ManeuverNodeLogPath = Path.Combine(KSPUtil.ApplicationRootPath, "LMP_ManeuverNodes.log");
        private static readonly Dictionary<Guid, string> ManeuverSignatures = new Dictionary<Guid, string>();

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(VesselProtoSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            GameEvents.onFlightReady.Add(VesselProtoEvents.FlightReady);
            GameEvents.onGameSceneLoadRequested.Add(VesselProtoEvents.OnSceneRequested);

            GameEvents.OnTriggeredDataTransmission.Add(VesselProtoEvents.TriggeredDataTransmission);
            GameEvents.OnExperimentStored.Add(VesselProtoEvents.ExperimentStored);
            ExperimentEvent.onExperimentReset.Add(VesselProtoEvents.ExperimentReset);
            GameEvents.onManeuverAdded.Add(VesselProtoEvents.ManeuverNodeAdded);
            GameEvents.onManeuverRemoved.Add(VesselProtoEvents.ManeuverNodeRemoved);
            GameEvents.onGroundSciencePartDeployed.Add(VesselProtoEvents.GroundSciencePartDeployed);
            GameEvents.onGroundSciencePartChanged.Add(VesselProtoEvents.GroundSciencePartChanged);
            GameEvents.onGroundSciencePartEnabledStateChanged.Add(VesselProtoEvents.GroundSciencePartChanged);
            GameEvents.onGroundSciencePartRemoved.Add(VesselProtoEvents.GroundSciencePartRemoved);
            GameEvents.onGroundScienceClusterRegistered.Add(VesselProtoEvents.GroundScienceClusterChanged);
            GameEvents.onGroundScienceClusterUpdated.Add(VesselProtoEvents.GroundScienceClusterChanged);
            GameEvents.onGroundScienceControllerChanged.Add(VesselProtoEvents.GroundScienceControllerChanged);
            GameEvents.onGroundScienceGenerated.Add(VesselProtoEvents.GroundScienceGenerated);
            GameEvents.onGroundScienceTransmitted.Add(VesselProtoEvents.GroundScienceGenerated);
            GameEvents.onGroundScienceDeregisterCluster.Add(VesselProtoEvents.GroundScienceClusterDeregistered);

            PartEvent.onPartDecoupled.Add(VesselProtoEvents.PartDecoupled);
            PartEvent.onPartUndocked.Add(VesselProtoEvents.PartUndocked);
            PartEvent.onPartCoupled.Add(VesselProtoEvents.PartCoupled);

            WarpEvent.onTimeWarpStopped.Add(VesselProtoEvents.WarpStopped);

            SetupRoutine(new RoutineDefinition(0, RoutineExecution.Update, CheckVesselsToLoad));
            SetupRoutine(new RoutineDefinition(2500, RoutineExecution.Update, SendVesselDefinition));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            GameEvents.onFlightReady.Remove(VesselProtoEvents.FlightReady);
            GameEvents.onGameSceneLoadRequested.Remove(VesselProtoEvents.OnSceneRequested);

            GameEvents.OnTriggeredDataTransmission.Remove(VesselProtoEvents.TriggeredDataTransmission);
            GameEvents.OnExperimentStored.Remove(VesselProtoEvents.ExperimentStored);
            ExperimentEvent.onExperimentReset.Remove(VesselProtoEvents.ExperimentReset);
            GameEvents.onManeuverAdded.Remove(VesselProtoEvents.ManeuverNodeAdded);
            GameEvents.onManeuverRemoved.Remove(VesselProtoEvents.ManeuverNodeRemoved);
            GameEvents.onGroundSciencePartDeployed.Remove(VesselProtoEvents.GroundSciencePartDeployed);
            GameEvents.onGroundSciencePartChanged.Remove(VesselProtoEvents.GroundSciencePartChanged);
            GameEvents.onGroundSciencePartEnabledStateChanged.Remove(VesselProtoEvents.GroundSciencePartChanged);
            GameEvents.onGroundSciencePartRemoved.Remove(VesselProtoEvents.GroundSciencePartRemoved);
            GameEvents.onGroundScienceClusterRegistered.Remove(VesselProtoEvents.GroundScienceClusterChanged);
            GameEvents.onGroundScienceClusterUpdated.Remove(VesselProtoEvents.GroundScienceClusterChanged);
            GameEvents.onGroundScienceControllerChanged.Remove(VesselProtoEvents.GroundScienceControllerChanged);
            GameEvents.onGroundScienceGenerated.Remove(VesselProtoEvents.GroundScienceGenerated);
            GameEvents.onGroundScienceTransmitted.Remove(VesselProtoEvents.GroundScienceGenerated);
            GameEvents.onGroundScienceDeregisterCluster.Remove(VesselProtoEvents.GroundScienceClusterDeregistered);

            PartEvent.onPartDecoupled.Remove(VesselProtoEvents.PartDecoupled);
            PartEvent.onPartUndocked.Remove(VesselProtoEvents.PartUndocked);
            PartEvent.onPartCoupled.Remove(VesselProtoEvents.PartCoupled);

            WarpEvent.onTimeWarpStopped.Remove(VesselProtoEvents.WarpStopped);

            //This is the main system that handles the vesselstore so if it's disabled clear the store too
            VesselProtos.Clear();
            VesselsUnableToLoad.Clear();
            QueuedVesselsToSend.Clear();
            ManeuverSignatures.Clear();
        }

        #endregion

        #region Update routines

        /// <summary>
        /// Send the definition of our own vessel and the secondary vessels.
        /// </summary>
        private void SendVesselDefinition()
        {
            try
            {
                if (ProtoSystemReady)
                {
                    var activeVessel = FlightGlobals.ActiveVessel;
                    if (activeVessel.parts.Count != activeVessel.protoVessel.protoPartSnapshots.Count)
                        MessageSender.SendVesselMessage(activeVessel);

                    CheckAndSendManeuverChanges(activeVessel);

                    foreach (var vessel in VesselCommon.GetSecondaryVessels())
                    {
                        if (vessel.parts.Count != vessel.protoVessel.protoPartSnapshots.Count)
                            MessageSender.SendVesselMessage(vessel);
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error in SendVesselDefinition {e}");
            }

        }

        /// <summary>
        /// Check vessels that must be loaded
        /// </summary>
        public void CheckVesselsToLoad()
        {
            if (HighLogic.LoadedScene < GameScenes.SPACECENTER) return;

            try
            {
                var loadsRemaining = MaxVesselProtoLoadsPerUpdate;
                foreach (var keyVal in VesselProtos)
                {
                    if (loadsRemaining <= 0)
                        break;

                    if (!keyVal.Value.TryPeek(out var vesselProto) || vesselProto.GameTime > TimeSyncSystem.UniversalTime)
                        continue;

                    if (!keyVal.Value.TryDequeue(out vesselProto))
                        continue;

                    loadsRemaining--;

                    if (VesselRemoveSystem.VesselWillBeKilled(vesselProto.VesselId))
                    {
                        keyVal.Value.Recycle(vesselProto);
                        continue;
                    }

                    var forceReload = vesselProto.ForceReload;
                    var protoVessel = vesselProto.CreateProtoVessel();
                    keyVal.Value.Recycle(vesselProto);

                    if (protoVessel == null || protoVessel.HasInvalidParts(!VesselsUnableToLoad.Contains(vesselProto.VesselId)))
                    {
                        VesselsUnableToLoad.Add(vesselProto.VesselId);
                        continue;
                    }

                    VesselsUnableToLoad.Remove(vesselProto.VesselId);

                    var existingVessel = FlightGlobals.FindVessel(vesselProto.VesselId);
                    if (existingVessel == null)
                    {
                        if (VesselLoader.LoadVessel(protoVessel, forceReload))
                        {
                            LunaLog.Log($"[KSPMP]: Vessel {protoVessel.vesselID} loaded");
                            VesselLoadEvent.onLmpVesselLoaded.Fire(protoVessel.vesselRef);
                        }
                    }
                    else
                    {
                        if (VesselLoader.LoadVessel(protoVessel, forceReload))
                        {
                            LunaLog.Log($"[KSPMP]: Vessel {protoVessel.vesselID} reloaded");
                            VesselReloadEvent.onLmpVesselReloaded.Fire(protoVessel.vesselRef);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error in CheckVesselsToLoad {e}");
            }
        }

        #endregion

        #region Public methods

        public void InitializeActiveVesselManeuverTracking(string reason)
        {
            var activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel == null || activeVessel.id == Guid.Empty)
                return;

            var signature = GetManeuverSignature(activeVessel);
            ManeuverSignatures[activeVessel.id] = signature;
            WriteManeuverNodeLog(activeVessel, $"Updated maneuver tracking ({reason})", signature);
        }

        public void SendActiveVesselManeuverNodes(string reason)
        {
            if (!ProtoSystemReady)
                return;

            var activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel == null || activeVessel.id == Guid.Empty)
                return;

            var signature = GetManeuverSignature(activeVessel);
            ManeuverSignatures[activeVessel.id] = signature;
            WriteManeuverNodeLog(activeVessel, $"Immediate maneuver node send ({reason})", signature);
            MessageSender.SendVesselMessage(activeVessel);
        }

        public bool SendLocalVesselProto(Guid vesselId, string reason, bool forceReload = true)
        {
            if (!Enabled || VesselCommon.IsSpectating || vesselId == Guid.Empty)
                return false;

            var vessel = FlightGlobals.FindVessel(vesselId);
            if (vessel != null)
            {
                LunaLog.Log($"[KSPMP]: Sending local vessel proto {vesselId} ({vessel.vesselName}) reason={reason}");
                MessageSender.SendVesselMessage(vessel, forceReload);
                return true;
            }

            var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels;
            if (protoVessels == null)
                return false;

            foreach (var protoVessel in protoVessels)
            {
                if (protoVessel == null || protoVessel.vesselID != vesselId)
                    continue;

                LunaLog.Log($"[KSPMP]: Sending local unloaded proto vessel {vesselId} ({protoVessel.vesselName}) reason={reason}");
                MessageSender.SendProtoVesselMessage(protoVessel, forceReload);
                return true;
            }

            return false;
        }

        public HashSet<Guid> GetLocalVesselIds()
        {
            var vesselIds = new HashSet<Guid>();

            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel != null && vessel.id != Guid.Empty)
                {
                    vesselIds.Add(vessel.id);
                }
            }

            var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels;
            if (protoVessels != null)
            {
                foreach (var protoVessel in protoVessels)
                {
                    if (protoVessel != null && protoVessel.vesselID != Guid.Empty)
                    {
                        vesselIds.Add(protoVessel.vesselID);
                    }
                }
            }

            return vesselIds;
        }

        private void CheckAndSendManeuverChanges(Vessel vessel)
        {
            if (vessel == null || vessel.id == Guid.Empty)
                return;

            if (!LockSystem.LockQuery.UpdateLockBelongsToPlayer(vessel.id, SettingsSystem.CurrentSettings.PlayerName))
                return;

            var signature = GetManeuverSignature(vessel);
            if (ManeuverSignatures.TryGetValue(vessel.id, out var lastSignature))
            {
                if (lastSignature == signature)
                    return;

                ManeuverSignatures[vessel.id] = signature;
                WriteManeuverNodeLog(vessel, $"Detected maneuver node change via poll. Previous: {lastSignature}", signature);
                MessageSender.SendVesselMessage(vessel);
                return;
            }

            ManeuverSignatures[vessel.id] = signature;
            WriteManeuverNodeLog(vessel, "Tracking active vessel maneuver nodes", signature);
        }

        private static string GetManeuverSignature(Vessel vessel)
        {
            var solver = vessel?.patchedConicSolver;
            if (solver == null || solver.maneuverNodes == null || solver.maneuverNodes.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            for (var i = 0; i < solver.maneuverNodes.Count; i++)
            {
                var node = solver.maneuverNodes[i];
                if (node == null)
                {
                    sb.Append("|null");
                    continue;
                }

                var deltaV = node.DeltaV;
                sb.Append('|').Append(i).Append(':')
                    .Append(node.UT.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(deltaV.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(deltaV.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(deltaV.z.ToString("F3", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static void WriteManeuverNodeLog(Vessel vessel, string message, string signature)
        {
            try
            {
                var vesselName = vessel?.vesselName ?? "<null>";
                var vesselId = vessel?.id.ToString() ?? Guid.Empty.ToString();
                var line = $"[{DateTime.UtcNow:O}] {message} | Vessel={vesselName} | Id={vesselId} | Signature={signature}{Environment.NewLine}";

                lock (ManeuverNodeLogLock)
                {
                    File.AppendAllText(ManeuverNodeLogPath, line);
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error writing maneuver node log: {e}");
            }
        }

        /// <summary>
        /// Sends a delayed vessel definition to the server.
        /// Call this method if you expect to do a lot of modifications to a vessel and you want to send it only once
        /// </summary>
        public void DelayedSendVesselMessage(Guid vesselId, float delayInSec, bool forceReload = false)
        {
            if (QueuedVesselsToSend.Contains(vesselId)) return;

            QueuedVesselsToSend.Add(vesselId);
            CoroutineUtil.StartDelayedRoutine("QueueVesselMessageAsPartsChanged", () =>
            {
                QueuedVesselsToSend.Remove(vesselId);

                LunaLog.Log($"[KSPMP]: Sending delayed proto vessel {vesselId}");
                MessageSender.SendVesselMessage(FlightGlobals.FindVessel(vesselId), forceReload);
            }, delayInSec);
        }

        /// <summary>
        /// Removes a vessel from the system
        /// </summary>
        public void RemoveVessel(Guid vesselId)
        {
            VesselProtos.TryRemove(vesselId, out _);
        }

        #endregion
    }
}
