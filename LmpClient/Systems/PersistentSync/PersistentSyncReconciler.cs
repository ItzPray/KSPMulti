using LmpClient;
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
                PsLog($"snapshot ignored stale domain={data.DomainId} incomingRevision={data.Revision}");
                return;
            }

            var bufferedSnapshot = CopySnapshot(data);
            if (!System.Domains.TryGetValue(bufferedSnapshot.DomainId, out var domain))
            {
                PsLog($"snapshot missing domain handler domain={bufferedSnapshot.DomainId} revision={bufferedSnapshot.Revision}");
                RequestResync(bufferedSnapshot.DomainId, "MissingDomainHandler");
                return;
            }

            var applyOutcome = domain.ApplySnapshot(bufferedSnapshot);
            PsLog($"snapshot apply domain={bufferedSnapshot.DomainId} incomingRevision={bufferedSnapshot.Revision} outcome={applyOutcome}");
            switch (applyOutcome)
            {
                case PersistentSyncApplyOutcome.Applied:
                    State.MarkApplied(bufferedSnapshot.DomainId, bufferedSnapshot.Revision);
                    LogAfterMarkApplied(bufferedSnapshot.DomainId, bufferedSnapshot.Revision);
                    CheckInitialSyncComplete();
                    break;
                case PersistentSyncApplyOutcome.Deferred:
                    PsLog($"snapshot deferred domain={bufferedSnapshot.DomainId} revision={bufferedSnapshot.Revision}");
                    State.StoreDeferred(bufferedSnapshot);
                    break;
                case PersistentSyncApplyOutcome.Rejected:
                    RequestResync(bufferedSnapshot.DomainId, "SnapshotRejected");
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
                PsLog($"deferred retry domain={pendingSnapshot.DomainId} revision={pendingSnapshot.Revision} outcome={applyOutcome}");
                switch (applyOutcome)
                {
                    case PersistentSyncApplyOutcome.Applied:
                        State.MarkApplied(pendingSnapshot.DomainId, pendingSnapshot.Revision);
                        LogAfterMarkApplied(pendingSnapshot.DomainId, pendingSnapshot.Revision);
                        CheckInitialSyncComplete();
                        break;
                    case PersistentSyncApplyOutcome.Deferred:
                        State.StoreDeferred(pendingSnapshot);
                        break;
                    case PersistentSyncApplyOutcome.Rejected:
                        RequestResync(pendingSnapshot.DomainId, "SnapshotRejected");
                        break;
                }
            }
        }

        public void FlushPendingState()
        {
            foreach (var entry in System.Domains)
            {
                var domainId = entry.Key;
                var outcome = entry.Value.FlushPendingState();
                switch (outcome)
                {
                    case PersistentSyncApplyOutcome.Applied:
                        if (State.TryGetDeferred(domainId, out var pending))
                        {
                            PsLog($"flush deferred->applied domain={domainId} revision={pending.Revision}");
                            State.MarkApplied(domainId, pending.Revision);
                            LogAfterMarkApplied(domainId, pending.Revision);
                            CheckInitialSyncComplete();
                        }

                        break;
                    case PersistentSyncApplyOutcome.Rejected:
                        PsLog($"flush rejected domain={domainId} requesting resync");
                        RequestResync(domainId, "FlushRejected");
                        break;
                    case PersistentSyncApplyOutcome.Deferred:
                        break;
                }
            }
        }

        public void RequestResync(PersistentSyncDomainId domainId, string reasonCategory)
        {
            if (_lastResyncRequest.TryGetValue(domainId, out var lastRequest) &&
                (DateTime.UtcNow - lastRequest).TotalMilliseconds < 1000)
            {
                return;
            }

            PsLog($"RequestResync domain={domainId} reason={reasonCategory}");
            _lastResyncRequest[domainId] = DateTime.UtcNow;
            System.MessageSender.SendRequest(domainId);
        }

        private void CheckInitialSyncComplete()
        {
            if (MainSystem.NetworkState != ClientState.SyncingPersistentState)
            {
                return;
            }

            if (!State.AreAllInitialSnapshotsApplied())
            {
                return;
            }

            PsLog("initial sync complete all required domains applied -> PersistentStateSynced");
            MainSystem.NetworkState = ClientState.PersistentStateSynced;
        }

        private void LogAfterMarkApplied(PersistentSyncDomainId domainId, long revision)
        {
            PsLog($"MarkApplied domain={domainId} revision={revision} domainInitialSnapshotSatisfied={State.HasInitialSnapshot(domainId)}");
        }

        private static void PsLog(string message)
        {
            LunaLog.Log($"[PersistentSync] {message}");
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
