using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;

namespace Server.System.PersistentSync
{
    [PersistentSyncStockScenario("ResearchAndDevelopment", ScalarField = "sci")]
    public class SciencePersistentSyncDomainStore : SyncDomainStore<float>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<SciencePersistentSyncDomainStore>();
        }

        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override float CreateDefaultPayload() => GameplaySettings.SettingsStore.StartingScience;
    }
}
