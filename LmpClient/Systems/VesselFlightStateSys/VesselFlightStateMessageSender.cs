using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Interface;
using LmpCommon.Time;
using System;

namespace LmpClient.Systems.VesselFlightStateSys
{
    public class VesselFlightStateMessageSender : SubSystem<VesselFlightStateSystem>, IMessageSender
    {
        private const float FlightStateTolerance = 0.001f;
        private const double ForceResendIntervalMs = 500;

        private readonly FlightCtrlState _lastSentFlightState = new FlightCtrlState();
        private DateTime _lastSentTime = DateTime.MinValue;
        private Guid _lastSentVesselId;
        private bool _hasLastSentFlightState;

        public void SendMessage(IMessageData msg)
        {
            NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<VesselCliMsg>(msg));
        }

        public bool SendCurrentFlightState()
        {
            var flightState = new FlightCtrlState();
            flightState.CopyFrom(FlightGlobals.ActiveVessel.ctrlState);
            var vesselId = FlightGlobals.ActiveVessel.id;

            if (!ShouldSendFlightState(vesselId, flightState))
                return false;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<VesselFlightStateMsgData>();
            msgData.PingSec = NetworkStatistics.PingSec;

            msgData.GameTime = TimeSyncSystem.UniversalTime;
            msgData.SubspaceId = WarpSystem.Singleton.CurrentSubspace;

            msgData.VesselId = vesselId;
            msgData.GearDown = flightState.gearDown;
            msgData.GearUp = flightState.gearUp;
            msgData.Headlight = flightState.headlight;
            msgData.KillRot = flightState.killRot;
            msgData.MainThrottle = flightState.mainThrottle;
            msgData.Pitch = flightState.pitch;
            msgData.PitchTrim = flightState.pitchTrim;
            msgData.Roll = flightState.roll;
            msgData.RollTrim = flightState.rollTrim;
            msgData.WheelSteer = flightState.wheelSteer;
            msgData.WheelSteerTrim = flightState.wheelSteerTrim;
            msgData.WheelThrottle = flightState.wheelThrottle;
            msgData.WheelThrottleTrim = flightState.wheelThrottleTrim;
            msgData.X = flightState.X;
            msgData.Y = flightState.Y;
            msgData.Yaw = flightState.yaw;
            msgData.YawTrim = flightState.yawTrim;
            msgData.Z = flightState.Z;

            SendMessage(msgData);
            RememberSentFlightState(vesselId, flightState);
            return true;
        }

        public void ResetLastSentState()
        {
            _hasLastSentFlightState = false;
            _lastSentTime = DateTime.MinValue;
        }

        private bool ShouldSendFlightState(Guid vesselId, FlightCtrlState flightState)
        {
            if (!_hasLastSentFlightState || _lastSentVesselId != vesselId)
                return true;

            if ((LunaComputerTime.UtcNow - _lastSentTime).TotalMilliseconds >= ForceResendIntervalMs)
                return true;

            return !Equivalent(_lastSentFlightState, flightState);
        }

        private void RememberSentFlightState(Guid vesselId, FlightCtrlState flightState)
        {
            _lastSentVesselId = vesselId;
            _lastSentFlightState.CopyFrom(flightState);
            _lastSentTime = LunaComputerTime.UtcNow;
            _hasLastSentFlightState = true;
        }

        private static bool Equivalent(FlightCtrlState a, FlightCtrlState b)
        {
            return a.killRot == b.killRot &&
                   a.gearUp == b.gearUp &&
                   a.gearDown == b.gearDown &&
                   a.headlight == b.headlight &&
                   NearlyEqual(a.mainThrottle, b.mainThrottle) &&
                   NearlyEqual(a.wheelThrottle, b.wheelThrottle) &&
                   NearlyEqual(a.wheelThrottleTrim, b.wheelThrottleTrim) &&
                   NearlyEqual(a.X, b.X) &&
                   NearlyEqual(a.Y, b.Y) &&
                   NearlyEqual(a.Z, b.Z) &&
                   NearlyEqual(a.pitch, b.pitch) &&
                   NearlyEqual(a.roll, b.roll) &&
                   NearlyEqual(a.yaw, b.yaw) &&
                   NearlyEqual(a.pitchTrim, b.pitchTrim) &&
                   NearlyEqual(a.rollTrim, b.rollTrim) &&
                   NearlyEqual(a.yawTrim, b.yawTrim) &&
                   NearlyEqual(a.wheelSteer, b.wheelSteer) &&
                   NearlyEqual(a.wheelSteerTrim, b.wheelSteerTrim);
        }

        private static bool NearlyEqual(float a, float b)
        {
            return Math.Abs(a - b) <= FlightStateTolerance;
        }
    }
}
