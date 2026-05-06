using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Client.Base;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Client
{
    public class PersistentSyncCliMsg : CliMsgBase<PersistentSyncBaseMsgData>
    {
        internal PersistentSyncCliMsg() { }

        public override string ClassName { get; } = nameof(PersistentSyncCliMsg);

        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)PersistentSyncMessageType.Request] = typeof(PersistentSyncRequestMsgData),
            [(ushort)PersistentSyncMessageType.Intent] = typeof(PersistentSyncIntentMsgData),
            [(ushort)PersistentSyncMessageType.AuditRequest] = typeof(PersistentSyncAuditRequestMsgData)
        };

        public override ClientMessageType MessageType => ClientMessageType.PersistentSync;

        protected override int DefaultChannel => 22;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
