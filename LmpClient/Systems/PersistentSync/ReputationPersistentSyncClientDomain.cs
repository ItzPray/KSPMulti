using LmpClient.Systems.ShareReputation;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class ReputationPersistentSyncClientDomain : ScalarPersistentSyncClientDomain<float>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("Reputation", 2);

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("Reputation")
                .UsesClientDomain<ReputationPersistentSyncClientDomain>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;

        protected override float DeserializePayload(byte[] payload, int numBytes)
        {
            return ReputationSnapshotPayloadSerializer.Deserialize(payload, numBytes);
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
