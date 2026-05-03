namespace LmpCommon.Message.Types
{
    public enum PersistentSyncMessageType : ushort
    {
        Request = 0,
        Intent = 1,
        Snapshot = 2,

        /// <summary>
        /// Server-only → client: wake the contract-lock producer to run a controlled RefreshContracts pass after a
        /// non-producer <see cref="LmpCommon.PersistentSync.ContractIntentPayloadKind.RequestOfferGeneration"/> intent.
        /// Same-revision snapshot replays are ignored client-side as stale; this message is not.
        /// </summary>
        ProducerOfferGenerationNudge = 3
    }
}
