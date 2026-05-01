using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    public class PersistentSyncReconcilerState
    {
        private readonly Dictionary<string, long> _lastAppliedRevision = new Dictionary<string, long>();
        private readonly Dictionary<string, bool> _hasInitialSnapshot = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _initialJoinHandshakeComplete = new Dictionary<string, bool>();
        private readonly Dictionary<string, PersistentSyncBufferedSnapshot> _pendingSnapshots = new Dictionary<string, PersistentSyncBufferedSnapshot>();
        private HashSet<string> _requiredDomains = new HashSet<string>();

        public void Reset(IEnumerable<string> requiredDomains)
        {
            _requiredDomains = new HashSet<string>(requiredDomains ?? Enumerable.Empty<string>());
            _lastAppliedRevision.Clear();
            _hasInitialSnapshot.Clear();
            _initialJoinHandshakeComplete.Clear();
            _pendingSnapshots.Clear();
        }

        public IReadOnlyCollection<string> RequiredDomains => _requiredDomains.ToArray();

        public bool ShouldIgnoreSnapshot(string domainId, long revision)
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

        public void MarkApplied(string domainId, long revision)
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
        public void MarkInitialJoinHandshakeComplete(string domainId)
        {
            _initialJoinHandshakeComplete[domainId] = true;
        }

        /// <summary>
        /// True once a domain has either live-applied a snapshot revision (<see cref="HasInitialSnapshot"/>)
        /// or completed the join-time deserialize/defer path (<see cref="MarkInitialJoinHandshakeComplete"/>).
        /// </summary>
        public bool IsInitialJoinHandshakeComplete(string domainId)
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

        private long GetHighestKnownRevision(string domainId)
        {
            var lastApplied = _lastAppliedRevision.TryGetValue(domainId, out var applied) ? applied : 0L;
            if (!_pendingSnapshots.TryGetValue(domainId, out var pending))
            {
                return lastApplied;
            }

            return Math.Max(lastApplied, pending.Revision);
        }

        public long GetLastAppliedRevision(string domainId)
        {
            return _lastAppliedRevision.TryGetValue(domainId, out var revision) ? revision : 0;
        }

        public bool HasInitialSnapshot(string domainId)
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

        public bool TryGetDeferred(string domainId, out PersistentSyncBufferedSnapshot snapshot)
        {
            return _pendingSnapshots.TryGetValue(domainId, out snapshot);
        }

        public IReadOnlyCollection<PersistentSyncBufferedSnapshot> GetDeferredSnapshots()
        {
            return _pendingSnapshots.Values.ToArray();
        }

        public void ClearDeferred(string domainId)
        {
            _pendingSnapshots.Remove(domainId);
        }
    }
}
