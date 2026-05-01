using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    public class PersistentSyncIntentMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncIntentMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType => Message.Types.PersistentSyncMessageType.Intent;

        public ushort DomainWireId;
        public PersistentSyncDomainId DomainId
        {
            get => PersistentSyncDomainCatalog.TryGetByWireId(DomainWireId, out var definition)
                ? definition.DomainId
                : (PersistentSyncDomainId)DomainWireId;
            set => DomainWireId = PersistentSyncDomainCatalog.TryGet(value, out var definition)
                ? definition.WireId
                : (ushort)value;
        }
        public long ClientKnownRevision;
        public byte[] Payload = new byte[0];
        public int NumBytes;
        public string Reason;

        public override string ClassName { get; } = nameof(PersistentSyncIntentMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(DomainWireId);
            lidgrenMsg.Write(ClientKnownRevision);
            lidgrenMsg.Write(NumBytes);
            lidgrenMsg.Write(Payload, 0, NumBytes);
            lidgrenMsg.Write(Reason ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            DomainWireId = lidgrenMsg.ReadUInt16();
            ClientKnownRevision = lidgrenMsg.ReadInt64();
            NumBytes = lidgrenMsg.ReadInt32();

            if (Payload.Length < NumBytes)
            {
                Payload = new byte[NumBytes];
            }

            lidgrenMsg.ReadBytes(Payload, 0, NumBytes);
            Reason = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(ushort) + sizeof(long) + sizeof(int) + NumBytes + (Reason ?? string.Empty).GetByteCount();
        }
    }
}
