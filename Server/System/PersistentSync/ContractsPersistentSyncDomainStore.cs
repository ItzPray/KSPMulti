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
    /// <item><description>Producer-only observations (OfferObserved/ParameterProgressObserved/Completed/Failed/FullReconcile)
    /// — only the current contract lock owner.</description></item>
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

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Contracts;

        /// <summary>
        /// Floor policy advertised to clients and the registry's default path. Real gating happens in
        /// <see cref="AuthorizeIntent"/> because Contracts has mixed per-intent authority. Advertising
        /// <see cref="PersistentAuthorityPolicy.AnyClientIntent"/> keeps player commands responsive; producer-only
        /// intents are rejected in <see cref="AuthorizeIntent"/> before reaching the reducer.
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
                    case ContractIntentPayloadKind.OfferObserved:
                    case ContractIntentPayloadKind.ParameterProgressObserved:
                    case ContractIntentPayloadKind.ContractCompletedObserved:
                    case ContractIntentPayloadKind.ContractFailedObserved:
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
                    return ReduceCommandStateRewrite(current, intentPayload.ContractGuid, "Active", ContractSnapshotPlacement.Active, requireOfferPool: true);
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

            return ReduceSingleRecord(current, incoming);
        }

        private static ReduceResult<Canonical> ReduceSingleRecord(Canonical current, ContractSnapshotInfo incoming)
        {
            var nextMap = current.ContractsByGuid.ToDictionary(kv => kv.Key, kv => ContractSnapshotInfoComparer.Clone(kv.Value));
            var nextOrder = nextMap.Any() ? nextMap.Values.Max(c => c.Order) + 1 : 0;
            ApplyCanonicalRecord(nextMap, incoming, ref nextOrder);
            return ReduceResult<Canonical>.Accept(new Canonical(nextMap));
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

        private static ReduceResult<Canonical> ReduceFullReplace(Canonical current, IEnumerable<ContractSnapshotInfo> incoming)
        {
            var nextMap = new Dictionary<Guid, ContractSnapshotInfo>();
            var nextOrder = 0;
            foreach (var rec in incoming ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                ApplyCanonicalRecord(nextMap, rec, ref nextOrder);
            }

            return ReduceResult<Canonical>.Accept(new Canonical(nextMap));
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
            return true;
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
