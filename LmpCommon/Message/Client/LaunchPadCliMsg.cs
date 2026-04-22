using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Client.Base;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Client
{
    public class LaunchPadCliMsg : CliMsgBase<LaunchPadClientBaseMsgData>
    {
        internal LaunchPadCliMsg() { }

        public override string ClassName { get; } = nameof(LaunchPadCliMsg);

        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)LaunchPadClientMessageType.ReserveSite] = typeof(LaunchPadReserveSiteRequestMsgData),
        };

        public override ClientMessageType MessageType => ClientMessageType.LaunchPad;

        protected override int DefaultChannel => 12;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
