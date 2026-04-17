using LmpClient.Systems.ShareFunds;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class FundsPersistentSyncClientDomain : ScalarPersistentSyncClientDomain<double>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Funds;

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
