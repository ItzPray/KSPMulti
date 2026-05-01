using System;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public abstract class ScalarPersistentSyncClientDomain<T> : IPersistentSyncClientDomain
    {
        private bool _hasPendingValue;
        private T _pendingValue;

        public abstract string DomainId { get; }

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                var deserialized = PersistentSyncPayloadSerializer.Deserialize<T>(snapshot.Payload, snapshot.NumBytes);
                _pendingValue = deserialized;
                _hasPendingValue = true;
            }
            catch (Exception)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            if (!CanApplyLiveState())
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            return TryCommitPendingLive();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            return TryCommitPendingLive();
        }

        private PersistentSyncApplyOutcome TryCommitPendingLive()
        {
            if (!_hasPendingValue)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (!CanApplyLiveState())
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            try
            {
                ApplyLiveState(_pendingValue);
                _hasPendingValue = false;
                return PersistentSyncApplyOutcome.Applied;
            }
            catch (Exception)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
        }

        protected abstract bool CanApplyLiveState();
        protected abstract void ApplyLiveState(T value);
    }
}
