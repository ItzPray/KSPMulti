using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public interface IPersistentSyncClientDomain
    {
        PersistentSyncDomainId DomainId { get; }
        PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot);
        void FlushPendingState();
    }
}
