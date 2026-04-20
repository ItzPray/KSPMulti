namespace LmpClient.Systems.ShareProgress
{
    /// <summary>
    /// Non-generic view over the <c>IgnoreEvents</c> surface on <see cref="ShareProgressBaseSystem{T,TS,TH}"/>.
    ///
    /// Exists so <c>ScenarioSyncApplyScope</c> can silence an arbitrary set of Share systems during server-state
    /// apply without taking a dependency on each concrete generic closure.
    /// </summary>
    public interface IShareProgressEventSuppressor
    {
        /// <summary>Begin suppressing KSP <c>GameEvents</c> handlers on this Share system.</summary>
        void StartIgnoringEvents();

        /// <summary>Stop suppressing events. When <paramref name="restoreOldValue"/> is true, the system restores tracked state captured at StartIgnoringEvents time.</summary>
        void StopIgnoringEvents(bool restoreOldValue = false);
    }
}
