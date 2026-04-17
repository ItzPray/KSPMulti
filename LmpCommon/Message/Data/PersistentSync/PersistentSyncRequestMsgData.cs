using Lidgren.Network;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    public class PersistentSyncRequestMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncRequestMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType => Message.Types.PersistentSyncMessageType.Request;

        public int DomainCount;
        public PersistentSyncDomainId[] Domains = new PersistentSyncDomainId[0];

        public override string ClassName { get; } = nameof(PersistentSyncRequestMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(DomainCount);
            for (var i = 0; i < DomainCount; i++)
            {
                lidgrenMsg.Write((byte)Domains[i]);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            DomainCount = lidgrenMsg.ReadInt32();
            if (Domains.Length < DomainCount)
            {
                Domains = new PersistentSyncDomainId[DomainCount];
            }

            for (var i = 0; i < DomainCount; i++)
            {
                Domains[i] = (PersistentSyncDomainId)lidgrenMsg.ReadByte();
            }
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(int) + DomainCount * sizeof(byte);
        }
    }
}
