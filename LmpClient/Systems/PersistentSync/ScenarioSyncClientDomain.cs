using System;
using System.Collections.Generic;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    // Scenario domains buffer one typed snapshot, retry until stock is ready, then apply with peer Share events silenced.
    public abstract class ScenarioSyncClientDomain<TCanonical> : IPersistentSyncClientDomain
        where TCanonical : class
    {
        private TCanonical _pending;

        public abstract PersistentSyncDomainId DomainId { get; }

        /// <summary>Peer Share systems to silence during <see cref="ApplyCanonicalToStock"/>. Empty list is allowed.</summary>
        protected abstract IReadOnlyList<IShareProgressEventSuppressor> PeersToSilence { get; }

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            TCanonical decoded;
            try
            {
                decoded = DeserializeSnapshot(snapshot.Payload ?? Array.Empty<byte>(), snapshot.NumBytes);
            }
            catch (Exception)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            if (decoded == null)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            _pending = decoded;
            OnSnapshotBuffered(snapshot, decoded);
            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pending == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (!ReadyToApply())
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var toApply = _pending;
            try
            {
                using (ScenarioSyncApplyScope.Begin(PeersToSilence))
                {
                    ApplyCanonicalToStock(toApply);
                }
            }
            catch (Exception)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            _pending = null;
            OnCanonicalApplied(toApply);
            return PersistentSyncApplyOutcome.Applied;
        }

        /// <summary>
        /// Stages <paramref name="canonical"/> for the next <see cref="FlushPendingState"/> pass. Use from scene-ready
        /// hooks where stock has reloaded the scenario node behind us and we need to reapply last-known server state.
        /// </summary>
        protected void StagePendingForReassert(TCanonical canonical)
        {
            if (canonical == null)
            {
                return;
            }

            _pending = canonical;
        }

        /// <summary>Decode the wire payload into typed canonical state.</summary>
        protected abstract TCanonical DeserializeSnapshot(byte[] payload, int numBytes);

        /// <summary>True when stock instances required by <see cref="ApplyCanonicalToStock"/> exist.</summary>
        protected abstract bool ReadyToApply();

        /// <summary>Write canonical state into stock. Called inside <see cref="ScenarioSyncApplyScope"/>.</summary>
        protected abstract void ApplyCanonicalToStock(TCanonical canonical);

        /// <summary>Optional hook: called after a snapshot is buffered but before apply attempts. Default: no-op.</summary>
        protected virtual void OnSnapshotBuffered(PersistentSyncBufferedSnapshot snapshot, TCanonical canonical) { }

        /// <summary>Optional hook: called after a successful apply. Default: no-op.</summary>
        protected virtual void OnCanonicalApplied(TCanonical canonical) { }
    }

    public abstract class ScenarioSyncClientDomain<TCanonical, TSnapshotPayload> : ScenarioSyncClientDomain<TCanonical>
        where TCanonical : class
    {
        protected sealed override TCanonical DeserializeSnapshot(byte[] payload, int numBytes)
        {
            return ConvertSnapshotPayload(PersistentSyncPayloadSerializer.Deserialize<TSnapshotPayload>(payload, numBytes));
        }

        protected abstract TCanonical ConvertSnapshotPayload(TSnapshotPayload payload);
    }

    public abstract class TypedPersistentSyncClientDomain<TSnapshotPayload> : IPersistentSyncClientDomain
    {
        public abstract PersistentSyncDomainId DomainId { get; }

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                var payload = PersistentSyncPayloadSerializer.Deserialize<TSnapshotPayload>(
                    snapshot.Payload ?? Array.Empty<byte>(),
                    snapshot.NumBytes);
                return ApplySnapshot(payload, snapshot);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
        }

        protected abstract PersistentSyncApplyOutcome ApplySnapshot(TSnapshotPayload payload, PersistentSyncBufferedSnapshot snapshot);

        public abstract PersistentSyncApplyOutcome FlushPendingState();
    }
}
