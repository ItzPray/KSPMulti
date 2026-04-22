using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Harmony;
using LmpClient.Utilities;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Interface;
using System.Collections.Concurrent;
using UnityEngine;

namespace LmpClient.Systems.LaunchPadCoordination
{
    public class LaunchPadMessageHandler : SubSystem<LaunchPadCoordinationSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            switch (msg.Data)
            {
                case LaunchPadOccupancySnapshotMsgData snap:
                    LaunchPadCoordinationSystem.Singleton.ApplySnapshot(snap);
                    LaunchPadStockLaunchSiteUi.RefreshAllInScene();
                    break;
                case LaunchPadOccupancyDeltaMsgData delta:
                    LaunchPadCoordinationSystem.Singleton.ApplyDelta(delta);
                    LaunchPadStockLaunchSiteUi.RefreshAllInScene();
                    break;
                case LaunchPadReserveSiteReplyMsgData reserve:
                    if (!reserve.Granted && !string.IsNullOrEmpty(reserve.Reason))
                        LunaScreenMsg.PostScreenMessage(reserve.Reason, 10f, ScreenMessageStyle.UPPER_CENTER);
                    LaunchPadStockLaunchSiteUi.RefreshAllInScene();
                    break;
                case LaunchPadLaunchDeniedMsgData denied:
                    if (!string.IsNullOrEmpty(denied.Reason))
                        LunaScreenMsg.PostScreenMessage(denied.Reason, 12f, ScreenMessageStyle.UPPER_CENTER);
                    break;
            }
        }
    }
}
