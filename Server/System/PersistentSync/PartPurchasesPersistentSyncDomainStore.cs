using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Server.Client;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Pure projection domain over <see cref="TechnologyPersistentSyncDomainStore"/>. PartPurchases does not
    /// write to the scenario; its state lives in Technology's canonical <c>PartsByTech</c>. This class exists
    /// only to satisfy the wire contract (client still sends <see cref="PersistentSyncDomainId.PartPurchases"/>
    /// intents and expects snapshots in the PartPurchases binary format). All mutations are routed into
    /// Technology's reducer so the "one scenario, one domain" Scenario Sync Domain Contract rule holds for
    /// the shared <c>Tech/*</c> node path.
    ///
    /// This is an intentional, audited exception to the "must inherit ScenarioSyncDomainStore" rule: the class
    /// has no independent canonical state or scenario write path. See AGENTS.md "Scenario Sync Domain Contract"
    /// for the projection allowance.
    /// </summary>
    public sealed class PartPurchasesPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private readonly TechnologyPersistentSyncDomainStore _technology;

        public PartPurchasesPersistentSyncDomainStore()
            : this(null)
        {
        }

        public PartPurchasesPersistentSyncDomainStore(TechnologyPersistentSyncDomainStore technology)
        {
            _technology = technology;
        }

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.PartPurchases;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        public void LoadFromPersistence(bool createdFromScratch)
        {
            // No-op: Technology owns the canonical load for Tech/* nodes.
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var technology = ResolveTechnology();
            var payload = technology?.SerializePartPurchasesSnapshot() ?? new byte[4];
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = technology?.RevisionForProjection ?? 0L,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            var technology = ResolveTechnology();
            if (technology == null)
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            var records = PartPurchasesSnapshotPayloadSerializer.Deserialize(data?.Payload ?? new byte[0]);
            var result = technology.ApplyPartPurchasesIntent(records, data?.ClientKnownRevision, data?.Reason, isServerMutation: false);
            return ReprojectAsPartPurchases(result, technology);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            var technology = ResolveTechnology();
            if (technology == null)
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            var records = PartPurchasesSnapshotPayloadSerializer.Deserialize(payload ?? new byte[0]);
            var result = technology.ApplyPartPurchasesIntent(records, null, reason, isServerMutation: true);
            return ReprojectAsPartPurchases(result, technology);
        }

        private PersistentSyncDomainApplyResult ReprojectAsPartPurchases(PersistentSyncDomainApplyResult technologyResult, TechnologyPersistentSyncDomainStore technology)
        {
            if (technologyResult == null || !technologyResult.Accepted)
            {
                return technologyResult ?? new PersistentSyncDomainApplyResult { Accepted = false };
            }

            var partPurchasesPayload = technology.SerializePartPurchasesSnapshot();
            return new PersistentSyncDomainApplyResult
            {
                Accepted = technologyResult.Accepted,
                Changed = technologyResult.Changed,
                ReplyToOriginClient = technologyResult.ReplyToOriginClient,
                ReplyToProducerClient = technologyResult.ReplyToProducerClient,
                Snapshot = new PersistentSyncDomainSnapshot
                {
                    DomainId = DomainId,
                    Revision = technologyResult.Snapshot?.Revision ?? 0L,
                    AuthorityPolicy = AuthorityPolicy,
                    Payload = partPurchasesPayload,
                    NumBytes = partPurchasesPayload.Length
                }
            };
        }

        private TechnologyPersistentSyncDomainStore ResolveTechnology()
        {
            return _technology ?? PersistentSyncRegistry.GetRegisteredDomain(PersistentSyncDomainId.Technology) as TechnologyPersistentSyncDomainStore;
        }
    }
}
