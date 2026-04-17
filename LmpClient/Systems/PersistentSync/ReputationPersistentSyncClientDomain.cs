using LmpClient.Systems.ShareReputation;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class ReputationPersistentSyncClientDomain : ScalarPersistentSyncClientDomain<float>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Reputation;

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
