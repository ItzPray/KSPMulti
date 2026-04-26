using LmpClient.Extensions;
using LmpClient.Systems.Lock;
using LmpClient.Systems.VesselPositionSys;
using LmpClient.VesselUtilities;
using System;
using System.Linq;
using System.Collections.Generic;

namespace LmpClient.Systems.VesselUndockSys
{
    /// <summary>
    /// Class that maps a message class to a system class. This way we avoid the message caching issues
    /// </summary>
    public class VesselUndock
    {
        #region Fields and Properties

        public double GameTime;
        public Guid VesselId;

        public uint PartFlightId;
        public Guid NewVesselId;

        public DockedVesselInfo DockedInfo;

        #endregion

        public void ProcessUndock()
        {
            if (!VesselCommon.DoVesselChecks(VesselId))
                return;

            var vessel = FlightGlobals.FindVessel(VesselId);
            if (vessel == null) return;

            var protoPart = vessel.protoVessel.GetProtoPart(PartFlightId);
            if (protoPart != null)
            {
                if (protoPart.partRef)
                {
                    var dockingNode = protoPart.partRef.FindModulesImplementing<ModuleDockingNode>().FirstOrDefault();
                    if (dockingNode != null)
                    {
                        if (DockingPortUtil.IsInDockedState(dockingNode))
                        {
                            // Port is in a valid docked state - proceed normally
                        }
                        else if (DockingPortUtil.IsInRecoverableTransientState(dockingNode))
                        {
                            // Port is stuck in a transient state (e.g., Disengage). Attempt FSM recovery
                            // before undocking, using the same technique as DockRotate: fsm.StartFSM(state)
                            var targetState = DockingPortUtil.InferDockedStateForUndock(dockingNode);
                            if (!DockingPortUtil.TryRecoverToDockedState(dockingNode, targetState))
                            {
                                LunaLog.LogWarning($"[LMP]: Cannot recover docking port for undock. " +
                                    $"Part: {protoPart.partRef.partName}, Vessel: {VesselId}, PartFlightId: {PartFlightId}");
                                return;
                            }
                        }
                        else
                        {
                            // Port is in Ready, Disabled, or unknown state - not docked, skip
                            LunaLog.LogWarning($"[LMP]: Skipping undock - docking port FSM is in state " +
                                $"'{dockingNode.fsm?.currentStateName}', not a docked or recoverable state. " +
                                $"Part: {protoPart.partRef.partName}, Vessel: {VesselId}, PartFlightId: {PartFlightId}");
                            return;
                        }
                    }

                    VesselUndockSystem.Singleton.ManuallyUndockingVesselId = protoPart.partRef.vessel.id;
                    VesselUndockSystem.Singleton.IgnoreEvents = true;

                    protoPart.partRef.Undock(DockedInfo);
                    protoPart.partRef.vessel.id = NewVesselId;

                    LockSystem.Singleton.FireVesselLocksEvents(NewVesselId);

                    //Forcefully set the vessel as immortal
                    protoPart.partRef.vessel.SetImmortal(true);

                    VesselPositionSystem.Singleton.ForceUpdateVesselPosition(NewVesselId);

                    VesselUndockSystem.Singleton.IgnoreEvents = false;
                    VesselUndockSystem.Singleton.ManuallyUndockingVesselId = Guid.Empty;
                }
            }
        }
    }
}

namespace LmpClient.VesselUtilities
{
    public static class DockingPortUtil
    {
        private static readonly string[] DockedStates =
        {
            "Docked (docker)",
            "Docked (dockee)",
            "Docked (same vessel)",
            "PreAttached"
        };

        private static readonly string[] RecoverableTransientStates =
        {
            "Disengage",
            "Acquire",
            "Acquire (dockee)"
        };

        public static bool IsInDockedState(ModuleDockingNode node)
        {
            if (node?.fsm == null) return false;
            var state = node.fsm.currentStateName;
            return !string.IsNullOrEmpty(state) && DockedStates.Contains(state);
        }

        public static bool IsInRecoverableTransientState(ModuleDockingNode node)
        {
            if (node?.fsm == null) return false;
            var state = node.fsm.currentStateName;
            return !string.IsNullOrEmpty(state) && RecoverableTransientStates.Contains(state);
        }

        public static ModuleDockingNode FindPartnerFromPartTree(ModuleDockingNode node)
        {
            if (node?.part == null) return null;

            var referenceNodeName = node.referenceAttachNode;
            if (string.IsNullOrEmpty(referenceNodeName)) return null;

            var attachNode = node.part.FindAttachNode(referenceNodeName);
            if (attachNode?.attachedPart != null)
            {
                var otherDockingNodes = attachNode.attachedPart.FindModulesImplementing<ModuleDockingNode>();
                if (otherDockingNodes != null && otherDockingNodes.Count > 0)
                {
                    foreach (var other in otherDockingNodes)
                    {
                        if (string.IsNullOrEmpty(other.referenceAttachNode)) continue;
                        var otherAttach = other.part.FindAttachNode(other.referenceAttachNode);
                        if (otherAttach?.attachedPart == node.part)
                            return other;
                    }
                    return otherDockingNodes[0];
                }
            }

            if (node.part.parent != null)
            {
                var parentDocks = node.part.parent.FindModulesImplementing<ModuleDockingNode>();
                if (parentDocks != null && parentDocks.Count > 0 && !IsBottomOrSurfaceAttachedTo(node.part, node.part.parent))
                    return parentDocks[0];
            }

            if (node.part.children != null)
            {
                foreach (var child in node.part.children)
                {
                    if (child == null) continue;
                    var childDocks = child.FindModulesImplementing<ModuleDockingNode>();
                    if (childDocks != null && childDocks.Count > 0 && !IsBottomOrSurfaceAttachedTo(child, node.part))
                        return childDocks[0];
                }
            }

            return null;
        }

        private static bool IsBottomOrSurfaceAttachedTo(Part part, Part other)
        {
            if (part.srfAttachNode != null && part.srfAttachNode.attachedPart == other)
                return true;
            var bottomNode = part.FindAttachNode("bottom");
            if (bottomNode != null && bottomNode.attachedPart == other)
                return true;
            return false;
        }

        public static ModuleDockingNode FindPartnerByUId(uint dockedPartUId, Vessel sameVesselOnly = null)
        {
            if (dockedPartUId == 0) return null;

            if (sameVesselOnly != null)
            {
                if (sameVesselOnly.parts == null) return null;
                foreach (var part in sameVesselOnly.parts)
                {
                    if (part.flightID == dockedPartUId)
                        return part.FindModulesImplementing<ModuleDockingNode>()?.FirstOrDefault();
                }
                return null;
            }

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (vessel?.parts == null) continue;
                foreach (var part in vessel.parts)
                {
                    if (part.flightID == dockedPartUId)
                        return part.FindModulesImplementing<ModuleDockingNode>()?.FirstOrDefault();
                }
            }

            return null;
        }

        private static void InferDockerDockeeRoles(ModuleDockingNode node, ModuleDockingNode partner,
            out string nodeState, out string partnerState)
        {
            if (!string.IsNullOrEmpty(node.state) && node.state.StartsWith("Docked"))
            {
                nodeState = node.state;
                partnerState = nodeState == "Docked (docker)" ? "Docked (dockee)" : "Docked (docker)";
                return;
            }
            if (!string.IsNullOrEmpty(partner.state) && partner.state.StartsWith("Docked"))
            {
                partnerState = partner.state;
                nodeState = partnerState == "Docked (docker)" ? "Docked (dockee)" : "Docked (docker)";
                return;
            }

            if (node.part?.parent == partner.part)
            {
                nodeState = "Docked (dockee)";
                partnerState = "Docked (docker)";
            }
            else
            {
                nodeState = "Docked (docker)";
                partnerState = "Docked (dockee)";
            }
        }

        private static void RecoverDockedPair(ModuleDockingNode node, ModuleDockingNode partner,
            string nodeState, string partnerState, string reason, Vessel vessel)
        {
            LunaLog.Log($"[LMP]: Recovering docked pair on {vessel.vesselName}: " +
                $"{node.part?.partName}({node.part?.flightID}) -> '{nodeState}', " +
                $"{partner.part?.partName}({partner.part?.flightID}) -> '{partnerState}' [{reason}]");

            node.otherNode = partner;
            partner.otherNode = node;

            if (node.dockedPartUId == 0 && partner.part != null)
                node.dockedPartUId = partner.part.flightID;
            if (partner.dockedPartUId == 0 && node.part != null)
                partner.dockedPartUId = node.part.flightID;

            var dockerNode = nodeState == "Docked (docker)" ? node : partner;
            var dockeeNode = nodeState == "Docked (docker)" ? partner : node;
            var dockerState = nodeState == "Docked (docker)" ? nodeState : partnerState;
            var dockeeState = nodeState == "Docked (docker)" ? partnerState : nodeState;

            if (!IsInDockedState(dockerNode))
            {
                dockerNode.fsm.StartFSM(dockerState);
                LunaLog.Log($"[LMP]: Docker {dockerNode.part?.partName}({dockerNode.part?.flightID}) FSM -> '{dockerNode.fsm.currentStateName}'");
            }

            if (!IsInDockedState(dockeeNode))
            {
                dockeeNode.fsm.StartFSM(dockeeState);
                LunaLog.Log($"[LMP]: Dockee {dockeeNode.part?.partName}({dockeeNode.part?.flightID}) FSM -> '{dockeeNode.fsm.currentStateName}'");
            }
        }

        public static string InferDockedStateForUndock(ModuleDockingNode node)
        {
            if (!string.IsNullOrEmpty(node.state) && node.state.StartsWith("Docked"))
                return node.state;

            if (node.otherNode?.fsm != null)
            {
                var otherState = node.otherNode.fsm.currentStateName;
                if (otherState == "Docked (dockee)" || otherState == "Docked (same vessel)")
                    return "Docked (docker)";
                if (otherState == "Docked (docker)")
                    return "Docked (dockee)";
            }

            return "Docked (docker)";
        }

        public static bool TryRecoverToDockedState(ModuleDockingNode node, string targetState)
        {
            if (node?.fsm == null) return false;

            var currentState = node.fsm.currentStateName;
            LunaLog.Log($"[LMP]: Attempting docking port FSM recovery: '{currentState}' -> '{targetState}' on part {node.part?.partName} (flightID: {node.part?.flightID})");

            if (node.otherNode == null)
            {
                var partner = FindPartnerFromPartTree(node);
                if (partner == null && node.dockedPartUId != 0)
                    partner = FindPartnerByUId(node.dockedPartUId);
                if (partner != null)
                {
                    node.otherNode = partner;
                    partner.otherNode = node;
                }
            }

            node.fsm.StartFSM(targetState);

            var newState = node.fsm.currentStateName;
            if (newState == targetState)
                return true;

            LunaLog.LogWarning($"[LMP]: Docking port FSM recovery failed - state is '{newState}' after StartFSM('{targetState}')");
            return false;
        }

        public static void FixDockingPortFsmStates(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return;

            List<ModuleDockingNode> dockingNodes;
            try
            {
                dockingNodes = vessel.FindPartModulesImplementing<ModuleDockingNode>();
            }
            catch
            {
                return;
            }

            if (dockingNodes == null || dockingNodes.Count == 0) return;

            foreach (var node in dockingNodes)
            {
                if (node?.fsm == null) continue;

                if (IsInDockedState(node))
                {
                    var dockedPartner = FindPartnerFromPartTree(node);

                    if (dockedPartner != null)
                    {
                        if (node.otherNode != dockedPartner)
                            node.otherNode = dockedPartner;
                        if (dockedPartner.otherNode != node)
                            dockedPartner.otherNode = node;
                        if (dockedPartner.part != null && node.dockedPartUId != dockedPartner.part.flightID)
                            node.dockedPartUId = dockedPartner.part.flightID;
                        if (node.part != null && dockedPartner.dockedPartUId != node.part.flightID)
                            dockedPartner.dockedPartUId = node.part.flightID;
                    }
                    else
                    {
                        LunaLog.Log($"[LMP]: Clearing ghost docked state on {vessel.vesselName} part {node.part?.partName} (flightID {node.part?.flightID}) fsm='{node.fsm.currentStateName}' serialized='{node.state}' dockedUId={node.dockedPartUId}");
                        node.otherNode = null;
                        node.dockedPartUId = 0;
                        node.state = "Ready";
                        node.fsm.StartFSM("Ready");
                    }

                    continue;
                }

                var fsmState = node.fsm.currentStateName;
                var serializedState = node.state;

                var partner = FindPartnerFromPartTree(node);
                if (partner == null && node.dockedPartUId != 0)
                {
                    var byUid = FindPartnerByUId(node.dockedPartUId, vessel);
                    if (byUid != null && FindPartnerFromPartTree(byUid) == node)
                        partner = byUid;
                }

                if (partner != null)
                {
                    string nodeState, partnerState;
                    InferDockerDockeeRoles(node, partner, out nodeState, out partnerState);
                    RecoverDockedPair(node, partner, nodeState, partnerState,
                        $"serialized='{serializedState}' fsm='{fsmState}' dockedUId={node.dockedPartUId}", vessel);
                    continue;
                }

                if (node.dockedPartUId != 0 && FindPartnerByUId(node.dockedPartUId) != null)
                {
                    LunaLog.Log($"[LMP]: Clearing stale cross-vessel dockedPartUId={node.dockedPartUId} on {vessel.vesselName} part {node.part?.partName} (flightID {node.part?.flightID})");
                    node.dockedPartUId = 0;
                    node.otherNode = null;
                    if (fsmState != "Ready")
                        node.fsm.StartFSM("Ready");
                    continue;
                }

                if (IsInRecoverableTransientState(node))
                {
                    LunaLog.Log($"[LMP]: Docking port stuck in transient state '{fsmState}' with no partner on {vessel.vesselName} part {node.part?.partName} (flightID {node.part?.flightID}) - resetting to Ready");
                    node.fsm.StartFSM("Ready");
                    continue;
                }

                if (node.dockedPartUId != 0 || node.otherNode != null)
                {
                    LunaLog.Log($"[LMP]: Cleaning stale metadata on {vessel.vesselName} part {node.part?.partName} (flightID {node.part?.flightID}) fsm='{fsmState}' dockedUId={node.dockedPartUId} otherNode={(node.otherNode != null ? node.otherNode.part?.flightID.ToString() : "null")}");
                    node.dockedPartUId = 0;
                    node.otherNode = null;
                }
            }
        }
    }
}
