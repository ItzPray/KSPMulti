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
        ProducerOfferGenerationNudge = 3,

        /// <summary>
        /// DEBUG/diagnostic: client requests compare-only snapshots without applying them.
        /// </summary>
        AuditRequest = 4,

        /// <summary>
        /// DEBUG/diagnostic: server responds with canonical snapshot bytes for audit comparison.
        /// </summary>
        AuditSnapshot = 5
    }
}
