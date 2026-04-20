using System;
using System.Collections.Generic;
using LmpClient.Systems.ShareProgress;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Single primitive for the &quot;applying server state&quot; window on the client. Replaces hand-rolled
    /// <c>Start/StopIgnoringEvents</c> calls sprinkled across client domain apply paths.
    ///
    /// Usage:
    /// <code>
    /// using (ScenarioSyncApplyScope.Begin(peersToSilence))
    /// {
    ///     // write authoritative state into stock; KSP GameEvents fired from this code
    ///     // will be observed by the listed Share systems with IgnoreEvents = true.
    /// }
    /// </code>
    ///
    /// The scope is strictly restore-last-wins: on dispose every peer has <see cref="IShareProgressEventSuppressor.StopIgnoringEvents"/>
    /// called with <c>restoreOldValue: false</c>, matching the contract the previous hand-rolled sites used. Exceptions
    /// inside the scope do not leak the ignore flag.
    /// </summary>
    public readonly struct ScenarioSyncApplyScope : IDisposable
    {
        private readonly IReadOnlyList<IShareProgressEventSuppressor> _peers;

        private ScenarioSyncApplyScope(IReadOnlyList<IShareProgressEventSuppressor> peers)
        {
            _peers = peers;
        }

        public static ScenarioSyncApplyScope Begin(IReadOnlyList<IShareProgressEventSuppressor> peers)
        {
            if (peers == null)
            {
                return new ScenarioSyncApplyScope(Array.Empty<IShareProgressEventSuppressor>());
            }

            for (var i = 0; i < peers.Count; i++)
            {
                peers[i]?.StartIgnoringEvents();
            }

            return new ScenarioSyncApplyScope(peers);
        }

        public void Dispose()
        {
            if (_peers == null)
            {
                return;
            }

            for (var i = _peers.Count - 1; i >= 0; i--)
            {
                try
                {
                    _peers[i]?.StopIgnoringEvents(false);
                }
                catch
                {
                    // Swallow to guarantee every peer is restored even if one throws; a single peer raising during
                    // StopIgnoringEvents must not leave later peers stuck with IgnoreEvents == true.
                }
            }
        }
    }
}
