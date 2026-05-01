using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    public class PersistentSyncSnapshotMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncSnapshotMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType => Message.Types.PersistentSyncMessageType.Snapshot;

        public ushort DomainWireId;
        public string DomainId
        {
            get => PersistentSyncDomainCatalog.TryGetByWireId(DomainWireId, out var definition)
                ? definition.DomainId
                : (PersistentSyncDomainNaming.TryGetKnownName(DomainWireId, out var knownName) ? knownName : string.Empty);
            set => DomainWireId = PersistentSyncDomainCatalog.TryGet(value, out var definition)
                ? definition.WireId
                : PersistentSyncDomainNaming.GetKnownWireId(value);
        }
        public long Revision;
        public PersistentAuthorityPolicy AuthorityPolicy;
        public byte[] Payload = new byte[0];
        public int NumBytes;

        public override string ClassName { get; } = nameof(PersistentSyncSnapshotMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(DomainWireId);
            lidgrenMsg.Write(Revision);
            lidgrenMsg.Write((byte)AuthorityPolicy);
            lidgrenMsg.Write(NumBytes);
            lidgrenMsg.Write(Payload, 0, NumBytes);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            DomainWireId = lidgrenMsg.ReadUInt16();
            Revision = lidgrenMsg.ReadInt64();
            AuthorityPolicy = (PersistentAuthorityPolicy)lidgrenMsg.ReadByte();
            NumBytes = lidgrenMsg.ReadInt32();

            if (Payload.Length < NumBytes)
            {
                Payload = new byte[NumBytes];
            }

            lidgrenMsg.ReadBytes(Payload, 0, NumBytes);
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(int) + NumBytes;
        }
    }
}
