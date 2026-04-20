using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Reentrancy-safe depth counter used by client Share-progress systems to guard their
    /// <c>IgnoreEvents</c> / <c>Start/StopIgnoringEvents</c> surface against overlapping apply
    /// scopes.
    ///
    /// Semantics:
    /// <list type="bullet">
    /// <item><description><see cref="IsActive"/> is true iff <see cref="Depth"/> is greater than zero.</description></item>
    /// <item><description><see cref="Start"/> invokes <paramref name="saveState"/> only on the outermost entry
    /// (<see cref="Depth"/> transitions 0 → 1); nested entries reuse the already-captured baseline instead
    /// of overwriting it with intermediate state.</description></item>
    /// <item><description><see cref="Stop"/> only runs <paramref name="restoreState"/> on the outermost exit
    /// (<see cref="Depth"/> transitions 1 → 0) and only when that outermost caller requested it via
    /// <paramref name="restoreOldValue"/>. Inner Stops' <paramref name="restoreOldValue"/> is a no-op —
    /// they cannot roll back state while an outer scope is still active.</description></item>
    /// <item><description><see cref="Stop"/> runs <paramref name="restoreState"/> BEFORE decrementing to zero so
    /// events fired during restore remain suppressed by <see cref="IsActive"/>, matching the legacy
    /// "restore under suppression" contract that the bool-flag implementation provided implicitly.</description></item>
    /// <item><description>An unbalanced <see cref="Stop"/> (when <see cref="Depth"/> is already zero) returns
    /// false without mutating state so the caller can log a diagnostic; a balanced Stop returns true.</description></item>
    /// </list>
    ///
    /// Lives in LmpCommon so the primitive is unit-testable without KSP/Unity references. See
    /// <c>LmpCommonTest.ReentrantEventSuppressorTests</c> for the behavioral regression suite.
    /// </summary>
    public struct ReentrantEventSuppressor
    {
        private int _depth;

        /// <summary>Current nesting depth; zero means no active suppression scope.</summary>
        public int Depth => _depth;

        /// <summary>True iff a suppression scope is currently active (<see cref="Depth"/> &gt; 0).</summary>
        public bool IsActive => _depth > 0;

        /// <summary>Resets depth to zero without invoking any callbacks. Used on system enable/disable.</summary>
        public void Reset()
        {
            _depth = 0;
        }

        /// <summary>
        /// Enter a new suppression scope. <paramref name="saveState"/> is invoked exactly once on the
        /// outermost entry (depth 0 → 1) and never on nested entries.
        /// </summary>
        public void Start(Action saveState)
        {
            if (_depth == 0)
            {
                saveState?.Invoke();
            }

            _depth++;
        }

        /// <summary>
        /// Leave a suppression scope. Returns true on a balanced call, false when depth was already zero
        /// (caller should log the unbalanced Stop as a programming error).
        /// </summary>
        /// <param name="restoreState">Callback invoked only on the outermost exit (depth 1 → 0) and only
        /// when <paramref name="restoreOldValue"/> is true.</param>
        /// <param name="restoreOldValue">Only honored on the outermost Stop.</param>
        public bool Stop(Action restoreState, bool restoreOldValue)
        {
            if (_depth <= 0)
            {
                return false;
            }

            if (_depth == 1 && restoreOldValue)
            {
                restoreState?.Invoke();
            }

            _depth--;
            return true;
        }
    }
}
