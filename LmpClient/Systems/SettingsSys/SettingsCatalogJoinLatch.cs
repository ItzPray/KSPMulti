using System;
using LmpCommon.Message.Data.Settings;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.SettingsSys
{
    /// <summary>
    /// Buffers settings reply vs persistent-sync catalog until both arrive, then the handler merges in plan order via <see cref="SettingsCatalogJoinMerge"/>.
    /// </summary>
    internal static class SettingsCatalogJoinLatch
    {
        private static SettingsReplyMsgData _bufferedReply;
        private static byte _bufferedCatalogWireVersion;
        private static PersistentSyncCatalogRowWire[] _bufferedCatalogRows = Array.Empty<PersistentSyncCatalogRowWire>();
        private static bool _hasReply;
        private static bool _hasCatalog;

        internal static bool HasBufferedReply => _hasReply;

        internal static bool HasBufferedCatalog => _hasCatalog;

        internal static void Reset()
        {
            _bufferedReply = null;
            _bufferedCatalogRows = Array.Empty<PersistentSyncCatalogRowWire>();
            _hasReply = false;
            _hasCatalog = false;
        }

        internal static void BufferReply(SettingsReplyMsgData reply)
        {
            _bufferedReply = reply?.CloneForBuffering();
            _hasReply = true;
        }

        internal static void BufferCatalog(byte wireVersion, PersistentSyncCatalogRowWire[] rows)
        {
            _bufferedCatalogWireVersion = wireVersion;
            _bufferedCatalogRows = CloneCatalogRows(rows);
            _hasCatalog = true;
        }

        internal static bool TryTakeBufferedReply(out SettingsReplyMsgData reply)
        {
            if (!_hasReply)
            {
                reply = null;
                return false;
            }

            reply = _bufferedReply;
            _bufferedReply = null;
            _hasReply = false;
            return true;
        }

        internal static bool TryTakeBufferedCatalog(out byte wireVersion, out PersistentSyncCatalogRowWire[] rows)
        {
            if (!_hasCatalog)
            {
                wireVersion = 0;
                rows = null;
                return false;
            }

            wireVersion = _bufferedCatalogWireVersion;
            rows = _bufferedCatalogRows;
            _bufferedCatalogRows = Array.Empty<PersistentSyncCatalogRowWire>();
            _hasCatalog = false;
            return true;
        }

        private static PersistentSyncCatalogRowWire[] CloneCatalogRows(PersistentSyncCatalogRowWire[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                return Array.Empty<PersistentSyncCatalogRowWire>();
            }

            return (PersistentSyncCatalogRowWire[])rows.Clone();
        }
    }
}
