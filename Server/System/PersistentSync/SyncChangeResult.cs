namespace Server.System.PersistentSync
{
    /// <summary>
    /// Result returned from scenario-owning incoming-payload hooks such as
    /// <see cref="SyncDomainStore{TPayload}.HandleIncomingPayload"/>.
    /// The server pipeline consumes this to decide whether to bump revision, rewrite the scenario, and how to route
    /// the resulting snapshot.
    /// </summary>
    public sealed class SyncChangeResult<TCanonical>
    {
        /// <summary>
        /// Whether the payload decoded and semantically validated. <c>false</c> produces a rejected apply result and
        /// leaves canonical state untouched.
        /// </summary>
        public bool Accepted { get; private set; }

        /// <summary>
        /// Candidate next canonical state. May reference the same instance as <c>current</c> for a no-op change; the
        /// pipeline runs equivalence checks to decide whether a state change occurred regardless of reference identity.
        /// </summary>
        public TCanonical NextState { get; private set; }

        /// <summary>
        /// Opt-in routing: the base class copies this into the
        /// <see cref="PersistentSyncDomainApplyResult.ReplyToProducerClient"/> flag so the registry can route the
        /// snapshot to the current designated producer (today used by Contracts for
        /// <see cref="LmpCommon.PersistentSync.ContractIntentPayloadKind.RequestOfferGeneration"/>).
        /// </summary>
        public bool ReplyToProducerClient { get; private set; }

        /// <summary>
        /// When true on a client intent that did not change canonical state, the server pipeline
        /// still sets <see cref="PersistentSyncDomainApplyResult.ReplyToOriginClient"/> so the sender receives an
        /// authoritative snapshot (e.g. Contracts monotonic merge repaired a regressed parameter observation).
        /// </summary>
        public bool ForceReplyToOriginClient { get; private set; }

        public static SyncChangeResult<TCanonical> Reject()
        {
            return new SyncChangeResult<TCanonical> { Accepted = false };
        }

        public static SyncChangeResult<TCanonical> Accept(
            TCanonical nextState,
            bool replyToProducerClient = false,
            bool forceReplyToOriginClient = false)
        {
            return new SyncChangeResult<TCanonical>
            {
                Accepted = true,
                NextState = nextState,
                ReplyToProducerClient = replyToProducerClient,
                ForceReplyToOriginClient = forceReplyToOriginClient
            };
        }
    }
}
