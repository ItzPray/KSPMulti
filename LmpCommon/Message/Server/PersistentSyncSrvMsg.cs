using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Server
{
    public class PersistentSyncSrvMsg : SrvMsgBase<PersistentSyncBaseMsgData>
    {
        internal PersistentSyncSrvMsg() { }

        public override string ClassName { get; } = nameof(PersistentSyncSrvMsg);

        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)PersistentSyncMessageType.Snapshot] = typeof(PersistentSyncSnapshotMsgData),
            [(ushort)PersistentSyncMessageType.ProducerOfferGenerationNudge] =
                typeof(PersistentSyncProducerOfferGenerationNudgeMsgData),
            [(ushort)PersistentSyncMessageType.AuditSnapshot] = typeof(PersistentSyncAuditSnapshotMsgData)
        };

        public override ServerMessageType MessageType => ServerMessageType.PersistentSync;

        protected override int DefaultChannel => 23;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
