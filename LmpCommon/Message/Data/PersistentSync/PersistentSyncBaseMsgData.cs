using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;
using System;

namespace LmpCommon.Message.Data.PersistentSync
{
    public abstract class PersistentSyncBaseMsgData : MessageData
    {
        internal PersistentSyncBaseMsgData() { }

        public override ushort SubType => (ushort)PersistentSyncMessageType;

        public virtual PersistentSyncMessageType PersistentSyncMessageType => throw new NotImplementedException();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
        }

        internal override int InternalGetMessageSize()
        {
            return 0;
        }
    }
}
