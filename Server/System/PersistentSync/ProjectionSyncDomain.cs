using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Server.Client;
using System;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Template for projection domains — domains that do NOT own their own scenario state but project/route
    /// intents and snapshots through another &quot;owner&quot; domain. The owner remains the sole writer of its scenario
    /// section, preserving the &quot;one scenario, one domain&quot; Scenario Sync Domain Contract rule for the shared
    /// scenario path.
    ///
    /// Use this when:
    /// <list type="bullet">
    /// <item><description>The wire contract requires a separate <see cref="string"/> (different
    /// binary snapshot or intent payload format).</description></item>
    /// <item><description>Canonical state for the projection is derived from the owner's canonical state; the
    /// projection never writes to any scenario on its own.</description></item>
    /// </list>
    ///
    /// Subclasses MUST NOT mutate the scenario directly or hold independent canonical state. All mutations
    /// route through <see cref="ApplyToOwner"/>; outgoing snapshot payloads are rendered via
    /// <see cref="RenderSnapshotPayload"/> over the owner's current canonical state.
    ///
    /// <para><b>Revision contract:</b> the projection's <see cref="PersistentSyncDomainSnapshot.Revision"/> is
    /// the owner's revision. Both domains observe one canonical source, so they must emit a single monotonic
    /// counter: if a projection advanced its own counter independently, a client reconciling both streams
    /// could see a PartPurchases-style projection at revision N while its owner was still at N-1 (or vice
    /// versa), violating snapshot ordering. The base class enforces this by pulling <c>Revision</c> from the
    /// owner's result inside both <see cref="GetCurrentSnapshot"/> and the reprojection helper &mdash; a
    /// regression test (<c>ProjectionRevisionTracksOwnerRevisionAfterMutation</c>) guards against drift.</para>
    /// </summary>
    public abstract class ProjectionSyncDomain<TOwner> : IPersistentSyncServerDomain
        where TOwner : class, IPersistentSyncServerDomain
    {
        private readonly TOwner _injectedOwner;

        protected ProjectionSyncDomain()
            : this(null)
        {
        }

        protected ProjectionSyncDomain(TOwner owner)
        {
            _injectedOwner = owner;
        }

        public virtual string DomainId
        {
            get
            {
                var domainName = PersistentSyncDomainNaming.InferDomainName(GetType());
                return PersistentSyncDomainCatalog.TryGetByName(domainName, out var definition)
                    ? definition.DomainId
                    : domainName;
            }
        }

        /// <summary>
        /// Projection domains advertise a floor authority; their per-intent gate runs through the owner
        /// domain's authority on re-entry. Override only if the projection itself needs server-derived or
        /// lock-owner gating independent of the owner.
        /// </summary>
        public virtual PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        /// <summary>
        /// The registry id of the owner domain this projection delegates to.
        /// </summary>
        protected abstract string OwnerDomainId { get; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            // No-op: the owner owns the scenario load path and any write-back after load.
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var owner = ResolveOwner();
            if (owner == null)
            {
                var emptyPayload = EmptyPayload();
                return new PersistentSyncDomainSnapshot
                {
                    DomainId = DomainId,
                    Revision = 0L,
                    AuthorityPolicy = AuthorityPolicy,
                    Payload = emptyPayload,
                    NumBytes = emptyPayload.Length
                };
            }

            var ownerSnapshot = owner.GetCurrentSnapshot();
            var payload = RenderSnapshotPayload(owner) ?? EmptyPayload();
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = ownerSnapshot?.Revision ?? 0L,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            var owner = ResolveOwner();
            if (owner == null)
            {
                return Rejected();
            }

            var ownerResult = ApplyToOwner(
                owner,
                client,
                ExactPayload(data?.Payload, data?.NumBytes ?? (data?.Payload?.Length ?? 0)),
                data?.ClientKnownRevision,
                data?.Reason,
                isServerMutation: false);

            return Reproject(ownerResult, owner);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, string reason)
        {
            var owner = ResolveOwner();
            if (owner == null)
            {
                return Rejected();
            }

            var ownerResult = ApplyToOwner(owner, null, payload ?? Array.Empty<byte>(), null, reason, isServerMutation: true);
            return Reproject(ownerResult, owner);
        }

        /// <summary>
        /// Per-intent authority gate; must be declared explicitly by every concrete projection. The method is
        /// <c>abstract</c> rather than providing a default implementation so authority is never silently
        /// inherited: a new projection author cannot forget to declare which gate applies. For projections
        /// that inherit the owner's authority semantics, call <see cref="AuthorizeByPolicy"/> or delegate to
        /// the resolved owner's <see cref="IPersistentSyncServerDomain.AuthorizeIntent"/>.
        /// </summary>
        public abstract bool AuthorizeIntent(ClientStructure client, byte[] payload);

        /// <summary>
        /// Canonical policy-based authority check — evaluates <see cref="AuthorityPolicy"/> through the
        /// registry. Use this from simple-projection <see cref="AuthorizeIntent"/> overrides.
        /// </summary>
        protected bool AuthorizeByPolicy(ClientStructure client)
        {
            return PersistentSyncRegistry.ValidateClientMaySubmitIntent(client, this);
        }

        /// <summary>
        /// Route the incoming intent / server mutation payload into the owner domain's reducer, returning the
        /// owner's un-reprojected <see cref="PersistentSyncDomainApplyResult"/>. The base class handles
        /// reprojection into <see cref="DomainId"/>'s snapshot wire shape.
        /// </summary>
        protected abstract PersistentSyncDomainApplyResult ApplyToOwner(
            TOwner owner,
            ClientStructure client,
            byte[] payload,
            long? clientKnownRevision,
            string reason,
            bool isServerMutation);

        /// <summary>
        /// Render the projection's wire snapshot payload from the owner's current canonical state.
        /// </summary>
        protected abstract byte[] RenderSnapshotPayload(TOwner owner);

        /// <summary>
        /// Zero-length-placeholder snapshot payload used when the owner is not yet registered. Override if your
        /// wire format requires something different (e.g. a count-prefixed empty list).
        /// </summary>
        protected virtual byte[] EmptyPayload()
        {
            return new byte[sizeof(int)];
        }

        private TOwner ResolveOwner()
        {
            return _injectedOwner ?? (PersistentSyncRegistry.GetRegisteredDomain(OwnerDomainId) as TOwner);
        }

        private PersistentSyncDomainApplyResult Reproject(PersistentSyncDomainApplyResult ownerResult, TOwner owner)
        {
            if (ownerResult == null || !ownerResult.Accepted)
            {
                return ownerResult ?? Rejected();
            }

            var payload = RenderSnapshotPayload(owner) ?? EmptyPayload();
            return new PersistentSyncDomainApplyResult
            {
                Accepted = ownerResult.Accepted,
                Changed = ownerResult.Changed,
                ReplyToOriginClient = ownerResult.ReplyToOriginClient,
                ReplyToProducerClient = ownerResult.ReplyToProducerClient,
                Snapshot = new PersistentSyncDomainSnapshot
                {
                    DomainId = DomainId,
                    Revision = ownerResult.Snapshot?.Revision ?? 0L,
                    AuthorityPolicy = AuthorityPolicy,
                    Payload = payload,
                    NumBytes = payload.Length
                }
            };
        }

        private static PersistentSyncDomainApplyResult Rejected()
        {
            return new PersistentSyncDomainApplyResult { Accepted = false };
        }

        private static byte[] ExactPayload(byte[] payload, int numBytes)
        {
            payload = payload ?? Array.Empty<byte>();
            if (numBytes < 0)
            {
                numBytes = 0;
            }

            if (numBytes >= payload.Length)
            {
                return payload;
            }

            var exact = new byte[numBytes];
            Buffer.BlockCopy(payload, 0, exact, 0, numBytes);
            return exact;
        }
    }

}
