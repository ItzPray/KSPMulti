using LmpClient.Systems.ShareReputation;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class ReputationPersistentSyncClientDomain : SyncClientDomain<float>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<ReputationPersistentSyncClientDomain>();
        }

        protected override bool CanApplyLiveState()
        {
            return Reputation.Instance != null;
        }

        protected override void ApplyLiveState(float value)
        {
            ShareReputationSystem.Singleton.SetReputationWithoutTriggeringEvent(value);
        }
    }
}
