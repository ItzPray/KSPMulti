using System;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Applies server-authoritative state while listed Persistent Sync domains have their local producers
    /// suppressed. Matches <see cref="ScenarioSyncApplyScope"/> dispose semantics: reverse-order stop, swallow
    /// exceptions per suppressor so one failure cannot strand peers.
    /// </summary>
    public readonly struct PersistentSyncDomainSuppressionScope : IDisposable
    {
        private readonly IReadOnlyList<IPersistentSyncEventSuppressor> _suppressors;
        private readonly bool _restoreOldValueOnDispose;

        private PersistentSyncDomainSuppressionScope(IReadOnlyList<IPersistentSyncEventSuppressor> suppressors, bool restoreOldValueOnDispose)
        {
            _suppressors = suppressors;
            _restoreOldValueOnDispose = restoreOldValueOnDispose;
        }

        /// <param name="suppressors">Resolved suppressors in outer scope order (index 0 entered first).</param>
        public static PersistentSyncDomainSuppressionScope Begin(IReadOnlyList<IPersistentSyncEventSuppressor> suppressors, bool restoreOldValueOnDispose = false)
        {
            if (suppressors == null || suppressors.Count == 0)
            {
                return new PersistentSyncDomainSuppressionScope(Array.Empty<IPersistentSyncEventSuppressor>(), restoreOldValueOnDispose);
            }

            for (var i = 0; i < suppressors.Count; i++)
            {
                suppressors[i]?.StartSuppressingLocalEvents();
            }

            return new PersistentSyncDomainSuppressionScope(suppressors, restoreOldValueOnDispose);
        }

        public void Dispose()
        {
            if (_suppressors == null || _suppressors.Count == 0)
            {
                return;
            }

            for (var i = _suppressors.Count - 1; i >= 0; i--)
            {
                try
                {
                    _suppressors[i]?.StopSuppressingLocalEvents(_restoreOldValueOnDispose);
                }
                catch
                {
                    // Same contract as ScenarioSyncApplyScope: never strand later suppressors.
                }
            }
        }
    }
}
