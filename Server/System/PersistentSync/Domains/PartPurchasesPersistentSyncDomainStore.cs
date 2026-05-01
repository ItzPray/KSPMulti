using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Client;
using System;

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
    /// AllServerDomainsInheritOneOfTheSanctionedTemplates).
    /// </summary>
    public sealed class PartPurchasesPersistentSyncDomainStore : ProjectionSyncDomain<TechnologyPersistentSyncDomainStore>
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
        protected override string OwnerDomainId => PersistentSyncDomainNames.Technology;

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload)
        {
            return AuthorizeByPolicy(client);
        }

        protected override PersistentSyncDomainApplyResult ApplyToOwner(
            TechnologyPersistentSyncDomainStore owner,
            ClientStructure client,
            byte[] payload,
            long? clientKnownRevision,
            string reason,
            bool isServerMutation)
        {
            var intent = PersistentSyncPayloadSerializer.Deserialize<PartPurchasesPayload>(payload ?? global::System.Array.Empty<byte>(), payload?.Length ?? 0);
            return owner.ApplyPartPurchasesIntent(intent?.Items ?? Array.Empty<PartPurchaseSnapshotInfo>(), clientKnownRevision, reason, isServerMutation);
        }

        protected override byte[] RenderSnapshotPayload(TechnologyPersistentSyncDomainStore owner)
        {
            return PersistentSyncPayloadSerializer.Serialize(new PartPurchasesPayload { Items = owner?.BuildPartPurchasesSnapshotPayload() ?? Array.Empty<PartPurchaseSnapshotInfo>() });
        }
    }
}
