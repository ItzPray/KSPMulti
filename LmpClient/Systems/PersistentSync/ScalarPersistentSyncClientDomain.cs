using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public abstract class ScalarPersistentSyncClientDomain<T> : IPersistentSyncClientDomain
    {
        private bool _hasPendingValue;
        private T _pendingValue;

        public abstract PersistentSyncDomainId DomainId { get; }

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            _pendingValue = DeserializePayload(snapshot.Payload, snapshot.NumBytes);
            _hasPendingValue = true;
            FlushPendingState();
            return PersistentSyncApplyOutcome.Applied;
        }

        public void FlushPendingState()
        {
            if (!_hasPendingValue || !CanApplyLiveState())
            {
                return;
            }

            ApplyLiveState(_pendingValue);
            _hasPendingValue = false;
        }

        protected abstract T DeserializePayload(byte[] payload, int numBytes);
        protected abstract bool CanApplyLiveState();
        protected abstract void ApplyLiveState(T value);
    }
}
