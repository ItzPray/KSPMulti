using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;

namespace Server.System.PersistentSync
{
    [PersistentSyncStockScenario("Funding", ScalarField = "funds")]
    public class FundsPersistentSyncDomainStore : SyncDomainStore<double>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<FundsPersistentSyncDomainStore>();
        }

        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override double CreateDefaultPayload() => GameplaySettings.SettingsStore.StartingFunds;
    }
}
