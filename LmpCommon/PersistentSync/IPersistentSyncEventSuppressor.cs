namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Suppression surface for Persistent Sync client domains. Cross-domain snapshot apply uses
    /// <see cref="PersistentSyncDomainSuppressionScope"/> to start/stop local event producers on other
    /// registered domains by <see cref="DomainName"/> (wire domain id), without Share-progress types.
    /// </summary>
    public interface IPersistentSyncEventSuppressor
    {
        /// <summary>Wire/catalog domain id (e.g. <see cref="PersistentSyncDomainNames.Funds"/>).</summary>
        string DomainName { get; }

        bool IsSuppressingLocalEvents { get; }

        void StartSuppressingLocalEvents();

        void StopSuppressingLocalEvents(bool restoreOldValue = false);
    }
}
