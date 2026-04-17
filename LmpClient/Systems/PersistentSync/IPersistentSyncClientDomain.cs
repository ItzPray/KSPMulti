using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public interface IPersistentSyncClientDomain
    {
        PersistentSyncDomainId DomainId { get; }
        PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot);

        /// <summary>
        /// Attempts to apply any value cached from a snapshot once live game state can accept it.
        /// Returns <see cref="PersistentSyncApplyOutcome.Applied"/> only when pending work was committed this call.
        /// </summary>
        PersistentSyncApplyOutcome FlushPendingState();
    }
}
