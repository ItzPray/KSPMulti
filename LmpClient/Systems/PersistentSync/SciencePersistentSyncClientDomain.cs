using LmpClient.Systems.ShareScience;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class SciencePersistentSyncClientDomain : ScalarPersistentSyncClientDomain<float>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Science;

        protected override float DeserializePayload(byte[] payload, int numBytes)
        {
            return ScienceSnapshotPayloadSerializer.Deserialize(payload, numBytes);
        }

        protected override bool CanApplyLiveState()
        {
            return ResearchAndDevelopment.Instance != null;
        }

        protected override void ApplyLiveState(float value)
        {
            ShareScienceSystem.Singleton.SetScienceWithoutTriggeringEvent(value);
        }
    }
}
