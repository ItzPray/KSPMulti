using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;

namespace Server.System.PersistentSync
{
    [PersistentSyncStockScenario("Reputation", ScalarField = "rep")]
    public class ReputationPersistentSyncDomainStore : SyncDomainStore<float>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<ReputationPersistentSyncDomainStore>();
        }

        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override float CreateDefaultPayload() => GameplaySettings.SettingsStore.StartingReputation;
    }
}
