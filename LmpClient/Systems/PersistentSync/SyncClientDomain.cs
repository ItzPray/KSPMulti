using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.PersistentSync
{
    public abstract class SyncClientDomain<TPayload> : IPersistentSyncClientDomain
    {
        private bool _hasPendingPayload;
        private TPayload _pendingPayload;

        public PersistentSyncDomainDefinition Definition => PersistentSyncDomainCatalog.GetByName(DomainName);
        public string DomainName => PersistentSyncDomainNaming.InferDomainName(GetType());
        public string DomainId => PersistentSyncDomainCatalog.TryGetByName(DomainName, out var definition)
            ? definition.DomainId
            : DomainName;

        protected virtual IReadOnlyList<IShareProgressEventSuppressor> PeersToSilence => new IShareProgressEventSuppressor[0];

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            try
            {
                _pendingPayload = PersistentSyncPayloadSerializer.Deserialize<TPayload>(snapshot.Payload ?? Array.Empty<byte>(), snapshot.NumBytes);
                _hasPendingPayload = true;
                OnPayloadBuffered(snapshot, _pendingPayload);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (!_hasPendingPayload)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (!CanApplyLiveState())
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            try
            {
                using (ScenarioSyncApplyScope.Begin(PeersToSilence))
                {
                    ApplyLiveState(_pendingPayload);
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            var applied = _pendingPayload;
            _hasPendingPayload = false;
            OnPayloadApplied(applied);
            return PersistentSyncApplyOutcome.Applied;
        }

        protected virtual bool CanApplyLiveState()
        {
            return true;
        }

        protected abstract void ApplyLiveState(TPayload payload);
        protected virtual void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, TPayload payload) { }
        protected virtual void OnPayloadApplied(TPayload payload) { }
    }
}
