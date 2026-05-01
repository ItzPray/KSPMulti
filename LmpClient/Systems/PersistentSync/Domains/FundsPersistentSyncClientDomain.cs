using LmpClient.Systems.ShareFunds;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class FundsPersistentSyncClientDomain : SyncClientDomain<double>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<FundsPersistentSyncClientDomain>();
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
