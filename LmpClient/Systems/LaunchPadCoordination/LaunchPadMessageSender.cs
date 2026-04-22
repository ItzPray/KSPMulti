using System;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.LaunchPadCoordination
{
    public class LaunchPadMessageSender : SubSystem<LaunchPadCoordinationSystem>, IMessageSender
    {
        private static readonly object ReserveThrottleGate = new object();
        private static string _lastReserveSite;
        private static long _lastReserveTicks;

        public void SendMessage(IMessageData msg)
        {
            // Reserved for future client→server launch pad payloads.
        }

        /// <summary>Plan B: ask server to hold a pad before PRELAUNCH proto exists. Throttled per site.</summary>
        public void SendReserveSiteRequest(string siteKey)
        {
            if (string.IsNullOrWhiteSpace(siteKey))
                return;

            lock (ReserveThrottleGate)
            {
                var now = DateTime.UtcNow.Ticks;
                if (string.Equals(_lastReserveSite, siteKey, StringComparison.Ordinal) &&
                    now - _lastReserveTicks < TimeSpan.TicksPerSecond / 4)
                    return;

                _lastReserveSite = siteKey;
                _lastReserveTicks = now;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<LaunchPadReserveSiteRequestMsgData>();
            msgData.SiteKey = siteKey.Trim();
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<LaunchPadCliMsg>(msgData)));
        }
    }
}
