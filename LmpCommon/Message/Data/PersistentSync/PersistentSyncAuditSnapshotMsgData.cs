using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    /// <summary>
    /// Server canonical snapshot for audit/compare (client must not route through normal reconciler apply).
    /// </summary>
    public class PersistentSyncAuditSnapshotMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncAuditSnapshotMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType =>
            Message.Types.PersistentSyncMessageType.AuditSnapshot;

        public int CorrelationId;

        /// <summary>Empty when <see cref="Error"/> is non-empty or payload unavailable.</summary>
        public string Error = string.Empty;

        public ushort DomainWireId;

        public string DomainId
        {
            get => PersistentSyncDomainCatalog.TryGetByWireId(DomainWireId, out var definition)
                ? definition.DomainId
                : string.Empty;
            set => DomainWireId = PersistentSyncDomainCatalog.Get(value).WireId;
        }

        public long Revision = -1;
        public PersistentAuthorityPolicy AuthorityPolicy;
        public byte[] Payload = new byte[0];
        public int NumBytes;

        public override string ClassName { get; } = nameof(PersistentSyncAuditSnapshotMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(CorrelationId);
            lidgrenMsg.Write(Error ?? string.Empty);
            lidgrenMsg.Write(DomainWireId);
            lidgrenMsg.Write(Revision);
            lidgrenMsg.Write((byte)AuthorityPolicy);
            lidgrenMsg.Write(NumBytes);
            lidgrenMsg.Write(Payload, 0, NumBytes);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            CorrelationId = lidgrenMsg.ReadInt32();
            Error = lidgrenMsg.ReadString();
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
            return base.InternalGetMessageSize() + sizeof(int) + (Error ?? string.Empty).GetByteCount() + sizeof(ushort) +
                   sizeof(long) + sizeof(byte) + sizeof(int) + NumBytes;
        }
    }
}
