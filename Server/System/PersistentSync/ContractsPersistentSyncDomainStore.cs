using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using Server.Properties;
using Server.Settings.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Authoritative server-side contract store. Migrated onto <see cref="ScenarioSyncDomainStore{TCanonical}"/>
    /// per the Scenario Sync Domain Contract.
    ///
    /// Contracts has mixed authority per intent kind:
    /// <list type="bullet">
    /// <item><description>Player commands (Accept/Decline/Cancel/RequestOfferGeneration) — any client.</description></item>
    /// <item><description>Producer-only observations for the <b>offer pool</b> (<see cref="ContractIntentPayloadKind.OfferObserved"/>)
    /// and <see cref="ContractIntentPayloadKind.FullReconcile"/> — only the current contract lock owner.</description></item>
    /// <item><description>In-sim Active contract observations (<see cref="ContractIntentPayloadKind.ParameterProgressObserved"/>,
    /// <see cref="ContractIntentPayloadKind.ContractCompletedObserved"/>, <see cref="ContractIntentPayloadKind.ContractFailedObserved"/>)
    /// — any client; <see cref="ReduceObservedActive"/> rejects unless the canonical row is Active so offer-pool spam cannot apply.</description></item>
    /// </list>
    /// Per-intent dispatch happens in <see cref="AuthorizeIntent"/>, called by the registry before the reducer
    /// ever sees the payload. The reducer itself does not perform authority checks; rule: &quot;No domain may do its
    /// own LockQuery check inside ReduceIntent&quot; (AGENTS.md).
    ///
    /// <see cref="WriteCanonical"/> rebuilds the scenario node via the LunaConfigNode graph API (no text
    /// splicing) by constructing a fresh <c>ScenarioModule</c> node, copying scalar values and non-CONTRACTS
    /// child nodes from the old scenario, and attaching a freshly-built CONTRACTS node derived from canonical
    /// state. Every node in the emitted subtree &mdash; the scenario wrapper, the CONTRACTS wrapper, each
    /// CONTRACT leaf, and each nested PARAMETER child &mdash; is created via the name-only graph constructor
    /// (<c>new ConfigNode(name, null)</c>) so no text-backed node survives out of <c>WriteCanonical</c>. This
    /// sidesteps the LunaConfigNode text cache that previously made <c>RemoveNode</c>/<c>AddNode</c> edits on
    /// text-backed nodes invisible to <c>ToString()</c>.
    /// </summary>
    public sealed class ContractsPersistentSyncDomainStore : ScenarioSyncDomainStore<ContractsPersistentSyncDomainStore.Canonical>
    {
        private const string ContractsNodeName = "CONTRACTS";
        private const string ContractNodeName = "CONTRACT";
        private const string GuidFieldName = "guid";
        private const string StateFieldName = "state";
        private const string TypeFieldName = "type";
        private const string TitleFieldName = "title";
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

        /// <summary>
        /// Hard ceiling on the total number of <see cref="ContractSnapshotPlacement.Current"/> (offered) records
        /// retained in canonical state. Chosen well above stock's per-client tier caps (~15) so a multi-client
        /// session can legitimately accumulate offers across prestige tiers, while still keeping snapshot payloads
        /// bounded (previous uncapped behavior produced ~950 rows / ~1.8 MB payloads that re-broadcast on every
        /// observation). When exceeded, the oldest offers by <c>Order</c> are evicted.
        /// </summary>
        private const int MaxOfferedPoolSize = 60;

        /// <summary>
        /// Per-contract-type ceiling on offered rows. Stock offers 1-2 of each type simultaneously; 3 leaves a
        /// little slack for in-flight intents and avoids a single template (e.g. "Escape the atmosphere!")
        /// dominating the pool when time-warp generation bursts produce a new GUID every tick.
        /// </summary>
        private const int MaxOfferedPerContractType = 3;

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Contracts;

        /// <summary>
        /// Floor policy advertised to clients and the registry's default path. Real gating happens in
        /// <see cref="AuthorizeIntent"/> because Contracts has mixed per-intent authority. Advertising
        /// <see cref="PersistentAuthorityPolicy.AnyClientIntent"/> keeps player commands responsive; offer-pool
        /// producer intents and full reconcile are rejected for non–lock-holders in <see cref="AuthorizeIntent"/>.
        /// </summary>
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "ContractSystem";

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes)
        {
            if (TryDeserializeContractIntentPayload(payload, numBytes, out var intentPayload))
            {
                switch (intentPayload.Kind)
                {
                    case ContractIntentPayloadKind.AcceptContract:
                    case ContractIntentPayloadKind.DeclineContract:
                    case ContractIntentPayloadKind.CancelContract:
                    case ContractIntentPayloadKind.RequestOfferGeneration:
                        return true;
                    case ContractIntentPayloadKind.ParameterProgressObserved:
                    case ContractIntentPayloadKind.ContractCompletedObserved:
                    case ContractIntentPayloadKind.ContractFailedObserved:
                        // Flying player often does not hold the contract lock (lock owner = offer generation). Reducer
                        // still requires canonical Active target — see ReduceObservedActive.
                        return true;
                    case ContractIntentPayloadKind.OfferObserved:
                    case ContractIntentPayloadKind.FullReconcile:
                        return ClientOwnsProducerAuthority(client);
                    default:
                        return false;
                }
            }

            // Envelope fallback: legacy full-replace / raw-row paths require producer authority.
            return ClientOwnsProducerAuthority(client);
        }

        protected override Canonical CreateEmpty()
        {
            return new Canonical(new Dictionary<Guid, ContractSnapshotInfo>());
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var contractsByGuid = new Dictionary<Guid, ContractSnapshotInfo>();

            // Match PersistentSyncDomainApplicability / ScenarioSystem: any save that includes the Career bit.
            var careerContracts = (GeneralSettings.SettingsStore.GameMode & GameMode.Career) != 0;

            if (scenario == null)
            {
                if (!careerContracts)
                {
                    return new Canonical(contractsByGuid);
                }

                // Fall through to seeding path below after constructing a synthetic scenario. The base class
                // will write it back via the createdFromScratch path in LoadFromPersistence.
                scenario = new ConfigNode(Resources.ContractSystem);
                ScenarioStoreSystem.CurrentScenarios[ScenarioName] = scenario;
            }

            var cleanedDuringLoad = IngestContractsFromScenario(scenario, contractsByGuid);

            if (careerContracts && contractsByGuid.Count == 0)
            {
                if (TryPopulateContractsFromEmbeddedTemplate(contractsByGuid))
                {
                    cleanedDuringLoad = true;
                }
            }

            if (careerContracts)
            {
                LunaLog.Normal(
                    $"[PersistentSync] Contracts LoadFromPersistence: gameMode={GeneralSettings.SettingsStore.GameMode} contractRows={contractsByGuid.Count} cleanedDuringLoad={cleanedDuringLoad}");
            }

            _loadRequiresWriteBack = cleanedDuringLoad;
            return new Canonical(contractsByGuid);
        }

        protected override bool ShouldWriteBackAfterLoad(Canonical loaded, ConfigNode scenario)
        {
            var requires = _loadRequiresWriteBack;
            _loadRequiresWriteBack = false;
            return requires;
        }

        // Communicates from LoadCanonical back up to LoadFromPersistence / ShouldWriteBackAfterLoad. Safe because
        // LoadFromPersistence holds the state lock for the duration of both calls (see ScenarioSyncDomainStore).
        private bool _loadRequiresWriteBack;

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
        {
            // Authority was already enforced by AuthorizeIntent at the registry gate for client intents; server
            // mutations are trusted by construction. The reducer is pure state-transition logic here.
            if (TryDeserializeContractIntentPayload(payload, numBytes, out var intentPayload))
            {
                return ReduceTypedIntent(current, intentPayload);
            }

            var envelope = ContractSnapshotPayloadSerializer.DeserializeEnvelope(payload, numBytes);
            return envelope.Mode == ContractSnapshotPayloadMode.FullReplace
                ? ReduceFullReplace(current, envelope.Contracts)
                : ReduceRecords(current, envelope.Contracts);
        }

        private ReduceResult<Canonical> ReduceTypedIntent(Canonical current, ContractIntentPayload intentPayload)
        {
            switch (intentPayload.Kind)
            {
                case ContractIntentPayloadKind.AcceptContract:
                    // Preferred path: client carries the post-Accept contract record (dateAccepted, dateDeadline,
                    // subclass runtime fields populated by stock OnAccepted overrides). Using it as the new
                    // canonical record keeps the server row consistent with Active-state invariants so the echoed
                    // snapshot does not wipe the accepting client's live Contract into a deadline-expired one.
                    // Fallback (null snapshot, pre-fix clients): keep the state-only rewrite so the command still
                    // advances canonical state even though the stored row's runtime fields remain Offered-era.
                    return intentPayload.Contract != null
                        ? ReduceAcceptWithProvidedSnapshot(current, intentPayload.ContractGuid, intentPayload.Contract)
                        : ReduceCommandStateRewrite(current, intentPayload.ContractGuid, "Active", ContractSnapshotPlacement.Active, requireOfferPool: true);
                case ContractIntentPayloadKind.DeclineContract:
                    return ReduceCommandStateRewrite(current, intentPayload.ContractGuid, "Declined", ContractSnapshotPlacement.Finished, requireOfferPool: true);
                case ContractIntentPayloadKind.CancelContract:
                    return ReduceCommandStateRewrite(current, intentPayload.ContractGuid, "Cancelled", ContractSnapshotPlacement.Finished, requireOfferPool: false, requireActive: true);
                case ContractIntentPayloadKind.RequestOfferGeneration:
                    return ReduceOfferGenerationRequest(current);
                case ContractIntentPayloadKind.OfferObserved:
                    return ReduceObservedOffer(current, intentPayload);
                case ContractIntentPayloadKind.ParameterProgressObserved:
                case ContractIntentPayloadKind.ContractCompletedObserved:
                case ContractIntentPayloadKind.ContractFailedObserved:
                    return ReduceObservedActive(current, intentPayload);
                case ContractIntentPayloadKind.FullReconcile:
                    return ReduceFullReplace(current, intentPayload.Contracts);
                default:
                    return ReduceResult<Canonical>.Reject();
            }
        }

        private static ReduceResult<Canonical> ReduceCommandStateRewrite(Canonical current, Guid contractGuid, string targetState, ContractSnapshotPlacement placement, bool requireOfferPool = false, bool requireActive = false)
        {
            if (!current.ContractsByGuid.TryGetValue(contractGuid, out var existing))
            {
                return ReduceResult<Canonical>.Reject();
            }

            if (requireOfferPool && !IsOfferPoolContract(existing)) return ReduceResult<Canonical>.Reject();
            if (requireActive && !IsActiveContract(existing)) return ReduceResult<Canonical>.Reject();

            var rewritten = RewriteContractState(existing, targetState, placement);
            return ReduceSingleRecord(current, rewritten);
        }

        /// <summary>
        /// Reducer for the enriched <see cref="ContractIntentPayloadKind.AcceptContract"/> path where the client
        /// supplies the full post-Accept contract record alongside the command. The state-machine gate remains the
        /// same as the legacy rewrite path — the target contract must exist in canonical state and be an
        /// offer-pool row — but the accepted data is the client-provided snapshot (carrying the post-Accept
        /// runtime fields) instead of the stale Offered-era bytes that <see cref="RewriteContractState"/> would
        /// edit. The client cannot downgrade an already-Active or already-Finished row through this path: the
        /// <see cref="IsOfferPoolContract"/> gate mirrors the Offered -&gt; Active one-way transition invariant
        /// from <see cref="ReduceFullReplace"/>.
        /// </summary>
        private static ReduceResult<Canonical> ReduceAcceptWithProvidedSnapshot(Canonical current, Guid contractGuid, ContractSnapshotInfo providedSnapshot)
        {
            if (providedSnapshot == null || providedSnapshot.ContractGuid != contractGuid || contractGuid == Guid.Empty)
            {
                return ReduceResult<Canonical>.Reject();
            }

            if (!current.ContractsByGuid.TryGetValue(contractGuid, out var existing) || !IsOfferPoolContract(existing))
            {
                return ReduceResult<Canonical>.Reject();
            }

            // Force the canonical row into the Active placement/state even if the client's snapshot says otherwise
            // (e.g. a raced client whose local Contract already ticked to DeadlineExpired before the wire send).
            // The Accept command is semantically "move this Offered row to Active"; honoring any other state here
            // would let clients use Accept to smuggle arbitrary state transitions through the offer-pool gate.
            var normalized = ContractSnapshotInfoComparer.Clone(providedSnapshot);
            normalized.ContractState = "Active";
            normalized.Placement = ContractSnapshotPlacement.Active;
            return ReduceSingleRecord(current, normalized);
        }

        private static ReduceResult<Canonical> ReduceOfferGenerationRequest(Canonical current)
        {
            // This intent never changes canonical state; we return the same state so the base class's
            // equality short-circuit collapses it to an accepted no-op. The producer-replay routing is
            // signaled via ReplyToProducerClient when the offer pool is empty.
            var offerPoolEmpty = !current.ContractsByGuid.Values.Any(IsOfferPoolContract);
            return ReduceResult<Canonical>.Accept(current, replyToProducerClient: offerPoolEmpty);
        }

        private static ReduceResult<Canonical> ReduceObservedOffer(Canonical current, ContractIntentPayload intentPayload)
        {
            if (!TryGetSingleIntentContract(intentPayload, out var incoming) || !IsOfferPoolContract(incoming))
            {
                return ReduceResult<Canonical>.Reject();
            }

            return ReduceSingleRecord(current, incoming);
        }

        private static ReduceResult<Canonical> ReduceObservedActive(Canonical current, ContractIntentPayload intentPayload)
        {
            if (!TryGetSingleIntentContract(intentPayload, out var incoming)
                || !current.ContractsByGuid.TryGetValue(incoming.ContractGuid, out var existing)
                || !IsActiveContract(existing))
            {
                return ReduceResult<Canonical>.Reject();
            }

            // Stock can fire parameter updates from every client; observers often lag (no vessel / crew loaded yet).
            // Without this guard, a regressed PARAM snapshot replaces canonical state and fights the client who
            // actually completed the step — endless snapshot ping-pong + server log spam.
            // When we repair the wire row against canonical completion, ForceReplyToOriginClient (via ReduceSingleRecord)
            // makes ScenarioSync echo the snapshot to the sender even if revision unchanged, so their UI matches.
            var forceOriginReply = false;
            if (intentPayload.Kind == ContractIntentPayloadKind.ParameterProgressObserved)
            {
                incoming = MergeParameterProgressObservationMonotonic(existing, incoming, out forceOriginReply);
            }

            return ReduceSingleRecord(current, incoming, forceOriginReply);
        }

        /// <summary>
        /// When merging a <see cref="ContractIntentPayloadKind.ParameterProgressObserved"/> row into canonical state,
        /// never let an incoming PARAM regress below what the server already recorded as <c>Complete</c> (same
        /// contract GUID, matched by stock PARAM <c>name</c>). Incoming may still advance incomplete → complete.
        /// </summary>
        private static ContractSnapshotInfo MergeParameterProgressObservationMonotonic(
            ContractSnapshotInfo canonicalExisting,
            ContractSnapshotInfo incoming,
            out bool repairedRegressionAgainstCanonical)
        {
            repairedRegressionAgainstCanonical = false;
            if (canonicalExisting == null || incoming == null || canonicalExisting.ContractGuid != incoming.ContractGuid)
            {
                return incoming;
            }

            try
            {
                var canonBody = Encoding.UTF8.GetString(canonicalExisting.Data, 0, canonicalExisting.NumBytes);
                var incomingClone = ContractSnapshotInfoComparer.Clone(incoming);
                var incomingBody = Encoding.UTF8.GetString(incomingClone.Data, 0, incomingClone.NumBytes);

                var canonGraph = BuildContractNodeFromBody(canonBody);
                var incomingGraph = BuildContractNodeFromBody(incomingBody);

                var canonParamsByName = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
                IndexContractParamNodesByName(canonGraph, canonParamsByName);
                repairedRegressionAgainstCanonical =
                    ApplyCanonicalCompleteParamGuardToIncoming(incomingGraph, canonParamsByName);

                var bodyText = StripContractWrapper(incomingGraph.ToString());
                var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
                incomingClone.NumBytes = bodyBytes.Length;
                incomingClone.Data = bodyBytes;
                return CanonicalizeRecordData(incomingClone);
            }
            catch
            {
                return incoming;
            }
        }

        private static void IndexContractParamNodesByName(ConfigNode node, Dictionary<string, ConfigNode> acc)
        {
            if (node == null)
            {
                return;
            }

            if (string.Equals(node.Name, "PARAM", StringComparison.OrdinalIgnoreCase))
            {
                var name = node.GetValue("name")?.Value?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    acc[name] = node;
                }
            }

            foreach (var child in node.GetAllNodes())
            {
                IndexContractParamNodesByName(child, acc);
            }
        }

        /// <returns><c>true</c> if any incoming PARAM was brought forward to match canonical completion.</returns>
        private static bool ApplyCanonicalCompleteParamGuardToIncoming(
            ConfigNode incomingRoot,
            IReadOnlyDictionary<string, ConfigNode> canonParamsByName)
        {
            if (incomingRoot == null || canonParamsByName == null || canonParamsByName.Count == 0)
            {
                return false;
            }

            var repaired = false;

            void Walk(ConfigNode node)
            {
                if (node == null)
                {
                    return;
                }

                if (string.Equals(node.Name, "PARAM", StringComparison.OrdinalIgnoreCase))
                {
                    var name = node.GetValue("name")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(name) &&
                        canonParamsByName.TryGetValue(name, out var canonParam) &&
                        IsContractParamNodeComplete(canonParam) &&
                        !IsContractParamNodeComplete(node))
                    {
                        OverlayContractParamScalarValuesFromSource(canonParam, node);
                        repaired = true;
                    }
                }

                foreach (var child in node.GetAllNodes())
                {
                    Walk(child);
                }
            }

            Walk(incomingRoot);
            return repaired;
        }

        private static bool IsContractParamNodeComplete(ConfigNode paramNode)
        {
            var st = paramNode?.GetValue("state")?.Value?.Trim();
            return string.Equals(st, "Complete", StringComparison.OrdinalIgnoreCase);
        }

        private static void OverlayContractParamScalarValuesFromSource(ConfigNode src, ConfigNode dest)
        {
            if (src == null || dest == null)
            {
                return;
            }

            foreach (var val in src.GetAllValues())
            {
                if (val.Key == null)
                {
                    continue;
                }

                while (dest.GetValues(val.Key).Any())
                {
                    dest.RemoveValue(val.Key);
                }

                dest.CreateValue(new CfgNodeValue<string, string>(val.Key, val.Value));
            }
        }

        private static ReduceResult<Canonical> ReduceSingleRecord(
            Canonical current,
            ContractSnapshotInfo incoming,
            bool forceReplyToOriginClient = false)
        {
            var nextMap = current.ContractsByGuid.ToDictionary(kv => kv.Key, kv => ContractSnapshotInfoComparer.Clone(kv.Value));
            var nextOrder = nextMap.Any() ? nextMap.Values.Max(c => c.Order) + 1 : 0;
            ApplyCanonicalRecord(nextMap, incoming, ref nextOrder);
            return ReduceResult<Canonical>.Accept(new Canonical(nextMap), replyToProducerClient: false, forceReplyToOriginClient);
        }

        private static ReduceResult<Canonical> ReduceRecords(Canonical current, IEnumerable<ContractSnapshotInfo> incoming)
        {
            var nextMap = current.ContractsByGuid.ToDictionary(kv => kv.Key, kv => ContractSnapshotInfoComparer.Clone(kv.Value));
            var nextOrder = nextMap.Any() ? nextMap.Values.Max(c => c.Order) + 1 : 0;
            foreach (var rec in incoming ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                ApplyCanonicalRecord(nextMap, rec, ref nextOrder);
            }

            return ReduceResult<Canonical>.Accept(new Canonical(nextMap));
        }

        /// <summary>
        /// Canonical invariant: the server's contract store must mirror the stock KSP contract state machine.
        /// Legitimate transitions are one-way: Offered -&gt; Active/Finished, Active -&gt; Finished, Finished is terminal.
        /// A producer's <c>FullReconcile</c> is an <b>upsert</b>, not a wipe: we preserve every canonical row whose
        /// incoming counterpart is missing or regresses state, regardless of placement (Offered, Active, or Finished).
        /// This protects player-committed work (accepted missions, completed history) AND the server-authoritative
        /// offer pool against producers whose local <c>ContractSystem</c> has transiently pruned rows — for example
        /// a reconnecting client whose stock <c>Contract.Update()</c> has withdrawn offers for expired deadlines
        /// immediately after the server snapshot applied, or a client whose per-tier offer caps locally truncated
        /// the list before the producer reconcile ran. Offers are retired by <i>state transition</i>
        /// (Offered -&gt; Withdrawn/Declined/etc. via the explicit command intents or a producer-proposed forward
        /// transition in the incoming set), never by omission from a producer reconcile.
        /// </summary>
        private static ReduceResult<Canonical> ReduceFullReplace(Canonical current, IEnumerable<ContractSnapshotInfo> incoming)
        {
            var incomingByGuid = new Dictionary<Guid, ContractSnapshotInfo>();
            foreach (var rec in incoming ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                if (rec == null || rec.ContractGuid == Guid.Empty) continue;
                incomingByGuid[rec.ContractGuid] = rec;
            }

            var nextMap = new Dictionary<Guid, ContractSnapshotInfo>();
            var nextOrder = 0;
            var preservedActive = 0;
            var preservedFinished = 0;
            var preservedOffered = 0;
            var rejectedRegressions = 0;

            // Step 1: walk canonical rows in Order so preserved state keeps stable ordering. Preserve every canonical
            // row unless the incoming record for that GUID represents a legitimate forward state transition; forward
            // transitions fall through and are applied from the incoming set below.
            foreach (var canonicalRec in current.ContractsByGuid.Values.OrderBy(c => c.Order).ThenBy(c => c.ContractGuid))
            {
                var isActive = IsActiveContract(canonicalRec);
                var isFinished = IsFinishedContract(canonicalRec);
                var isOffered = !isActive && !isFinished;

                if (incomingByGuid.TryGetValue(canonicalRec.ContractGuid, out var incomingRec) &&
                    IsForwardContractStateTransition(canonicalRec, incomingRec))
                {
                    continue;
                }

                if (incomingByGuid.Remove(canonicalRec.ContractGuid))
                {
                    rejectedRegressions++;
                }

                ApplyCanonicalRecord(nextMap, canonicalRec, ref nextOrder);
                if (isFinished) preservedFinished++;
                else if (isActive) preservedActive++;
                else if (isOffered) preservedOffered++;
            }

            // Step 2: apply remaining incoming records. These are genuinely new rows the producer has just observed
            // (freshly generated offers) or forward-transitioned rows the producer legitimately advanced.
            foreach (var incomingRec in incomingByGuid.Values.OrderBy(c => c.Order).ThenBy(c => c.ContractGuid))
            {
                ApplyCanonicalRecord(nextMap, incomingRec, ref nextOrder);
            }

            if (preservedActive > 0 || preservedFinished > 0 || preservedOffered > 0 || rejectedRegressions > 0)
            {
                LunaLog.Debug(
                    $"[PersistentSync] Contracts FullReplace merge preservedActive={preservedActive} " +
                    $"preservedFinished={preservedFinished} preservedOffered={preservedOffered} " +
                    $"rejectedRegressions={rejectedRegressions} canonicalRowsBefore={current.ContractsByGuid.Count} " +
                    $"finalRows={nextMap.Count}");
            }

            return ReduceResult<Canonical>.Accept(new Canonical(nextMap));
        }

        private static bool IsFinishedContract(ContractSnapshotInfo contract)
        {
            if (contract == null) return false;
            if (contract.Placement == ContractSnapshotPlacement.Finished) return true;
            var state = contract.ContractState?.Trim();
            if (string.IsNullOrEmpty(state)) return false;
            switch (state.ToLowerInvariant())
            {
                case "completed":
                case "deadlineexpired":
                case "failed":
                case "cancelled":
                case "declined":
                case "withdrawn":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Mirrors the stock KSP <c>Contract.State</c> machine. Allowed transitions (or identity):
        ///   Offered/Available -&gt; any state.
        ///   Active -&gt; Active (identity) or Finished (terminal).
        ///   Finished -&gt; Finished (identity only; terminal rows are immutable in the canonical store).
        /// Returns false for any regression (Active -&gt; Offered, Finished -&gt; non-Finished, etc.).
        /// </summary>
        private static bool IsForwardContractStateTransition(ContractSnapshotInfo canonical, ContractSnapshotInfo incoming)
        {
            if (canonical == null || incoming == null) return false;

            if (IsFinishedContract(canonical))
            {
                return IsFinishedContract(incoming);
            }

            if (IsActiveContract(canonical))
            {
                return IsActiveContract(incoming) || IsFinishedContract(incoming);
            }

            return true;
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
            // Build a fresh scenario ConfigNode via the LunaConfigNode graph API. Every node in the emitted
            // subtree is constructed with the name-only constructor (no backing text) and populated via
            // CreateValue/AddNode, so no text-backed node from the incoming scenario leaks through. This
            // sidesteps the LunaConfigNode pre-parse text cache that previously made RemoveNode/AddNode
            // edits on text-backed nodes invisible to ToString().
            var scenarioName = string.IsNullOrEmpty(scenario?.Name) ? ScenarioName : scenario.Name;
            var fresh = new ConfigNode(scenarioName, null);

            if (scenario != null)
            {
                foreach (var value in scenario.GetAllValues())
                {
                    fresh.CreateValue(new CfgNodeValue<string, string>(value.Key, value.Value));
                }

                foreach (var childNode in scenario.GetAllNodes())
                {
                    if (childNode == null) continue;
                    if (string.Equals(childNode.Name, ContractsNodeName, StringComparison.Ordinal)) continue;
                    fresh.AddNode(CloneIntoFreshGraphNode(childNode, childNode.Name));
                }
            }

            fresh.AddNode(BuildContractsNode(canonical));
            return fresh;
        }

        private static ConfigNode BuildContractsNode(Canonical canonical)
        {
            var contractsNode = new ConfigNode(ContractsNodeName, null);
            foreach (var contract in canonical.ContractsByGuid.Values.OrderBy(c => c.Order).ThenBy(c => c.ContractGuid))
            {
                var bodyText = Encoding.UTF8.GetString(contract.Data, 0, contract.NumBytes);
                contractsNode.AddNode(BuildContractNodeFromBody(bodyText));
            }

            return contractsNode;
        }

        /// <summary>
        /// Parses a header-less CONTRACT body into a transient text-backed ConfigNode, then deep-copies its
        /// values and children into a fresh graph-backed subtree. The transient parse is discarded; no
        /// text-backed node survives into the scenario graph. This eliminates the last remnant of the
        /// LunaConfigNode text-cache concern in the Contracts write path.
        /// </summary>
        private static ConfigNode BuildContractNodeFromBody(string bodyText)
        {
            var parsed = new ConfigNode(bodyText) { Name = ContractNodeName };
            return CloneIntoFreshGraphNode(parsed, ContractNodeName);
        }

        /// <summary>
        /// Recursively rebuild <paramref name="source"/> as a brand-new graph-backed ConfigNode (name-only
        /// constructor, no backing text). Values and child nodes are re-created via the graph API so no
        /// cached text from <paramref name="source"/> leaks into the returned subtree.
        /// </summary>
        private static ConfigNode CloneIntoFreshGraphNode(ConfigNode source, string nameOverride)
        {
            var fresh = new ConfigNode(nameOverride ?? source.Name, null);
            foreach (var value in source.GetAllValues())
            {
                fresh.CreateValue(new CfgNodeValue<string, string>(value.Key, value.Value));
            }

            foreach (var child in source.GetAllNodes())
            {
                if (child == null) continue;
                fresh.AddNode(CloneIntoFreshGraphNode(child, child.Name));
            }

            return fresh;
        }

        protected override byte[] SerializeSnapshot(Canonical canonical)
        {
            var orderedContracts = canonical.ContractsByGuid.Values
                .OrderBy(c => c.Order)
                .ThenBy(c => c.ContractGuid)
                .Select(ContractSnapshotInfoComparer.Clone)
                .ToArray();
            return ContractSnapshotPayloadSerializer.Serialize(orderedContracts);
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.ContractsByGuid.Count != b.ContractsByGuid.Count) return false;

            foreach (var kv in a.ContractsByGuid)
            {
                if (!b.ContractsByGuid.TryGetValue(kv.Key, out var other)) return false;
                if (kv.Value.Order != other.Order) return false;
                if (!ContractSnapshotInfoComparer.AreEquivalent(kv.Value, other)) return false;
            }

            return true;
        }

        private static bool IngestContractsFromScenario(ConfigNode scenario, Dictionary<Guid, ContractSnapshotInfo> contractsByGuid)
        {
            contractsByGuid.Clear();

            var contractsNode = scenario.GetNode(ContractsNodeName)?.Value;
            if (contractsNode == null) return false;

            var nextOrder = 0;
            var rawCount = 0;
            foreach (var contractNode in contractsNode.GetNodes(ContractNodeName).Select(n => n.Value).Where(n => n != null))
            {
                var snapshotInfo = CreateSnapshotInfo(contractNode, nextOrder);
                if (snapshotInfo != null)
                {
                    rawCount++;
                    ApplyCanonicalRecord(contractsByGuid, snapshotInfo, ref nextOrder);
                }
            }

            return contractsByGuid.Count != rawCount;
        }

        private static bool TryPopulateContractsFromEmbeddedTemplate(Dictionary<Guid, ContractSnapshotInfo> contractsByGuid)
        {
            ConfigNode templateRoot;
            try
            {
                templateRoot = new ConfigNode(Resources.ContractSystem);
            }
            catch (Exception ex)
            {
                LunaLog.Error($"[PersistentSync] Contracts: failed to parse embedded ContractSystem template: {ex.Message}");
                return false;
            }

            var templateContracts = templateRoot.GetNode(ContractsNodeName)?.Value;
            if (templateContracts == null) return false;

            var addedAny = false;
            var nextOrder = contractsByGuid.Any() ? contractsByGuid.Values.Max(c => c.Order) + 1 : 0;
            foreach (var templateContractWrapper in templateContracts.GetNodes(ContractNodeName))
            {
                var templateContract = templateContractWrapper.Value;
                if (templateContract == null) continue;

                var snapshotInfo = CreateSnapshotInfo(templateContract, nextOrder);
                if (snapshotInfo == null) continue;

                if (ApplyCanonicalRecord(contractsByGuid, snapshotInfo, ref nextOrder))
                {
                    addedAny = true;
                }
            }

            if (addedAny)
            {
                LunaLog.Warning("[PersistentSync] Contracts: universe ContractSystem had no readable offers; seeded starter contracts from embedded template.");
            }

            return addedAny;
        }

        private static bool TryDeserializeContractIntentPayload(byte[] payload, int numBytes, out ContractIntentPayload intentPayload)
        {
            intentPayload = null;
            try
            {
                intentPayload = ContractIntentPayloadSerializer.Deserialize(payload, numBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSingleIntentContract(ContractIntentPayload intentPayload, out ContractSnapshotInfo contract)
        {
            contract = intentPayload?.Contract != null
                ? ContractSnapshotInfoComparer.Clone(intentPayload.Contract)
                : null;

            if (contract == null || contract.ContractGuid == Guid.Empty)
            {
                return false;
            }

            if (intentPayload.ContractGuid != Guid.Empty && intentPayload.ContractGuid != contract.ContractGuid)
            {
                return false;
            }

            return true;
        }

        private static bool ClientOwnsProducerAuthority(ClientStructure client)
        {
            return client != null && LockSystem.LockQuery.ContractLockBelongsToPlayer(client.PlayerName);
        }

        private static bool IsOfferPoolContract(ContractSnapshotInfo contract)
        {
            return contract != null &&
                   contract.Placement == ContractSnapshotPlacement.Current &&
                   IsOfferLikeContractState(contract.ContractState);
        }

        private static bool IsActiveContract(ContractSnapshotInfo contract)
        {
            return contract != null &&
                   (contract.Placement == ContractSnapshotPlacement.Active ||
                    string.Equals(contract.ContractState?.Trim(), "Active", StringComparison.OrdinalIgnoreCase));
        }

        private static ContractSnapshotInfo RewriteContractState(ContractSnapshotInfo source, string targetState, ContractSnapshotPlacement placement)
        {
            var rewritten = ContractSnapshotInfoComparer.Clone(source);
            if (rewritten == null)
            {
                return null;
            }

            var contractNode = new ConfigNode(Encoding.UTF8.GetString(rewritten.Data, 0, rewritten.NumBytes));
            while (contractNode.GetValues(StateFieldName).Any())
            {
                contractNode.RemoveValue(StateFieldName);
            }

            contractNode.CreateValue(new CfgNodeValue<string, string>(StateFieldName, targetState ?? string.Empty));

            rewritten.ContractState = targetState ?? string.Empty;
            rewritten.Placement = placement;
            rewritten.Data = Encoding.UTF8.GetBytes(contractNode.ToString());
            rewritten.NumBytes = rewritten.Data.Length;
            return CanonicalizeRecordData(rewritten);
        }

        private static bool ApplyCanonicalRecord(Dictionary<Guid, ContractSnapshotInfo> map, ContractSnapshotInfo incomingRecord, ref int nextOrder)
        {
            if (incomingRecord == null || incomingRecord.ContractGuid == Guid.Empty)
            {
                return false;
            }

            var normalizedRecord = NormalizeRecord(incomingRecord, nextOrder);
            if (normalizedRecord.Order == nextOrder)
            {
                nextOrder++;
            }

            if (ShouldRejectIncomingOfferedDuplicateOfActive(map, normalizedRecord))
            {
                return false;
            }

            if (ShouldRejectIncomingOfferedDuplicateOfCompleted(map, normalizedRecord))
            {
                return false;
            }

            RemoveOlderOfferedDuplicatesOf(map, normalizedRecord);

            if (map.TryGetValue(normalizedRecord.ContractGuid, out var existingRecord))
            {
                normalizedRecord.Order = existingRecord.Order;
                if (!ContractSnapshotInfoComparer.AreEquivalent(existingRecord, normalizedRecord))
                {
                    map[normalizedRecord.ContractGuid] = normalizedRecord;
                }

                return false;
            }

            map[normalizedRecord.ContractGuid] = normalizedRecord;
            EnforceOfferedPoolCap(map, normalizedRecord);
            return true;
        }

        /// <summary>
        /// Evicts surplus offered rows when inserting <paramref name="newlyInserted"/> caused the offered pool to
        /// exceed <see cref="MaxOfferedPoolSize"/> or <see cref="MaxOfferedPerContractType"/>. Evictions are always
        /// oldest-first by <c>Order</c> and only touch offered (<see cref="ContractSnapshotPlacement.Current"/>)
        /// rows; active/finished/declined records are never evicted so accepted missions and history are preserved.
        /// The newly-inserted record itself is never evicted even if it is the oldest offered row, so a valid
        /// observation never silently no-ops due to caps alone.
        /// </summary>
        private static void EnforceOfferedPoolCap(Dictionary<Guid, ContractSnapshotInfo> map, ContractSnapshotInfo newlyInserted)
        {
            if (newlyInserted == null || !IsOfferPoolContract(newlyInserted))
            {
                return;
            }

            var newGuid = newlyInserted.ContractGuid;

            if (TryBuildContractIdentityKey(newlyInserted, out var insertedTypeKey))
            {
                EvictOldestOfferedMatching(
                    map,
                    newGuid,
                    candidate =>
                        TryBuildContractIdentityKey(candidate, out var existingKey) &&
                        string.Equals(existingKey, insertedTypeKey, StringComparison.Ordinal),
                    MaxOfferedPerContractType,
                    "per-type cap");
            }

            EvictOldestOfferedMatching(
                map,
                newGuid,
                _ => true,
                MaxOfferedPoolSize,
                "total cap");
        }

        private static void EvictOldestOfferedMatching(
            Dictionary<Guid, ContractSnapshotInfo> map,
            Guid protectedGuid,
            Func<ContractSnapshotInfo, bool> selector,
            int keepCount,
            string reason)
        {
            if (keepCount <= 0)
            {
                return;
            }

            var matching = map.Values.Where(IsOfferPoolContract).Where(selector).ToList();
            if (matching.Count <= keepCount)
            {
                return;
            }

            // Order ascending (smallest Order = oldest). Protect the record that was just inserted so the caller
            // always sees their observation land even when it is technically the "oldest" record among the matching
            // set (e.g. Order==0 on a reset pool).
            matching.Sort((left, right) => left.Order.CompareTo(right.Order));

            var toRemove = matching.Count - keepCount;
            var removedGuids = new List<Guid>(toRemove);
            foreach (var candidate in matching)
            {
                if (removedGuids.Count >= toRemove) break;
                if (candidate.ContractGuid == protectedGuid) continue;
                removedGuids.Add(candidate.ContractGuid);
            }

            foreach (var evictGuid in removedGuids)
            {
                map.Remove(evictGuid);
            }

            if (removedGuids.Count > 0)
            {
                LunaLog.Debug(
                    $"[PersistentSync] Contracts offered pool evicted {removedGuids.Count} row(s) reason={reason} " +
                    $"totalOfferedAfter={map.Values.Count(IsOfferPoolContract)} protectedGuid={protectedGuid}");
            }
        }

        private static ContractSnapshotInfo NormalizeRecord(ContractSnapshotInfo incomingRecord, int nextOrder)
        {
            var normalized = ContractSnapshotInfoComparer.Clone(incomingRecord);
            normalized = CanonicalizeRecordData(normalized);
            normalized.Order = normalized.Order >= 0 ? normalized.Order : nextOrder;
            normalized.Placement = DeterminePlacement(normalized.ContractState);
            return normalized;
        }

        private static ContractSnapshotInfo CreateSnapshotInfo(ConfigNode contractNode, int order)
        {
            var guidValue = contractNode.GetValue(GuidFieldName)?.Value;
            if (!Guid.TryParse(guidValue, out var contractGuid))
            {
                return null;
            }

            // Client-produced ContractSnapshotInfo.Data is body-only (no CONTRACT wrapper). When we ingest from a
            // scenario ConfigNode, contractNode.ToString() emits the wrapping "CONTRACT\n{\n ... \n}" header. Strip
            // it so all canonical in-memory/persisted rows share the same body-only shape, otherwise mutations like
            // RewriteContractState operate at the wrong node level.
            var bodyText = StripContractWrapper(contractNode.ToString());
            var bodyBytes = Encoding.UTF8.GetBytes(bodyText);

            return CanonicalizeRecordData(new ContractSnapshotInfo
            {
                ContractGuid = contractGuid,
                ContractState = contractNode.GetValue(StateFieldName)?.Value ?? string.Empty,
                Placement = DeterminePlacement(contractNode.GetValue(StateFieldName)?.Value ?? string.Empty),
                Order = order,
                NumBytes = bodyBytes.Length,
                Data = bodyBytes
            });
        }

        private static string StripContractWrapper(string wrappedText)
        {
            if (string.IsNullOrEmpty(wrappedText))
            {
                return wrappedText ?? string.Empty;
            }

            var openBrace = wrappedText.IndexOf('{');
            if (openBrace < 0)
            {
                return wrappedText;
            }

            var header = wrappedText.Substring(0, openBrace).Trim();
            if (!string.Equals(header, ContractNodeName, StringComparison.Ordinal))
            {
                return wrappedText;
            }

            var depth = 0;
            var closeBrace = -1;
            for (var i = openBrace; i < wrappedText.Length; i++)
            {
                var c = wrappedText[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBrace = i;
                        break;
                    }
                }
            }

            if (closeBrace < 0)
            {
                return wrappedText;
            }

            var bodyRaw = wrappedText.Substring(openBrace + 1, closeBrace - openBrace - 1);
            var normalized = bodyRaw.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var sb = new StringBuilder(normalized.Length);
            foreach (var line in lines)
            {
                var stripped = line;
                if (stripped.Length > 0 && stripped[0] == '\t')
                {
                    stripped = stripped.Substring(1);
                }

                if (stripped.Length == 0)
                {
                    continue;
                }

                sb.Append(stripped);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static ContractSnapshotPlacement DeterminePlacement(string contractState)
        {
            if (string.IsNullOrWhiteSpace(contractState))
            {
                return ContractSnapshotPlacement.Current;
            }

            switch (contractState.Trim().ToLowerInvariant())
            {
                case "active":
                    return ContractSnapshotPlacement.Active;
                case "completed":
                case "deadlineexpired":
                case "failed":
                case "cancelled":
                case "declined":
                case "withdrawn":
                    return ContractSnapshotPlacement.Finished;
                default:
                    return ContractSnapshotPlacement.Current;
            }
        }

        private static ContractSnapshotInfo CanonicalizeRecordData(ContractSnapshotInfo info)
        {
            var text = Encoding.UTF8.GetString(info.Data, 0, info.NumBytes);
            var stripped = StripContractWrapper(text);
            var contractNode = new ConfigNode(stripped);
            var bodyOnly = StripContractWrapper(contractNode.ToString());
            var normalizedData = Encoding.UTF8.GetBytes(bodyOnly);
            info.ContractState = contractNode.GetValue(StateFieldName)?.Value ?? info.ContractState ?? string.Empty;
            info.NumBytes = normalizedData.Length;
            info.Data = normalizedData;
            return info;
        }

        /// <summary>Stock can offer the same career template many times during time warp (new GUID each tick).
        /// Collapse to one offered row per contract type + title so Mission Control stays sane across clients.</summary>
        private static void RemoveOlderOfferedDuplicatesOf(Dictionary<Guid, ContractSnapshotInfo> map, ContractSnapshotInfo incoming)
        {
            if (!TryBuildContractIdentityKey(incoming, out var key)) return;

            var toRemove = new List<Guid>();
            foreach (var kv in map)
            {
                if (kv.Key == incoming.ContractGuid) continue;
                if (!TryBuildContractIdentityKey(kv.Value, out var existingKey) || existingKey != key) continue;

                if (incoming.Placement == ContractSnapshotPlacement.Active)
                {
                    if (TryBuildOfferedDedupKey(kv.Value, out _))
                    {
                        toRemove.Add(kv.Key);
                    }
                    continue;
                }

                if (!TryBuildOfferedDedupKey(incoming, out _) || !TryBuildOfferedDedupKey(kv.Value, out _)) continue;
                toRemove.Add(kv.Key);
            }

            foreach (var g in toRemove) map.Remove(g);
        }

        private static bool ShouldRejectIncomingOfferedDuplicateOfActive(Dictionary<Guid, ContractSnapshotInfo> map, ContractSnapshotInfo incoming)
        {
            if (!TryBuildOfferedDedupKey(incoming, out var key)) return false;

            foreach (var kv in map)
            {
                if (kv.Key == incoming.ContractGuid) continue;
                if (kv.Value.Placement != ContractSnapshotPlacement.Active) continue;
                if (!TryBuildContractIdentityKey(kv.Value, out var existingKey) || existingKey != key) continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Defense-in-depth against a client regenerating a fresh-GUID offer for a contract the server has
        /// already recorded as <c>Completed</c>. Stock KSP's ContractGenerator should never emit such an
        /// offer (progression achievements like <c>FirstLaunch</c> gate their own re-offer via
        /// <c>MeetRequirements</c>), but it can happen when a reconnecting client runs
        /// <c>ContractSystem.RefreshContracts</c> against a transiently-cleared local
        /// <c>ContractSystem.Instance</c> (post-<c>OnAwake</c>, before the PersistentSync Contracts snapshot
        /// has applied). The client-side gate in
        /// <c>ShareContractsSystem.ReplenishStockOffersAfterPersistentSnapshotApply</c> closes that window,
        /// but this guard ensures a racing client can't permanently corrupt canonical state with a "duplicate
        /// completed contract appears in Available" row even if the client-side gate is bypassed.
        ///
        /// Only rows whose canonical state is <c>Completed</c> qualify for rejection here. Other finished
        /// states (<c>Declined</c>, <c>Cancelled</c>, <c>Failed</c>, <c>DeadlineExpired</c>, <c>Withdrawn</c>)
        /// represent outcomes where stock legitimately re-offers the same template later, so rejecting those
        /// would break normal gameplay progression.
        /// </summary>
        private static bool ShouldRejectIncomingOfferedDuplicateOfCompleted(Dictionary<Guid, ContractSnapshotInfo> map, ContractSnapshotInfo incoming)
        {
            if (!TryBuildOfferedDedupKey(incoming, out var key)) return false;

            foreach (var kv in map)
            {
                if (kv.Key == incoming.ContractGuid) continue;
                if (kv.Value.Placement != ContractSnapshotPlacement.Finished) continue;
                if (!string.Equals(kv.Value.ContractState?.Trim(), "Completed", StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryBuildContractIdentityKey(kv.Value, out var existingKey) || existingKey != key) continue;

                LunaLog.Debug(
                    $"[PersistentSync] Contracts rejected offered duplicate of completed row: " +
                    $"incomingGuid={incoming.ContractGuid} existingCompletedGuid={kv.Key} identityKey={key}");
                return true;
            }

            return false;
        }

        private static bool TryBuildContractIdentityKey(ContractSnapshotInfo info, out string key)
        {
            key = null;
            if (info == null) return false;

            try
            {
                var text = Encoding.UTF8.GetString(info.Data, 0, info.NumBytes);
                var contractNode = new ConfigNode(text);
                var type = contractNode.GetValue(TypeFieldName)?.Value?.Trim()
                           ?? TryReadContractLineValue(text, TypeFieldName);
                var rawTitle = contractNode.GetValue(LmpOfferTitleFieldName)?.Value
                               ?? TryReadContractLineValue(text, LmpOfferTitleFieldName)
                               ?? contractNode.GetValue(TitleFieldName)?.Value
                               ?? TryReadContractLineValue(text, TitleFieldName)
                               ?? TryReadContractLineValue(text, "Title")
                               ?? contractNode.GetValue("synopsis")?.Value
                               ?? TryReadContractLineValue(text, "synopsis")
                               ?? contractNode.GetValue("notes")?.Value
                               ?? TryReadContractLineValue(text, "notes")
                               ?? contractNode.GetValue("description")?.Value
                               ?? TryReadContractLineValue(text, "description");
                var title = NormalizeOfferTitleForDedupe(rawTitle);
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(title)) return false;

                key = string.Concat(type, "\u001f", title);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBuildOfferedDedupKey(ContractSnapshotInfo info, out string key)
        {
            key = null;
            if (info.Placement != ContractSnapshotPlacement.Current) return false;

            var state = (info.ContractState ?? string.Empty).Trim();
            if (!IsOfferLikeContractState(state)) return false;

            return TryBuildContractIdentityKey(info, out key);
        }

        private static bool IsOfferLikeContractState(string state)
        {
            return string.Equals(state, "Offered", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(state, "Available", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryReadContractLineValue(string configText, string key)
        {
            if (string.IsNullOrEmpty(configText) || string.IsNullOrEmpty(key)) return null;

            var prefix = key + " = ";
            foreach (var line in configText.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Length > prefix.Length ? trimmed.Substring(prefix.Length).Trim() : string.Empty;
                }
            }

            return null;
        }

        private static string NormalizeOfferTitleForDedupe(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            return Regex.Replace(title.Trim(), @"\s+", " ");
        }

        /// <summary>Typed canonical state: contracts keyed by GUID.</summary>
        public sealed class Canonical
        {
            public Canonical(Dictionary<Guid, ContractSnapshotInfo> contractsByGuid)
            {
                ContractsByGuid = contractsByGuid ?? new Dictionary<Guid, ContractSnapshotInfo>();
            }

            public Dictionary<Guid, ContractSnapshotInfo> ContractsByGuid { get; }
        }
    }
}
