using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareProgress
{
    public abstract class ShareProgressBaseSystem<T, TS, TH> : MessageSystem<T, TS, TH>, IShareProgressEventSuppressor
        where T : System<T>, new()
        where TS : class, IMessageSender, new()
        where TH : class, IMessageHandler, new()
    {
        /// <summary>
        /// Reentrant suppression state backing <see cref="IgnoreEvents"/>. Uses a depth counter, not a
        /// boolean, so nested Start/Stop pairs from overlapping apply scopes (e.g. a persistent-sync
        /// snapshot apply that internally triggers a second apply via stock callbacks) cannot drop a
        /// suppression prematurely or leave one stuck forever — concrete bug class observed when a
        /// contract completion ran while Achievements snapshots were being applied. Logic lives in
        /// <see cref="ReentrantEventSuppressor"/> (LmpCommon) so its semantics are covered by
        /// <c>LmpCommonTest.ReentrantEventSuppressorTests</c>; this class only owns the Save/Restore
        /// callbacks and the unbalanced-Stop diagnostic.
        /// </summary>
        private ReentrantEventSuppressor _suppressor;

        /// <summary>
        /// True while any code frame on this system is inside a <see cref="StartIgnoringEvents"/>/
        /// <see cref="StopIgnoringEvents"/> pair. Read by event handlers to suppress echoing server-applied
        /// state back as fresh client intents.
        /// </summary>
        public bool IgnoreEvents => _suppressor.IsActive;

        private Queue<Action> _actionQueue;

        protected abstract GameMode RelevantGameModes { get; }

        protected bool CurrentGameModeIsRelevant => (SettingsSystem.ServerSettings.GameMode & RelevantGameModes) != 0;

        /// <summary>
        /// When true, <see cref="OnEnabled"/> uses <see cref="IsShareSystemApplicableForSession"/> instead of
        /// <see cref="CurrentGameModeIsRelevant"/> so producers follow <see cref="LmpCommon.PersistentSync.PersistentSyncDomainApplicability"/>.
        /// All concrete share-progress systems in this client set this to true.
        /// </summary>
        protected virtual bool UseSessionApplicabilityInsteadOfGameModeMask => false;

        /// <summary>
        /// Gate for subscribing to stock events / running share queues when <see cref="UseSessionApplicabilityInsteadOfGameModeMask"/> is true.
        /// Otherwise <see cref="CurrentGameModeIsRelevant"/> is used.
        /// </summary>
        protected virtual bool IsShareSystemApplicableForSession() => CurrentGameModeIsRelevant;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            var applicable = UseSessionApplicabilityInsteadOfGameModeMask
                ? IsShareSystemApplicableForSession()
                : CurrentGameModeIsRelevant;
            if (!applicable) return;

            _suppressor.Reset();
            _actionQueue = new Queue<Action>();

            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, RunQueue));
        }

        /// <summary>
        /// Returns true if the system is in a ready state to be run
        /// </summary>
        protected abstract bool ShareSystemReady { get; }

        /// <summary>
        /// Saves the current state of the variables that this system is tracking.
        /// </summary>
        public virtual void SaveState()
        {
            //Implement your own stuff
        }

        /// <summary>
        /// Restores the state of variables this system is tracking from the last save.
        /// </summary>
        public virtual void RestoreState()
        {
            //Implement your own stuff
        }

        /// <summary>
        /// Start ignoring the incoming ksp events. Reentrant: calls nest with a matching
        /// <see cref="StopIgnoringEvents"/>. <see cref="SaveState"/> runs only on the outermost Start so
        /// nested scopes observe a single captured baseline rather than overwriting it with intermediate
        /// values. Only call this if you are sure ShareSystemReady() returns true. For example in a QueueAction().
        /// </summary>
        public void StartIgnoringEvents()
        {
            _suppressor.Start(SaveState);
        }

        /// <summary>
        /// Stop ignoring the incoming ksp events. Reentrant: matching inverse of
        /// <see cref="StartIgnoringEvents"/>. <see cref="RestoreState"/> runs only on the outermost Stop
        /// (the Stop that drops depth from 1 to 0) and only when that outermost Stop passes
        /// <paramref name="restoreOldValue"/>=true. Inner Stops' <paramref name="restoreOldValue"/> is a
        /// no-op — they cannot roll back state while an outer scope is still active, because the outer
        /// scope's caller may have changed state intentionally while suppressed.
        /// Only call this if you are sure ShareSystemReady() returns true. For example in a QueueAction().
        /// </summary>
        public void StopIgnoringEvents(bool restoreOldValue = false)
        {
            // RestoreState runs BEFORE depth drops to zero so events fired from inside RestoreState
            // (e.g. Reputation.Instance.SetReputation → OnReputationChanged) are still suppressed by
            // <see cref="IgnoreEvents"/> — matches legacy "restore under suppression" behavior that the
            // bool-flag implementation provided implicitly. Logic lives in <see cref="ReentrantEventSuppressor"/>.
            if (!_suppressor.Stop(RestoreState, restoreOldValue))
            {
                LunaLog.LogWarning($"[LMP] {SystemName}.StopIgnoringEvents called with depth=0; ignoring unbalanced Stop.");
            }
        }

        /// <summary>
        /// Queue an action that is dependent on the ActionDependency and will run
        /// if the ActionDependencyReady method returns true. For example a action like:
        /// Funding.Instance.SetFunds(1000, TransactionReasons.None);
        /// </summary>
        /// <param name="action"></param>
        public void QueueAction(Action action)
        {
            _actionQueue.Enqueue(action);
            RunQueue();
        }

        /// <summary>
        /// Run the queue and call the actions.
        /// </summary>
        private void RunQueue()
        {
            while (_actionQueue.Count > 0 && ShareSystemReady)
            {
                var action = _actionQueue.Dequeue();
                action?.Invoke();
            }
        }
    }
}
