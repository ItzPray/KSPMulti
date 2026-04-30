using LmpClient.Systems.ShareFunds;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class FundsPersistentSyncClientDomain : ScalarPersistentSyncClientDomain<double>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("Funds", 0);

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("Funds")
                .UsesClientDomain<FundsPersistentSyncClientDomain>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;

        protected override double DeserializePayload(byte[] payload, int numBytes)
        {
            return FundsSnapshotPayloadSerializer.Deserialize(payload, numBytes);
        }

        protected override bool CanApplyLiveState()
        {
            return Funding.Instance != null;
        }

        protected override void ApplyLiveState(double value)
        {
            ShareFundsSystem.Singleton.SetFundsWithoutTriggeringEvent(value);
        }
    }
}
