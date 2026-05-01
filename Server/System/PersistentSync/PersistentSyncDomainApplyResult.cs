using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
namespace Server.System.PersistentSync
{
    public class PersistentSyncDomainApplyResult
    {
        public bool Accepted;
        public bool Changed;
        public bool ReplyToOriginClient;

        /// <summary>
        /// When true the registry sends <see cref="Snapshot"/> to the current designated producer (contract lock
        /// owner) so a non-producer client can request a controlled generation pass (see
        /// <see cref="LmpCommon.PersistentSync.ContractIntentPayloadKind.RequestOfferGeneration"/>). Re-applying the
        /// canonical snapshot on the producer drives its <c>ReplenishStockOffersAfterPersistentSnapshotApply</c> pass,
        /// which mints offers back to the server as <see cref="LmpCommon.PersistentSync.ContractIntentPayloadKind.OfferObserved"/> proposals.
        /// </summary>
        public bool ReplyToProducerClient;
        public PersistentSyncDomainSnapshot Snapshot;
    }
}
