using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Server
{
    public class LaunchPadSrvMsg : SrvMsgBase<LaunchPadBaseMsgData>
    {
        internal LaunchPadSrvMsg() { }

        public override string ClassName { get; } = nameof(LaunchPadSrvMsg);

        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)LaunchPadMessageType.OccupancySnapshot] = typeof(LaunchPadOccupancySnapshotMsgData),
            [(ushort)LaunchPadMessageType.LaunchDenied] = typeof(LaunchPadLaunchDeniedMsgData),
            [(ushort)LaunchPadMessageType.OccupancyDelta] = typeof(LaunchPadOccupancyDeltaMsgData),
            [(ushort)LaunchPadMessageType.ReserveSiteReply] = typeof(LaunchPadReserveSiteReplyMsgData),
        };

        public override ServerMessageType MessageType => ServerMessageType.LaunchPad;

        protected override int DefaultChannel => 12;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
