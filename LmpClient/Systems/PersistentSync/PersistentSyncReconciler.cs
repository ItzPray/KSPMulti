using LmpClient;
using LmpClient.Base;
using LmpClient.Network;
using LmpClient.Systems.Network;
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
        private readonly Dictionary<string, DateTime> _lastResyncRequest = new Dictionary<string, DateTime>();

        public PersistentSyncReconcilerState State { get; } = new PersistentSyncReconcilerState();

        public void Reset(IEnumerable<string> requiredDomains)
        {
            State.Reset(requiredDomains);
            _lastResyncRequest.Clear();
        }

        public long GetKnownRevision(string domainId)
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

            NetworkSystem.BumpPersistentSyncJoinActivity();

            var bufferedSnapshot = CopySnapshot(data);
            if (!System.Domains.TryGetValue(bufferedSnapshot.DomainId, out var domain))
            {
                PsLog($"snapshot missing domain handler domain={bufferedSnapshot.DomainId} revision={bufferedSnapshot.Revision} disconnecting");
                NetworkConnection.Disconnect(
                    $"[KSPMP] Persistent sync protocol error: no client handler for domain '{bufferedSnapshot.DomainId}'. Update client or verify catalog.");
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
                    // Deserialize succeeded but live singletons / scene objects are missing (e.g. main menu).
                    // Do not call MarkApplied here — that makes ShouldIgnoreSnapshot drop same-revision
                    // re-sends before SetLevel runs (KSC buildings stuck at default).
                    PsLog(
                        $"snapshot deferred (payload retained) domain={bufferedSnapshot.DomainId} revision={bufferedSnapshot.Revision} marking join handshake only");
                    State.StoreDeferred(bufferedSnapshot);
                    State.MarkInitialJoinHandshakeComplete(bufferedSnapshot.DomainId);
                    CheckInitialSyncComplete();
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
                        State.MarkInitialJoinHandshakeComplete(pendingSnapshot.DomainId);
                        CheckInitialSyncComplete();
                        break;
                    case PersistentSyncApplyOutcome.Rejected:
                        RequestResync(pendingSnapshot.DomainId, "SnapshotRejected");
                        break;
                }
            }
        }

        public void FlushPendingState()
        {
            var appliedDomainIds = new List<string>();
            foreach (var entry in System.Domains)
            {
                var domainId = entry.Key;
                var outcome = entry.Value.FlushPendingState();
                switch (outcome)
                {
                    case PersistentSyncApplyOutcome.Applied:
                        appliedDomainIds.Add(domainId);
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

            if (appliedDomainIds.Count > 0)
            {
                PersistentSyncGamePersistenceMaterializer.Materialize(appliedDomainIds, "FlushPendingState");
            }
        }

        public void RequestResync(string domainId, string reasonCategory)
        {
            if (_lastResyncRequest.TryGetValue(domainId, out var lastRequest) &&
                (DateTime.UtcNow - lastRequest).TotalMilliseconds < 1000)
            {
                return;
            }

            PsLog($"RequestResync domain={domainId} reason={reasonCategory}");
            _lastResyncRequest[domainId] = DateTime.UtcNow;
            // Allow the server to re-send the same revision after a failed or deferred apply.
            State.ClearDeferred(domainId);
            System.MessageSender.SendRequest(domainId);
        }

        private void CheckInitialSyncComplete()
        {
            if (MainSystem.NetworkState != ClientState.SyncingPersistentState)
            {
                return;
            }

            if (!State.AreAllJoinHandshakesComplete())
            {
                return;
            }

            PsLog("initial join handshakes complete (live apply may still be pending for some domains) -> PersistentStateSynced");
            MainSystem.NetworkState = ClientState.PersistentStateSynced;
            System.RequestOptionalGameLaunchIdSnapshotAfterMandatorySync();
        }

        private void LogAfterMarkApplied(string domainId, long revision)
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
