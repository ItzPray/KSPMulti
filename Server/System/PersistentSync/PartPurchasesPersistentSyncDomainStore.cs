using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Client;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Pure projection domain over TechnologyPersistentSyncDomainStore. PartPurchases does not
    /// write to the scenario; its state lives in Technology's canonical <c>PartsByTech</c>. This class exists
    /// only to satisfy the wire contract (client still sends PartPurchases
    /// intents and expects snapshots in the PartPurchases binary format). All mutations are routed into
    /// Technology's reducer so the "one scenario, one domain" Scenario Sync Domain Contract rule holds for
    /// the shared <c>Tech/*</c> node path.
    ///
    /// Inherits the ProjectionSyncDomain{TOwner} template; no domain in the registry implements
    /// IPersistentSyncServerDomain directly anymore (enforced by the regression gate
    /// AllServerDomainsInheritTemplateUnlessInProjectionAllowlist).
    /// </summary>
    public sealed class PartPurchasesPersistentSyncDomainStore : ProjectionSyncDomain<TechnologyPersistentSyncDomainStore>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("PartPurchases", 10);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("ResearchAndDevelopment")
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(TechnologyPersistentSyncDomainStore.Domain)
                .UsesServerDomain<PartPurchasesPersistentSyncDomainStore>();
        }

        public PartPurchasesPersistentSyncDomainStore()
            : base(null)
        {
        }

        public PartPurchasesPersistentSyncDomainStore(TechnologyPersistentSyncDomainStore technology)
            : base(technology)
        {
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;
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