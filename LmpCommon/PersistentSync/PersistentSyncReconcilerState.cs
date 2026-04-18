using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    public class PersistentSyncReconcilerState
    {
        private readonly Dictionary<PersistentSyncDomainId, long> _lastAppliedRevision = new Dictionary<PersistentSyncDomainId, long>();
        private readonly Dictionary<PersistentSyncDomainId, bool> _hasInitialSnapshot = new Dictionary<PersistentSyncDomainId, bool>();
        private readonly Dictionary<PersistentSyncDomainId, bool> _initialJoinHandshakeComplete = new Dictionary<PersistentSyncDomainId, bool>();
        private readonly Dictionary<PersistentSyncDomainId, PersistentSyncBufferedSnapshot> _pendingSnapshots = new Dictionary<PersistentSyncDomainId, PersistentSyncBufferedSnapshot>();
        private HashSet<PersistentSyncDomainId> _requiredDomains = new HashSet<PersistentSyncDomainId>();

        public void Reset(IEnumerable<PersistentSyncDomainId> requiredDomains)
        {
            _requiredDomains = new HashSet<PersistentSyncDomainId>(requiredDomains ?? Enumerable.Empty<PersistentSyncDomainId>());
            _lastAppliedRevision.Clear();
            _hasInitialSnapshot.Clear();
            _initialJoinHandshakeComplete.Clear();
            _pendingSnapshots.Clear();
        }

        public IReadOnlyCollection<PersistentSyncDomainId> RequiredDomains => _requiredDomains.ToArray();

        public bool ShouldIgnoreSnapshot(PersistentSyncDomainId domainId, long revision)
        {
            var highest = GetHighestKnownRevision(domainId);
            if (revision < highest)
            {
                return true;
            }

            if (revision > highest)
            {
                return false;
            }

            // revision == highest
            if (HasInitialSnapshot(domainId))
            {
                return true;
            }

            // Same revision already captured as deferred (duplicate network delivery before apply).
            if (_pendingSnapshots.TryGetValue(domainId, out var pending) && pending.Revision == revision)
            {
                return true;
            }

            return false;
        }

        public void MarkApplied(PersistentSyncDomainId domainId, long revision)
        {
            var previousApplied = _lastAppliedRevision.TryGetValue(domainId, out var p) ? p : 0L;
            var applied = Math.Max(previousApplied, revision);
            _lastAppliedRevision[domainId] = applied;
            _hasInitialSnapshot[domainId] = true;
            _initialJoinHandshakeComplete[domainId] = true;

            if (_pendingSnapshots.TryGetValue(domainId, out var pending) && pending.Revision <= applied)
            {
                _pendingSnapshots.Remove(domainId);
            }
        }

        /// <summary>
        /// Initial network join may receive and deserialize snapshots while live game objects are missing
        /// (main menu). That must advance the join state machine without setting <see cref="HasInitialSnapshot"/>,
        /// otherwise <see cref="ShouldIgnoreSnapshot"/> would drop re-sent payloads at the same revision before
        /// <see cref="MarkApplied"/> runs (e.g. KSC facility levels never applied).
        /// </summary>
        public void MarkInitialJoinHandshakeComplete(PersistentSyncDomainId domainId)
        {
            _initialJoinHandshakeComplete[domainId] = true;
        }

        /// <summary>
        /// True once a domain has either live-applied a snapshot revision (<see cref="HasInitialSnapshot"/>)
        /// or completed the join-time deserialize/defer path (<see cref="MarkInitialJoinHandshakeComplete"/>).
        /// </summary>
        public bool IsInitialJoinHandshakeComplete(PersistentSyncDomainId domainId)
        {
            return HasInitialSnapshot(domainId) ||
                   (_initialJoinHandshakeComplete.TryGetValue(domainId, out var done) && done);
        }

        /// <summary>
        /// Used to leave <c>ClientState.SyncingPersistentState</c>; does not require live KSP singleton writes.
        /// </summary>
        public bool AreAllJoinHandshakesComplete()
        {
            return _requiredDomains.All(IsInitialJoinHandshakeComplete);
        }

        private long GetHighestKnownRevision(PersistentSyncDomainId domainId)
        {
            var lastApplied = _lastAppliedRevision.TryGetValue(domainId, out var applied) ? applied : 0L;
            if (!_pendingSnapshots.TryGetValue(domainId, out var pending))
            {
                return lastApplied;
            }

            return Math.Max(lastApplied, pending.Revision);
        }

        public long GetLastAppliedRevision(PersistentSyncDomainId domainId)
        {
            return _lastAppliedRevision.TryGetValue(domainId, out var revision) ? revision : 0;
        }

        public bool HasInitialSnapshot(PersistentSyncDomainId domainId)
        {
            return _hasInitialSnapshot.TryGetValue(domainId, out var hasInitial) && hasInitial;
        }

        /// <summary>
        /// True only after each required domain has live-committed its initial snapshot (see <see cref="MarkApplied"/>).
        /// Used before advancing into lock sync.
        /// </summary>
        public bool AreAllInitialSnapshotsApplied()
        {
            return _requiredDomains.All(HasInitialSnapshot);
        }

        public void StoreDeferred(PersistentSyncBufferedSnapshot snapshot)
        {
            _pendingSnapshots[snapshot.DomainId] = snapshot;
        }

        public bool TryGetDeferred(PersistentSyncDomainId domainId, out PersistentSyncBufferedSnapshot snapshot)
        {
            return _pendingSnapshots.TryGetValue(domainId, out snapshot);
        }

        public IReadOnlyCollection<PersistentSyncBufferedSnapshot> GetDeferredSnapshots()
        {
            return _pendingSnapshots.Values.ToArray();
        }

        public void ClearDeferred(PersistentSyncDomainId domainId)
        {
            _pendingSnapshots.Remove(domainId);
        }
    }
}
