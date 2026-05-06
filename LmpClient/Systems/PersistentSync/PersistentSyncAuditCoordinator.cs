#if DEBUG
using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Audit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// DEBUG-only: buffers audit snapshots (never routes through <see cref="PersistentSyncReconciler"/>).
    /// </summary>
    public sealed class PersistentSyncAuditCoordinator
    {
        public static PersistentSyncAuditCoordinator Instance { get; } = new PersistentSyncAuditCoordinator();

        private static int _nextCorrelation = 1;

        private int _activeCorrelationId = -1;
        private string[] _pendingDomainOrder = Array.Empty<string>();
        private readonly Dictionary<string, PersistentSyncAuditSnapshotMsgData> _pendingSnapshots =
            new Dictionary<string, PersistentSyncAuditSnapshotMsgData>(StringComparer.Ordinal);

        private readonly Dictionary<string, PersistentSyncDomainAuditRow> _lastRows =
            new Dictionary<string, PersistentSyncDomainAuditRow>(StringComparer.Ordinal);

        /// <summary>Fires on main thread when a refresh batch completes (success or partial).</summary>
        public event Action RefreshCompleted;

        public bool RefreshInProgress { get; private set; }

        public DateTime LastCompletedUtc { get; private set; }

        public int LastCorrelationId { get; private set; } = -1;

        public IReadOnlyDictionary<string, PersistentSyncDomainAuditRow> LastRows => _lastRows;

        private PersistentSyncAuditCoordinator()
        {
        }

        public bool TryBeginRefreshAll(out string failureReason)
        {
            var ids = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId).ToArray();
            return TryBeginRefresh(ids, out failureReason);
        }

        public bool TryBeginRefresh(IReadOnlyList<string> domainIds, out string failureReason)
        {
            failureReason = null;
            if (domainIds == null || domainIds.Count == 0)
            {
                failureReason = "No domains selected.";
                return false;
            }

            if (MainSystem.NetworkState < ClientState.Running)
            {
                failureReason = "Not connected.";
                return false;
            }

            if (PersistentSyncSystem.Singleton == null || !PersistentSyncSystem.Singleton.Enabled)
            {
                failureReason = "Persistent sync is not active.";
                return false;
            }

            var ids = domainIds.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
            {
                failureReason = "No valid domain ids.";
                return false;
            }

            var correlationId = System.Threading.Interlocked.Increment(ref _nextCorrelation);
            _activeCorrelationId = correlationId;
            _pendingDomainOrder = ids;
            _pendingSnapshots.Clear();
            RefreshInProgress = true;

            PersistentSyncSystem.Singleton.MessageSender.SendAuditRequest(correlationId, true, ids);
            return true;
        }

        public void HandleAuditSnapshot(PersistentSyncAuditSnapshotMsgData data)
        {
            if (data == null || data.CorrelationId != _activeCorrelationId || !RefreshInProgress)
            {
                return;
            }

            if (!TryResolveDomainKey(data, out var domainKey))
            {
                return;
            }

            _pendingSnapshots[domainKey] = data;

            if (!IsBatchComplete())
            {
                return;
            }

            FinalizeBatch();
        }

        private bool IsBatchComplete()
        {
            foreach (var id in _pendingDomainOrder)
            {
                if (!_pendingSnapshots.ContainsKey(id))
                {
                    return false;
                }
            }

            return true;
        }

        private void FinalizeBatch()
        {
            try
            {
                _lastRows.Clear();
                var system = PersistentSyncSystem.Singleton;
                if (system == null)
                {
                    return;
                }

                foreach (var domainId in _pendingDomainOrder)
                {
                    _pendingSnapshots.TryGetValue(domainId, out var snap);
                    BuildRow(system, domainId, snap);
                }

                LastCorrelationId = _activeCorrelationId;
                LastCompletedUtc = DateTime.UtcNow;
            }
            finally
            {
                RefreshInProgress = false;
                _activeCorrelationId = -1;
                _pendingSnapshots.Clear();
                RefreshCompleted?.Invoke();
            }
        }

        private void BuildRow(PersistentSyncSystem system, string domainId, PersistentSyncAuditSnapshotMsgData snap)
        {
            var row = new PersistentSyncDomainAuditRow { DomainId = domainId };

            if (PersistentSyncDomainCatalog.TryGet(domainId, out var def))
            {
                row.WireId = def.WireId;
            }

            row.ClientKnownRevision = system.GetKnownRevision(domainId);

            if (snap != null)
            {
                row.ServerRevision = snap.Revision;
                row.AuthorityPolicy = snap.AuthorityPolicy;
                row.ServerError = snap.Error ?? string.Empty;
                row.ServerPayloadBytes = snap.NumBytes;
                row.ServerHash8 = Hash8Prefix(snap.Payload, snap.NumBytes);
                row.ServerPreviewHex = ToHexPreview(snap.Payload, snap.NumBytes, 128);

                if (!system.Domains.TryGetValue(domainId, out var domain))
                {
                    row.LocalUnavailableReason = "Client domain not registered.";
                    row.Comparison = PersistentSyncAuditComparer.Compare(
                        domainId,
                        Array.Empty<byte>(),
                        0,
                        snap.Payload,
                        snap.NumBytes,
                        row.ClientKnownRevision,
                        snap.Revision,
                        snap.Error ?? string.Empty);
                }
                else if (domain.TrySerializeLocalAuditPayload(out var localBytes, out var localNum, out var unavail))
                {
                    row.LocalPayloadBytes = localNum;
                    row.LocalHash8 = Hash8Prefix(localBytes, localNum);
                    row.LocalPreviewHex = ToHexPreview(localBytes, localNum, 128);
                    row.Comparison = PersistentSyncAuditComparer.Compare(
                        domainId,
                        localBytes,
                        localNum,
                        snap.Payload,
                        snap.NumBytes,
                        row.ClientKnownRevision,
                        snap.Revision,
                        snap.Error ?? string.Empty);
                }
                else
                {
                    row.LocalUnavailableReason = string.IsNullOrEmpty(unavail) ? "Local audit unavailable." : unavail;
                    row.Comparison = PersistentSyncAuditComparer.Compare(
                        domainId,
                        Array.Empty<byte>(),
                        0,
                        snap.Payload,
                        snap.NumBytes,
                        row.ClientKnownRevision,
                        snap.Revision,
                        snap.Error ?? string.Empty);
                }
            }
            else
            {
                row.ServerError = "Missing server response for this domain.";
                row.Comparison = new PersistentSyncAuditComparisonResult
                {
                    PrimaryKind = PersistentSyncAuditDifferenceKind.ServerError,
                    Severity = PersistentSyncAuditSeverity.Error,
                    Summary = row.ServerError
                };
            }

            _lastRows[domainId] = row;
        }

        private static bool TryResolveDomainKey(PersistentSyncAuditSnapshotMsgData msg, out string domainId)
        {
            domainId = msg.DomainId;
            if (!string.IsNullOrEmpty(domainId))
            {
                return true;
            }

            if (PersistentSyncDomainCatalog.TryGetByWireId(msg.DomainWireId, out var def))
            {
                domainId = def.DomainId;
                return true;
            }

            if (!string.IsNullOrEmpty(msg.Error))
            {
                var idx = msg.Error.IndexOf(':');
                if (idx > 0 && idx + 1 < msg.Error.Length)
                {
                    var tail = msg.Error.Substring(idx + 1);
                    if (!string.IsNullOrEmpty(tail) && tail != "<empty>")
                    {
                        domainId = tail;
                        return true;
                    }
                }
            }

            domainId = null;
            return false;
        }

        private static string ToHexPreview(byte[] payload, int numBytes, int maxBytes)
        {
            if (payload == null || numBytes <= 0)
            {
                return string.Empty;
            }

            var n = Math.Min(numBytes, maxBytes);
            var sb = new StringBuilder(n * 3);
            for (var i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(payload[i].ToString("x2"));
            }

            if (numBytes > maxBytes)
            {
                sb.Append(" …");
            }

            return sb.ToString();
        }

        private static string Hash8Prefix(byte[] payload, int numBytes)
        {
            if (payload == null || numBytes <= 0)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(payload, 0, numBytes);
                var sb = new StringBuilder(16);
                for (var i = 0; i < 8 && i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }

    public sealed class PersistentSyncDomainAuditRow
    {
        public string DomainId { get; set; }

        public ushort WireId { get; set; }

        public long ClientKnownRevision { get; set; }

        public long ServerRevision { get; set; }

        public PersistentAuthorityPolicy AuthorityPolicy { get; set; }

        public string ServerError { get; set; }

        public int LocalPayloadBytes { get; set; }

        public int ServerPayloadBytes { get; set; }

        public string LocalHash8 { get; set; }

        public string ServerHash8 { get; set; }

        public string LocalPreviewHex { get; set; }

        public string ServerPreviewHex { get; set; }

        public string LocalUnavailableReason { get; set; }

        public PersistentSyncAuditComparisonResult Comparison { get; set; }
    }
}
#endif
