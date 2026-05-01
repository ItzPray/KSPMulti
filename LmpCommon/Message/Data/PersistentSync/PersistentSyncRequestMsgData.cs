using Lidgren.Network;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    public class PersistentSyncRequestMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncRequestMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType => Message.Types.PersistentSyncMessageType.Request;

        public int DomainCount;
        public ushort[] DomainWireIds = new ushort[0];
        public PersistentSyncDomainId[] Domains
        {
            get
            {
                var result = new PersistentSyncDomainId[DomainWireIds.Length];
                for (var i = 0; i < DomainWireIds.Length; i++)
                {
                    result[i] = PersistentSyncDomainCatalog.TryGetByWireId(DomainWireIds[i], out var definition)
                        ? definition.DomainId
                        : (PersistentSyncDomainId)DomainWireIds[i];
                }

                return result;
            }
            set
            {
                var source = value ?? new PersistentSyncDomainId[0];
                DomainWireIds = new ushort[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    DomainWireIds[i] = PersistentSyncDomainCatalog.TryGet(source[i], out var definition)
                        ? definition.WireId
                        : (ushort)source[i];
                }
            }
        }

        public override string ClassName { get; } = nameof(PersistentSyncRequestMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(DomainCount);
            for (var i = 0; i < DomainCount; i++)
            {
                lidgrenMsg.Write(DomainWireIds[i]);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            DomainCount = lidgrenMsg.ReadInt32();
            if (DomainWireIds.Length < DomainCount)
            {
                DomainWireIds = new ushort[DomainCount];
            }

            for (var i = 0; i < DomainCount; i++)
            {
                DomainWireIds[i] = lidgrenMsg.ReadUInt16();
            }
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(int) + DomainCount * sizeof(ushort);
        }
    }
}
