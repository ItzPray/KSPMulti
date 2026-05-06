using Lidgren.Network;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.PersistentSync
{
    /// <summary>
    /// Compare-only pull of server persistent-sync canonical payloads (does not mutate server state).
    /// </summary>
    public class PersistentSyncAuditRequestMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncAuditRequestMsgData() { }

        public override Message.Types.PersistentSyncMessageType PersistentSyncMessageType =>
            Message.Types.PersistentSyncMessageType.AuditRequest;

        /// <summary>Correlates multiple <see cref="PersistentSyncAuditSnapshotMsgData"/> responses.</summary>
        public int CorrelationId;

        /// <summary>Reserved for future trimmed responses; server currently always returns payload bytes when successful.</summary>
        public bool IncludeRawPayload = true;

        public int DomainCount;
        public ushort[] DomainWireIds = new ushort[0];

        public string[] Domains
        {
            get
            {
                var result = new string[DomainWireIds.Length];
                for (var i = 0; i < DomainWireIds.Length; i++)
                {
                    result[i] = PersistentSyncDomainCatalog.TryGetByWireId(DomainWireIds[i], out var definition)
                        ? definition.DomainId
                        : string.Empty;
                }

                return result;
            }
            set
            {
                var source = value ?? new string[0];
                DomainCount = source.Length;
                DomainWireIds = new ushort[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    DomainWireIds[i] = PersistentSyncDomainCatalog.Get(source[i]).WireId;
                }
            }
        }

        public override string ClassName { get; } = nameof(PersistentSyncAuditRequestMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(CorrelationId);
            lidgrenMsg.Write(IncludeRawPayload);
            lidgrenMsg.Write(DomainCount);
            for (var i = 0; i < DomainCount; i++)
            {
                lidgrenMsg.Write(DomainWireIds[i]);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            CorrelationId = lidgrenMsg.ReadInt32();
            IncludeRawPayload = lidgrenMsg.ReadBoolean();
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
            return base.InternalGetMessageSize() + sizeof(int) + sizeof(bool) + sizeof(int) + DomainCount * sizeof(ushort);
        }
    }
}
