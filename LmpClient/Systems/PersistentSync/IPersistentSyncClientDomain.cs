using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public interface IPersistentSyncClientDomain
    {
        string DomainId { get; }
        PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot);

        /// <summary>
        /// Attempts to apply any value cached from a snapshot once live game state can accept it.
        /// Returns <see cref="PersistentSyncApplyOutcome.Applied"/> only when pending work was committed this call.
        /// </summary>
        PersistentSyncApplyOutcome FlushPendingState();

        /// <summary>Activates local event producers and domain-owned lifecycle state for this session.</summary>
        void EnableDomainLifecycle();

        /// <summary>Stops local event producers and clears domain-owned lifecycle state for this session.</summary>
        void DisableDomainLifecycle();

        /// <summary>Runs queued local actions that are waiting for live KSP state to become ready.</summary>
        void FlushQueuedDomainActions();

        /// <summary>
        /// Side-effect-free serialization of current local scenario state for Domain Analyzer comparison.
        /// </summary>
        bool TrySerializeLocalAuditPayload(out byte[] payloadBytes, out int payloadNumBytes, out string unavailableReason);
    }
}
