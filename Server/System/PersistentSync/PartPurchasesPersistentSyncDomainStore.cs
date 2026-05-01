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
    public sealed class PartPurchasesPersistentSyncDomainStore : ProjectionSyncDomain<TechnologyPersistentSyncDomainStore, PartPurchaseSnapshotInfo[], PartPurchaseSnapshotInfo[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(PersistentSyncDomainNames.Technology)
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

        public override string DomainId => PersistentSyncDomainNames.PartPurchases;
        protected override string OwnerDomainId => PersistentSyncDomainNames.Technology;

        protected override PersistentSyncDomainApplyResult ApplyToOwner(
            TechnologyPersistentSyncDomainStore owner,
            ClientStructure client,
            PartPurchaseSnapshotInfo[] intent,
            long? clientKnownRevision,
            string reason,
            bool isServerMutation)
        {
            return owner.ApplyPartPurchasesIntent(intent, clientKnownRevision, reason, isServerMutation);
        }

        protected override PartPurchaseSnapshotInfo[] BuildSnapshotPayload(TechnologyPersistentSyncDomainStore owner)
        {
            return owner?.BuildPartPurchasesSnapshotPayload() ?? new PartPurchaseSnapshotInfo[0];
        }
    }
}
