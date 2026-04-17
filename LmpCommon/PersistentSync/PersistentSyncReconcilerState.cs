using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    public class PersistentSyncReconcilerState
    {
        private readonly Dictionary<PersistentSyncDomainId, long> _lastAppliedRevision = new Dictionary<PersistentSyncDomainId, long>();
        private readonly Dictionary<PersistentSyncDomainId, bool> _hasInitialSnapshot = new Dictionary<PersistentSyncDomainId, bool>();
        private readonly Dictionary<PersistentSyncDomainId, PersistentSyncBufferedSnapshot> _pendingSnapshots = new Dictionary<PersistentSyncDomainId, PersistentSyncBufferedSnapshot>();
        private HashSet<PersistentSyncDomainId> _requiredDomains = new HashSet<PersistentSyncDomainId>();

        public void Reset(IEnumerable<PersistentSyncDomainId> requiredDomains)
        {
            _requiredDomains = new HashSet<PersistentSyncDomainId>(requiredDomains ?? Enumerable.Empty<PersistentSyncDomainId>());
            _lastAppliedRevision.Clear();
            _hasInitialSnapshot.Clear();
            _pendingSnapshots.Clear();
        }

        public IReadOnlyCollection<PersistentSyncDomainId> RequiredDomains => _requiredDomains.ToArray();

        public bool ShouldIgnoreSnapshot(PersistentSyncDomainId domainId, long revision)
        {
            return _lastAppliedRevision.TryGetValue(domainId, out var lastRevision) && revision <= lastRevision;
        }

        public void MarkApplied(PersistentSyncDomainId domainId, long revision)
        {
            _lastAppliedRevision[domainId] = revision;
            _hasInitialSnapshot[domainId] = true;
            _pendingSnapshots.Remove(domainId);
        }

        public long GetLastAppliedRevision(PersistentSyncDomainId domainId)
        {
            return _lastAppliedRevision.TryGetValue(domainId, out var revision) ? revision : 0;
        }

        public bool HasInitialSnapshot(PersistentSyncDomainId domainId)
        {
            return _hasInitialSnapshot.TryGetValue(domainId, out var hasInitial) && hasInitial;
        }

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
