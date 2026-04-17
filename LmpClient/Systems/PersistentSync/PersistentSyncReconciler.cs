using LmpClient.Base;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.Message.Data.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncReconciler : SubSystem<PersistentSyncSystem>
    {
        private readonly Dictionary<PersistentSyncDomainId, DateTime> _lastResyncRequest = new Dictionary<PersistentSyncDomainId, DateTime>();

        public PersistentSyncReconcilerState State { get; } = new PersistentSyncReconcilerState();

        public void Reset(IEnumerable<PersistentSyncDomainId> requiredDomains)
        {
            State.Reset(requiredDomains);
            _lastResyncRequest.Clear();
        }

        public long GetKnownRevision(PersistentSyncDomainId domainId)
        {
            return State.GetLastAppliedRevision(domainId);
        }

        public void HandleSnapshot(PersistentSyncSnapshotMsgData data)
        {
            if (State.ShouldIgnoreSnapshot(data.DomainId, data.Revision))
            {
                return;
            }

            var bufferedSnapshot = CopySnapshot(data);
            if (!System.Domains.TryGetValue(bufferedSnapshot.DomainId, out var domain))
            {
                RequestResync(bufferedSnapshot.DomainId);
                return;
            }

            var applyOutcome = domain.ApplySnapshot(bufferedSnapshot);
            switch (applyOutcome)
            {
                case PersistentSyncApplyOutcome.Applied:
                    State.MarkApplied(bufferedSnapshot.DomainId, bufferedSnapshot.Revision);
                    CheckInitialSyncComplete();
                    break;
                case PersistentSyncApplyOutcome.Deferred:
                    State.StoreDeferred(bufferedSnapshot);
                    break;
                case PersistentSyncApplyOutcome.Rejected:
                    RequestResync(bufferedSnapshot.DomainId);
                    break;
            }
        }

        public void RetryDeferredSnapshots()
        {
            foreach (var pendingSnapshot in State.GetDeferredSnapshots().ToArray())
            {
                if (!System.Domains.TryGetValue(pendingSnapshot.DomainId, out var domain))
                {
                    continue;
                }

                var applyOutcome = domain.ApplySnapshot(pendingSnapshot);
                switch (applyOutcome)
                {
                    case PersistentSyncApplyOutcome.Applied:
                        State.MarkApplied(pendingSnapshot.DomainId, pendingSnapshot.Revision);
                        CheckInitialSyncComplete();
                        break;
                    case PersistentSyncApplyOutcome.Deferred:
                        State.StoreDeferred(pendingSnapshot);
                        break;
                    case PersistentSyncApplyOutcome.Rejected:
                        RequestResync(pendingSnapshot.DomainId);
                        break;
                }
            }
        }

        public void FlushPendingState()
        {
            foreach (var domain in System.Domains.Values)
            {
                domain.FlushPendingState();
            }
        }

        public void RequestResync(PersistentSyncDomainId domainId)
        {
            if (_lastResyncRequest.TryGetValue(domainId, out var lastRequest) &&
                (DateTime.UtcNow - lastRequest).TotalMilliseconds < 1000)
            {
                return;
            }

            _lastResyncRequest[domainId] = DateTime.UtcNow;
            System.MessageSender.SendRequest(domainId);
        }

        private void CheckInitialSyncComplete()
        {
            if (MainSystem.NetworkState == ClientState.SyncingPersistentState && State.AreAllInitialSnapshotsApplied())
            {
                MainSystem.NetworkState = ClientState.PersistentStateSynced;
            }
        }

        private static PersistentSyncBufferedSnapshot CopySnapshot(PersistentSyncSnapshotMsgData data)
        {
            var payload = new byte[data.NumBytes];
            Buffer.BlockCopy(data.Payload, 0, payload, 0, data.NumBytes);
            return new PersistentSyncBufferedSnapshot
            {
                DomainId = data.DomainId,
                Revision = data.Revision,
                AuthorityPolicy = data.AuthorityPolicy,
                NumBytes = data.NumBytes,
                Payload = payload
            };
        }
    }
}
