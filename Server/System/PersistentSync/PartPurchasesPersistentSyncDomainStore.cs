using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Client;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Pure projection domain over <see cref="TechnologyPersistentSyncDomainStore"/>. PartPurchases does not
    /// write to the scenario; its state lives in Technology's canonical <c>PartsByTech</c>. This class exists
    /// only to satisfy the wire contract (client still sends <see cref="PersistentSyncDomainId.PartPurchases"/>
    /// intents and expects snapshots in the PartPurchases binary format). All mutations are routed into
    /// Technology's reducer so the &quot;one scenario, one domain&quot; Scenario Sync Domain Contract rule holds for
    /// the shared <c>Tech/*</c> node path.
    ///
    /// Inherits the <see cref="ProjectionSyncDomain{TOwner}"/> template; no domain in the registry implements
    /// <see cref="IPersistentSyncServerDomain"/> directly anymore (enforced by the regression gate
    /// AllServerDomainsInheritTemplateUnlessInProjectionAllowlist).
    /// </summary>
    public sealed class PartPurchasesPersistentSyncDomainStore : ProjectionSyncDomain<TechnologyPersistentSyncDomainStore>
    {
        public PartPurchasesPersistentSyncDomainStore()
            : base(null)
        {
        }

        public PartPurchasesPersistentSyncDomainStore(TechnologyPersistentSyncDomainStore technology)
            : base(technology)
        {
        }

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.PartPurchases;
        protected override PersistentSyncDomainId OwnerDomainId => PersistentSyncDomainId.Technology;

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        protected override PersistentSyncDomainApplyResult ApplyToOwner(
            TechnologyPersistentSyncDomainStore owner,
            ClientStructure client,
            byte[] payload,
            int numBytes,
            long? clientKnownRevision,
            string reason,
            bool isServerMutation)
        {
            var records = PartPurchasesSnapshotPayloadSerializer.Deserialize(payload ?? new byte[0]);
            return owner.ApplyPartPurchasesIntent(records, clientKnownRevision, reason, isServerMutation);
        }

        protected override byte[] RenderSnapshotPayload(TechnologyPersistentSyncDomainStore owner)
        {
            return owner?.SerializePartPurchasesSnapshot() ?? EmptyPayload();
        }
    }
}
