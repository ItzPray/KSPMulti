using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using Server.System;
using System;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Internal scenario reducer pipeline shared by <see cref="SyncDomainStore{TPayload}"/> (public authoring
    /// surface). Owns revision counting, equality short-circuit, authority enforcement via
    /// <see cref="PersistentAuthorityPolicy"/>, and scenario write synchronization.
    ///
    /// Normal domains inherit <see cref="SyncDomainStore{TPayload}"/> only; this type stays public because
    /// <see cref="SyncDomainStore{TPayload}"/> is public (C# accessibility rules). Prefer the grep gate under
    /// <c>Scripts/VerifyPersistentSyncAuthoring.ps1</c> over subclassing this directly; see <c>AGENTS.md</c>
    /// &quot;Scenario Sync Domain Contract&quot;.
    /// </summary>
    public abstract class SyncDomainStoreBase<TCanonical> : IPersistentSyncServerDomain
        where TCanonical : class
    {
        private readonly object _stateLock = new object();

        private TCanonical _current;
        private long _revision;

        public virtual string DomainId
        {
            get
            {
                var domainName = PersistentSyncDomainNaming.InferDomainName(GetType());
                return PersistentSyncDomainCatalog.TryGetByName(domainName, out var definition)
                    ? definition.DomainId
                    : domainName;
            }
        }
        public abstract PersistentAuthorityPolicy AuthorityPolicy { get; }

        /// <summary>
        /// Name of the scenario node in <see cref="ScenarioStoreSystem.CurrentScenarios"/> this domain owns. Required so
        /// the base class can enforce the &quot;one scenario, one domain&quot; rule and scope the scenario write lock.
        /// </summary>
        protected virtual string ScenarioName
        {
            get
            {
                var domainName = PersistentSyncDomainNaming.InferDomainName(GetType());
                var stockScenario = (PersistentSyncStockScenarioAttribute)Attribute.GetCustomAttribute(
                    GetType(),
                    typeof(PersistentSyncStockScenarioAttribute));
                if (!string.IsNullOrEmpty(stockScenario?.ScenarioName))
                {
                    return stockScenario.ScenarioName;
                }

                var ownedScenario = (PersistentSyncOwnedScenarioAttribute)Attribute.GetCustomAttribute(
                    GetType(),
                    typeof(PersistentSyncOwnedScenarioAttribute));
                if (!string.IsNullOrEmpty(ownedScenario?.ScenarioName))
                {
                    return ownedScenario.ScenarioName;
                }

                return PersistentSyncDomainCatalog.TryGetByName(domainName, out var definition)
                    ? definition.ScenarioName
                    : null;
            }
        }

        /// <summary>
        /// Exposed only for the ScenarioSyncReducerPipelineTests base-class regression harness. Do not call from
        /// production reducer code; always go through <see cref="ApplyClientIntent"/> / <see cref="ApplyServerMutation"/>.
        /// </summary>
        internal TCanonical CurrentForTests => _current;

        /// <summary>Exposed for tests to assert equality short-circuit behavior.</summary>
        internal long RevisionForTests => _revision;

        /// <summary>
        /// Internal read used by projection domains (e.g. PartPurchases) to mirror the store's revision into
        /// their own wire snapshots. Reads under the state lock so projections observe the same revision
        /// they'd observe via <see cref="GetCurrentSnapshot"/>.
        /// </summary>
        internal long RevisionForProjection
        {
            get
            {
                lock (_stateLock) return _revision;
            }
        }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            lock (_stateLock)
            {
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario);
                    _current = LoadCanonical(scenario, createdFromScratch) ?? CreateEmpty();

                    var shouldWriteBack = scenario != null
                        && (createdFromScratch || ShouldWriteBackAfterLoad(_current, scenario));
                    if (shouldWriteBack)
                    {
                        try
                        {
                            var replacement = WriteCanonical(scenario, _current);
                            if (replacement != null && !ReferenceEquals(replacement, scenario))
                            {
                                ScenarioStoreSystem.CurrentScenarios[ScenarioName] = replacement;
                            }
                        }
                        catch (Exception ex)
                        {
                            LunaLog.Error($"[PersistentSync] {DomainId} LoadFromPersistence write-back failed (createdFromScratch={createdFromScratch}): {ex.Message}");
                        }
                    }
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            TCanonical snapshotState;
            long snapshotRevision;
            lock (_stateLock)
            {
                snapshotState = _current ?? CreateEmpty();
                snapshotRevision = _revision;
            }

            var payload = SerializeSnapshot(snapshotState) ?? Array.Empty<byte>();
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = snapshotRevision,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            if (data == null)
            {
                return Rejected();
            }

            return ApplyInternal(client, ExactPayload(data.Payload, data.NumBytes), data.ClientKnownRevision, data.Reason, isServerMutation: false);
        }

        /// <summary>
        /// Per-intent authority gate; must be declared explicitly by every concrete domain. The method is
        /// <c>abstract</c> rather than providing a default implementation so authority is never silently
        /// inherited: a new domain author cannot forget to declare which gate applies (see AGENTS.md
        /// &quot;Scenario Sync Domain Contract&quot; rule: &quot;Authority is declared once and enforced at the registry gate.&quot;).
        ///
        /// <list type="bullet">
        /// <item><description>For simple policy-based domains, implement with
        /// <c>=&gt; <see cref="AuthorizeByPolicy"/>(client);</c>.</description></item>
        /// <item><description>For mixed per-intent authority (e.g. Contracts), decode the payload and dispatch.</description></item>
        /// </list>
        /// </summary>
        public abstract bool AuthorizeIntent(ClientStructure client, byte[] payload);

        /// <summary>
        /// Canonical policy-based authority check — evaluates <see cref="AuthorityPolicy"/> through the
        /// registry. Use this from simple-domain <see cref="AuthorizeIntent"/> overrides; mixed-authority
        /// domains may call it as part of their per-intent dispatch where the policy still applies.
        /// </summary>
        protected bool AuthorizeByPolicy(ClientStructure client)
        {
            return PersistentSyncRegistry.ValidateClientMaySubmitIntent(client, this);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, string reason)
        {
            return ApplyInternal(null, payload ?? Array.Empty<byte>(), null, reason, isServerMutation: true);
        }

        /// <summary>
        /// Alternate apply path for projection domains that share this store's canonical state but speak a
        /// different wire format. Supplies a custom reducer in place of <see cref="ReduceIntent"/>; the rest
        /// of the pipeline (state lock, equality short-circuit, revision bump, scenario write) is reused.
        /// Internal so only same-assembly domain stores (e.g. PartPurchases projecting onto Technology) can
        /// invoke it.
        /// </summary>
        internal PersistentSyncDomainApplyResult ApplyWithCustomReduce(byte[] payload, long? clientKnownRevision, string reason, bool isServerMutation, Func<TCanonical, byte[], ReduceResult<TCanonical>> reducer)
        {
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));
            return ApplyInternal(null, payload ?? Array.Empty<byte>(), clientKnownRevision, reason, isServerMutation, reducer);
        }

        internal PersistentSyncDomainApplyResult ApplyWithCustomReduce<TPayload>(
            byte[] payload,
            long? clientKnownRevision,
            string reason,
            bool isServerMutation,
            Func<TCanonical, TPayload, ReduceResult<TCanonical>> reducer)
        {
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));
            return ApplyInternal(
                null,
                payload ?? Array.Empty<byte>(),
                clientKnownRevision,
                reason,
                isServerMutation,
                (current, rawPayload) => reducer(
                    current,
                    PersistentSyncPayloadSerializer.Deserialize<TPayload>(rawPayload, rawPayload.Length)));
        }

        private PersistentSyncDomainApplyResult ApplyInternal(ClientStructure client, byte[] payload, long? clientKnownRevision, string reason, bool isServerMutation, Func<TCanonical, byte[], ReduceResult<TCanonical>> customReducer = null)
        {
            payload = payload ?? Array.Empty<byte>();
            lock (_stateLock)
            {
                var previous = _current ?? (_current = CreateEmpty());
                ReduceResult<TCanonical> reduce;
                try
                {
                    reduce = customReducer != null
                        ? customReducer(previous, payload)
                        : ReduceIntent(client, previous, payload, reason, isServerMutation);
                }
                catch (Exception ex)
                {
                    LunaLog.Error($"[PersistentSync] {DomainId} ReduceIntent threw (isServerMutation={isServerMutation} reason={reason}): {ex.Message}");
                    return Rejected();
                }

                if (reduce == null || !reduce.Accepted)
                {
                    return Rejected(reduce);
                }

                var next = reduce.NextState ?? previous;

                // Equality short-circuit: base class enforces that reducers which produce an equivalent state
                // never bump Revision or rewrite the scenario. This is rule 4 of the Scenario Sync Domain Contract.
                if (AreEquivalent(previous, next))
                {
                    var replyBecauseRevisionDrift =
                        !isServerMutation && clientKnownRevision.HasValue && clientKnownRevision.Value != _revision;
                    var replyBecauseDomainForced = !isServerMutation && reduce.ForceReplyToOriginClient;
                    return new PersistentSyncDomainApplyResult
                    {
                        Accepted = true,
                        Changed = false,
                        ReplyToOriginClient = replyBecauseRevisionDrift || replyBecauseDomainForced,
                        ReplyToProducerClient = reduce.ReplyToProducerClient,
                        Snapshot = GetCurrentSnapshotLocked()
                    };
                }

                _current = next;
                _revision++;

                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    if (ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                    {
                        try
                        {
                            var replacement = WriteCanonical(scenario, _current);
                            if (replacement != null && !ReferenceEquals(replacement, scenario))
                            {
                                ScenarioStoreSystem.CurrentScenarios[ScenarioName] = replacement;
                            }
                        }
                        catch (Exception ex)
                        {
                            LunaLog.Error($"[PersistentSync] {DomainId} WriteCanonical failed (reason={reason}): {ex.Message}");
                        }
                    }
                }

                return new PersistentSyncDomainApplyResult
                {
                    Accepted = true,
                    Changed = true,
                    ReplyToOriginClient = false,
                    ReplyToProducerClient = reduce.ReplyToProducerClient,
                    Snapshot = GetCurrentSnapshotLocked()
                };
            }
        }

        private PersistentSyncDomainSnapshot GetCurrentSnapshotLocked()
        {
            var payload = SerializeSnapshot(_current ?? CreateEmpty()) ?? Array.Empty<byte>();
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = _revision,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        private static PersistentSyncDomainApplyResult Rejected(ReduceResult<TCanonical> reduce = null)
        {
            return new PersistentSyncDomainApplyResult
            {
                Accepted = false,
                Changed = false,
                ReplyToOriginClient = false,
                ReplyToProducerClient = reduce?.ReplyToProducerClient ?? false,
                Snapshot = null
            };
        }

        /// <summary>
        /// Read the canonical state from the scenario on server startup / universe load. <paramref name="scenario"/>
        /// may be null if the scenario is missing; implementations typically seed from settings in that case.
        /// </summary>
        protected abstract TCanonical LoadCanonical(ConfigNode scenario, bool createdFromScratch);

        /// <summary>
        /// Decode a payload (client intent or server mutation) against <paramref name="current"/>, returning a
        /// <see cref="ReduceResult{T}"/> describing the next state. Domains must not mutate <paramref name="current"/>
        /// in place: always return a new immutable snapshot or the same reference if the reduce was a no-op.
        /// </summary>
        protected abstract ReduceResult<TCanonical> ReduceIntent(ClientStructure client, TCanonical current, byte[] payload, string reason, bool isServerMutation);

        /// <summary>
        /// Rebuild the scenario node graph from <paramref name="canonical"/>. Called under the scenario write lock
        /// after a state-changing reduce. The base class normally re-uses the passed-in <paramref name="scenario"/>
        /// node, but LunaConfigNode's serializer retains raw pre-parse text for nodes it did not construct from
        /// scratch; domains that need a full subtree rewrite may return a freshly-constructed ConfigNode from
        /// this method to have the base class swap it into <see cref="ScenarioStoreSystem.CurrentScenarios"/>.
        /// Text-based synthesis is allowed as an internal implementation detail of WriteCanonical itself, but
        /// callers outside this method must not mutate scenario text.
        /// </summary>
        /// <returns>Null or <paramref name="scenario"/> for in-place mutation; a new ConfigNode to replace the
        /// stored scenario.</returns>
        protected abstract ConfigNode WriteCanonical(ConfigNode scenario, TCanonical canonical);

        /// <summary>Serialize the canonical state into the wire payload broadcast to clients.</summary>
        protected abstract byte[] SerializeSnapshot(TCanonical canonical);

        /// <summary>
        /// Equality for the revision short-circuit. Must return true when the inputs would produce the exact same
        /// <see cref="SerializeSnapshot"/> payload, otherwise revisions drift unnecessarily and clients re-apply
        /// no-ops. See AGENTS.md rule: &quot;Equality short-circuits revisions&quot;.
        /// </summary>
        protected abstract bool AreEquivalent(TCanonical a, TCanonical b);

        /// <summary>
        /// Return a neutral canonical value to use before the first <see cref="LoadFromPersistence"/>.
        /// Default: abstract-derived; override only if a meaningful empty exists.
        /// </summary>
        protected abstract TCanonical CreateEmpty();

        /// <summary>
        /// Optional hook: domains that perform cleanup (dedupe, normalization, seeding) during
        /// <see cref="LoadCanonical"/> return true here so the base class rewrites the scenario with the cleaned
        /// canonical. Default: false; a createdFromScratch load always rewrites regardless of this hook.
        /// Called under the state + scenario locks so implementations may read <paramref name="loaded"/> and
        /// <paramref name="scenario"/> freely.
        /// </summary>
        protected virtual bool ShouldWriteBackAfterLoad(TCanonical loaded, ConfigNode scenario) => false;

        private static byte[] ExactPayload(byte[] payload, int numBytes)
        {
            payload = payload ?? Array.Empty<byte>();
            if (numBytes < 0)
            {
                numBytes = 0;
            }

            if (numBytes >= payload.Length)
            {
                return payload;
            }

            var exact = new byte[numBytes];
            Buffer.BlockCopy(payload, 0, exact, 0, numBytes);
            return exact;
        }
    }

}
