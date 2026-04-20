namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Non-generic view over the <c>IgnoreEvents</c> surface used by client Share-progress systems.
    ///
    /// Exists so <see cref="ScenarioSyncApplyScope"/> can silence an arbitrary set of suppressors during
    /// server-state apply without taking a dependency on KSP-bound client types. Lives in LmpCommon so both
    /// the client implementations and unit tests can depend on it without pulling in Unity/KSP references.
    /// </summary>
    public interface IShareProgressEventSuppressor
    {
        /// <summary>Begin suppressing the underlying event/handler surface on this suppressor.</summary>
        void StartIgnoringEvents();

        /// <summary>
        /// Stop suppressing events. When <paramref name="restoreOldValue"/> is true the suppressor restores
        /// tracked state captured at StartIgnoringEvents time; when false, it drops the captured state.
        /// </summary>
        void StopIgnoringEvents(bool restoreOldValue = false);
    }
}
