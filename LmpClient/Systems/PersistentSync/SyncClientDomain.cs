using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.PersistentSync
{
    public abstract class SyncClientDomain<TPayload> : IPersistentSyncClientDomain, IPersistentSyncEventSuppressor
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

        /// <summary>
        /// Other Persistent Sync domains whose local producers must be suppressed while this domain applies a server snapshot.
        /// Use wire ids such as <see cref="PersistentSyncDomainNames.Funds"/>.
        /// </summary>
        protected virtual IReadOnlyList<string> DomainsToSuppressDuringApply => Array.Empty<string>();

        string IPersistentSyncEventSuppressor.DomainName => DomainId;

        bool IPersistentSyncEventSuppressor.IsSuppressingLocalEvents => IgnoreLocalEvents;

        void IPersistentSyncEventSuppressor.StartSuppressingLocalEvents()
        {
            StartIgnoringLocalEvents();
        }

        void IPersistentSyncEventSuppressor.StopSuppressingLocalEvents(bool restoreOldValue)
        {
            StopIgnoringLocalEvents(restoreOldValue);
        }

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

        bool IPersistentSyncClientDomain.TrySerializeLocalAuditPayload(out byte[] payloadBytes, out int payloadNumBytes, out string unavailableReason)
        {
            return TrySerializeLocalAuditPayloadCore(out payloadBytes, out payloadNumBytes, out unavailableReason);
        }

        /// <summary>
        /// Override <see cref="TryBuildLocalAuditPayload"/> to export live KSP state without mutating gameplay.
        /// </summary>
        protected virtual bool TryBuildLocalAuditPayload(out TPayload payload, out string unavailableReason)
        {
            payload = default;
            unavailableReason = "Local audit not implemented for this domain";
            return false;
        }

        private bool TrySerializeLocalAuditPayloadCore(out byte[] payloadBytes, out int payloadNumBytes, out string unavailableReason)
        {
            payloadBytes = null;
            payloadNumBytes = 0;
            if (!TryBuildLocalAuditPayload(out var payload, out unavailableReason))
            {
                return false;
            }

            payloadBytes = PersistentSyncPayloadSerializer.Serialize(payload);
            payloadNumBytes = payloadBytes.Length;
            unavailableReason = null;
            return true;
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
                using (PersistentSyncDomainSuppressionScope.Begin(
                    PersistentSyncEventSuppressorRegistry.Resolve(DomainsToSuppressDuringApply),
                    restoreOldValueOnDispose: false))
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

        /// <summary>
        /// Suppresses other domains by Persistent Sync wire id for the duration of the scope.
        /// </summary>
        protected IDisposable SuppressDomains(params string[] domainNames)
        {
            return PersistentSyncDomainSuppressionScope.Begin(
                PersistentSyncEventSuppressorRegistry.Resolve(domainNames),
                restoreOldValueOnDispose: false);
        }

        /// <summary>
        /// Resolves each client domain type to a wire id via <see cref="PersistentSyncDomainNaming.InferDomainName"/> then suppresses those domains.
        /// </summary>
        protected IDisposable SuppressDomains(params Type[] domainTypes)
        {
            if (domainTypes == null || domainTypes.Length == 0)
            {
                return PersistentSyncDomainSuppressionScope.Begin(
                    Array.Empty<IPersistentSyncEventSuppressor>(),
                    restoreOldValueOnDispose: false);
            }

            var names = new string[domainTypes.Length];
            for (var i = 0; i < domainTypes.Length; i++)
            {
                names[i] = PersistentSyncDomainNaming.InferDomainName(domainTypes[i]);
            }

            return SuppressDomains(names);
        }

        /// <summary>
        /// Starts local (<see cref="StartIgnoringLocalEvents"/>) suppression on this domain and cross-domain suppression on the listed ids.
        /// </summary>
        protected IDisposable SuppressLocalAndDomains(params string[] domainNames)
        {
            return new LocalAndCrossDomainScope(this, domainNames);
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

            // Stock scenario singletons may still hold defaults during join/load until ApplyLiveState runs.
            // Publishing before then sends bogus intents that the server accepts and rebroadcasts (scalar domains).
            if (!system.Reconciler.State.HasInitialSnapshot(DomainId))
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

        private sealed class LocalAndCrossDomainScope : IDisposable
        {
            private readonly SyncClientDomain<TPayload> _domain;
            private PersistentSyncDomainSuppressionScope _domainScope;

            public LocalAndCrossDomainScope(SyncClientDomain<TPayload> domain, string[] domainNames)
            {
                _domain = domain;
                domain.StartIgnoringLocalEvents();
                _domainScope = PersistentSyncDomainSuppressionScope.Begin(
                    PersistentSyncEventSuppressorRegistry.Resolve(domainNames),
                    restoreOldValueOnDispose: false);
            }

            public void Dispose()
            {
                _domainScope.Dispose();
                _domain.StopIgnoringLocalEvents();
            }
        }
    }
}
