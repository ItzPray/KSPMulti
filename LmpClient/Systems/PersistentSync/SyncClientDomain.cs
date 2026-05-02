using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.PersistentSync
{
    public abstract class SyncClientDomain<TPayload> : IPersistentSyncClientDomain
    {
        private readonly Queue<Action> _actionQueue = new Queue<Action>();
        private ReentrantEventSuppressor _suppressor;
        private bool _hasPendingPayload;
        private TPayload _pendingPayload;
        private bool _lifecycleEnabled;

        public PersistentSyncDomainDefinition Definition => PersistentSyncDomainCatalog.GetByName(DomainName);
        public string DomainName => PersistentSyncDomainNaming.InferDomainName(GetType());
        public string DomainId => PersistentSyncDomainCatalog.TryGetByName(DomainName, out var definition)
            ? definition.DomainId
            : DomainName;

        protected bool IgnoreLocalEvents => _suppressor.IsActive;

        protected virtual IReadOnlyList<IShareProgressEventSuppressor> PeersToSilence => new IShareProgressEventSuppressor[0];

        public void EnableDomainLifecycle()
        {
            if (_lifecycleEnabled)
            {
                return;
            }

            _suppressor.Reset();
            _actionQueue.Clear();
            _lifecycleEnabled = true;
            OnDomainEnabled();
            OnPersistentSyncLive();
        }

        public void DisableDomainLifecycle()
        {
            if (!_lifecycleEnabled)
            {
                return;
            }

            OnPersistentSyncStopped();
            OnDomainDisabled();
            _actionQueue.Clear();
            _suppressor.Reset();
            _lifecycleEnabled = false;
        }

        public void FlushQueuedDomainActions()
        {
            while (_actionQueue.Count > 0 && CanRunQueuedActions())
            {
                _actionQueue.Dequeue()?.Invoke();
            }
        }

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            try
            {
                _pendingPayload = PersistentSyncPayloadSerializer.Deserialize<TPayload>(snapshot.Payload ?? Array.Empty<byte>(), snapshot.NumBytes);
                _hasPendingPayload = true;
                OnPayloadBuffered(snapshot, _pendingPayload);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public virtual PersistentSyncApplyOutcome FlushPendingState()
        {
            if (!_hasPendingPayload)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (!CanApplyLiveState())
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            try
            {
                using (ScenarioSyncApplyScope.Begin(PeersToSilence))
                {
                    ApplyLiveState(_pendingPayload);
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            var applied = _pendingPayload;
            _hasPendingPayload = false;
            OnPayloadApplied(applied);
            return PersistentSyncApplyOutcome.Applied;
        }

        protected virtual bool CanApplyLiveState()
        {
            return true;
        }

        protected virtual bool CanRunQueuedActions()
        {
            return CanApplyLiveState();
        }

        protected void StartIgnoringLocalEvents()
        {
            _suppressor.Start(SaveLocalState);
        }

        protected void StopIgnoringLocalEvents(bool restoreOldValue = false)
        {
            if (!_suppressor.Stop(RestoreLocalState, restoreOldValue))
            {
                LunaLog.LogWarning($"[KSPMP] {DomainName}.StopIgnoringLocalEvents called with depth=0; ignoring unbalanced Stop.");
            }
        }

        protected void QueueDomainAction(Action action)
        {
            _actionQueue.Enqueue(action);
            FlushQueuedDomainActions();
        }

        protected void SendLocalPayload(TPayload payload, string reason)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(DomainId))
            {
                return;
            }

            var system = PersistentSyncSystem.Singleton;
            if (system == null)
            {
                return;
            }

            PersistentSyncSystem.SendIntent(DomainId, system.GetKnownRevision(DomainId), payload, reason);
        }

        protected void SendScenarioScalar(TPayload payload, string reason)
        {
            SendLocalPayload(payload, reason);
        }

        /// <summary>
        /// Runs <paramref name="applyMutation"/> under <see cref="StartIgnoringLocalEvents"/> / <see cref="StopIgnoringLocalEvents"/>.
        /// Use in <see cref="ApplyLiveState"/> for scalar writes that would otherwise fire stock change events and echo as client intents.
        /// </summary>
        protected void ApplyLiveStateWithLocalSuppression(Action applyMutation)
        {
            StartIgnoringLocalEvents();
            try
            {
                applyMutation?.Invoke();
            }
            finally
            {
                StopIgnoringLocalEvents();
            }
        }

        protected virtual void ApplyLiveState(TPayload payload) { }
        protected virtual void OnDomainEnabled() { }
        protected virtual void OnDomainDisabled() { }
        protected virtual void SaveLocalState() { }
        protected virtual void RestoreLocalState() { }
        protected virtual void OnPersistentSyncLive() { }
        protected virtual void OnPersistentSyncStopped() { }
        protected virtual void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, TPayload payload) { }
        protected virtual void OnPayloadApplied(TPayload payload) { }
    }
}
