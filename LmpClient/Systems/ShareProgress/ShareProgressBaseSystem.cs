using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareProgress
{
    public abstract class ShareProgressBaseSystem<T, TS, TH> : MessageSystem<T, TS, TH>, IShareProgressEventSuppressor
        where T : System<T>, new()
        where TS : class, IMessageSender, new()
        where TH : class, IMessageHandler, new()
    {
        public bool IgnoreEvents { get; protected set; }

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

            IgnoreEvents = false;
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
        /// Start ignoring the incoming ksp events.
        /// Only call this if you are sure ShareSystemReady() returns true.
        /// For example in an QueueAction().
        /// </summary>
        public void StartIgnoringEvents()
        {
            SaveState();
            IgnoreEvents = true;
        }

        /// <summary>
        /// Stop ignoring the incoming ksp events.
        /// Only call this if you are sure ShareSystemReady() returns true.
        /// For example in an QueueAction().
        /// </summary>
        public void StopIgnoringEvents(bool restoreOldValue = false)
        {
            if (restoreOldValue)
                RestoreState();

            IgnoreEvents = false;
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
