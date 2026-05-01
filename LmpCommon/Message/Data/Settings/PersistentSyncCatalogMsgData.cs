using Lidgren.Network;
using LmpCommon.Message.Types;
using LmpCommon.PersistentSync;

namespace LmpCommon.Message.Data.Settings
{
    /// <summary>
    /// Dedicated settings-channel carrier for the persistent-sync domain catalog (wire ids and metadata).
    /// </summary>
    public sealed class PersistentSyncCatalogMsgData : SettingsBaseMsgData
    {
        internal PersistentSyncCatalogMsgData() { }

        public override SettingsMessageType SettingsMessageType => SettingsMessageType.PersistentSyncCatalog;

        /// <summary>0 = empty rows / registry not ready (client uses local wire map).</summary>
        public byte PersistentSyncCatalogWireVersion;

        public PersistentSyncCatalogRowWire[] PersistentSyncCatalogRows = System.Array.Empty<PersistentSyncCatalogRowWire>();

        public override string ClassName { get; } = nameof(PersistentSyncCatalogMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            PersistentSyncCatalogWire.WriteCatalog(lidgrenMsg, PersistentSyncCatalogWireVersion, PersistentSyncCatalogRows);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            PersistentSyncCatalogWire.ReadCatalog(lidgrenMsg, out PersistentSyncCatalogWireVersion, out PersistentSyncCatalogRows);
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + PersistentSyncCatalogWire.GetCatalogByteCount(PersistentSyncCatalogRows);
        }
    }
}
