using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using System.Collections.Concurrent;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncMessageHandler : SubSystem<PersistentSyncSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is PersistentSyncBaseMsgData msgData))
            {
                return;
            }

            switch (msgData.PersistentSyncMessageType)
            {
                case PersistentSyncMessageType.Snapshot:
                    System.Reconciler.HandleSnapshot((PersistentSyncSnapshotMsgData)msg.Data);
                    break;

                case PersistentSyncMessageType.ProducerOfferGenerationNudge:
                    // Nudge can arrive in the same network batch as a contracts snapshot. Replenish consults
                    // ContractsPersistentSyncClientDomain.HasPendingSnapshot — if we run before the reconciler flush
                    // applies/clears that snapshot, Replenish defers and the retry can lose to ordering.
                    // Flush first so the producer runs RefreshContracts against settled ContractSystem + open gates.
                    if (System.Enabled)
                    {
                        System.FlushLivePendingPersistentSyncState("ProducerOfferGenerationNudge");
                    }

                    ShareContractsSystem.Singleton?.ReplenishStockOffersAfterPersistentSnapshotApply(
                        "PersistentSyncProducerOfferGenerationNudge");
                    break;

#if DEBUG
                case PersistentSyncMessageType.AuditSnapshot:
                    PersistentSyncAuditCoordinator.Instance.HandleAuditSnapshot((PersistentSyncAuditSnapshotMsgData)msg.Data);
                    break;
#endif
            }
        }
    }
}
