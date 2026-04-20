using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using Server.Properties;
using Server.Settings.Structures;
using Server.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.System.PersistentSync
{
    public class ContractsPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ContractSystem";
        private const string ContractsNodeName = "CONTRACTS";
        private const string ContractNodeName = "CONTRACT";
        private const string GuidFieldName = "guid";
        private const string StateFieldName = "state";
        private const string TypeFieldName = "type";
        private const string TitleFieldName = "title";
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

        private readonly Dictionary<Guid, ContractSnapshotInfo> _contractsByGuid = new Dictionary<Guid, ContractSnapshotInfo>();

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Contracts;

        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _contractsByGuid.Clear();

            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                // Match PersistentSyncDomainApplicability / ScenarioSystem: any save that includes the Career bit.
                var careerContracts = (GeneralSettings.SettingsStore.GameMode & GameMode.Career) != 0;
                var needsPersist = false;

                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    if (!careerContracts)
                    {
                        return;
                    }

                    scenario = new ConfigNode(Resources.ContractSystem);
                    ScenarioStoreSystem.CurrentScenarios[ScenarioName] = scenario;
                    needsPersist = true;
                }

                if (IngestContractsFromScenario(scenario))
                {
                    needsPersist = true;
                }

                if (careerContracts && _contractsByGuid.Count == 0)
                {
                    if (TryPopulateContractsFromEmbeddedTemplate())
                    {
                        needsPersist = true;
                    }
                }

                if (needsPersist && _contractsByGuid.Count > 0)
                {
                    PersistCurrentState();
                }

                if (careerContracts)
                {
                    LunaLog.Normal(
                        $"[PersistentSync] Contracts LoadFromPersistence: gameMode={GeneralSettings.SettingsStore.GameMode} " +
                        $"contractRows={_contractsByGuid.Count} seededOrInsertedScenario={needsPersist}");
                }
            }
        }

        private bool IngestContractsFromScenario(ConfigNode scenario)
        {
            _contractsByGuid.Clear();

            var contractsNode = scenario.GetNode(ContractsNodeName)?.Value;
            if (contractsNode == null)
            {
                return false;
            }

            var nextOrder = 0;
            var rawCount = 0;
            var cleanedDuringLoad = false;
            foreach (var contractNode in contractsNode.GetNodes(ContractNodeName).Select(n => n.Value).Where(n => n != null))
            {
                var snapshotInfo = CreateSnapshotInfo(contractNode, nextOrder);
                if (snapshotInfo != null)
                {
                    rawCount++;
                    ApplyCanonicalRecord(snapshotInfo, ref nextOrder, ref cleanedDuringLoad);
                }
            }

            return cleanedDuringLoad || _contractsByGuid.Count != rawCount;
        }

        /// <summary>
        /// Career saves sometimes end up with an empty or unreadable CONTRACTS block (for example after a bad sync).
        /// Stock seeds starter offers on new games; mirror that from the embedded template so PersistentSync snapshots
        /// are never authoritative-empty while the server is in Career mode.
        /// </summary>
        private bool TryPopulateContractsFromEmbeddedTemplate()
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
            if (templateContracts == null)
            {
                return false;
            }

            var addedAny = false;
            var nextOrder = _contractsByGuid.Any() ? _contractsByGuid.Values.Max(c => c.Order) + 1 : 0;
            var cleanedSeededRows = false;
            foreach (var templateContractWrapper in templateContracts.GetNodes(ContractNodeName))
            {
                var templateContract = templateContractWrapper.Value;
                if (templateContract == null)
                {
                    continue;
                }

                var snapshotInfo = CreateSnapshotInfo(templateContract, nextOrder);
                if (snapshotInfo == null)
                {
                    continue;
                }

                if (ApplyCanonicalRecord(snapshotInfo, ref nextOrder, ref cleanedSeededRows))
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

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var orderedContracts = GetOrderedContracts().Select(ContractSnapshotInfoComparer.Clone).ToArray();
            var payload = ContractSnapshotPayloadSerializer.Serialize(orderedContracts);
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = Revision,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            if (TryDeserializeContractIntentPayload(data.Payload, data.NumBytes, out var intentPayload))
            {
                return ApplyTypedClientIntent(client, intentPayload, data.ClientKnownRevision);
            }

            if (!ClientOwnsProducerAuthority(client))
            {
                return RejectedApplyResult();
            }

            var payload = ContractSnapshotPayloadSerializer.DeserializeEnvelope(data.Payload, data.NumBytes);
            return payload.Mode == ContractSnapshotPayloadMode.FullReplace
                ? ApplyFullReplace(payload.Contracts, data.ClientKnownRevision)
                : ApplyRecords(payload.Contracts, data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            var deserialized = ContractSnapshotPayloadSerializer.DeserializeEnvelope(payload, numBytes);
            return deserialized.Mode == ContractSnapshotPayloadMode.FullReplace
                ? ApplyFullReplace(deserialized.Contracts, null)
                : ApplyRecords(deserialized.Contracts, null);
        }

        private PersistentSyncDomainApplyResult ApplyTypedClientIntent(ClientStructure client, ContractIntentPayload intentPayload, long? clientKnownRevision)
        {
            switch (intentPayload.Kind)
            {
                case ContractIntentPayloadKind.AcceptContract:
                    return ApplyAcceptContractCommand(intentPayload.ContractGuid, clientKnownRevision);
                case ContractIntentPayloadKind.DeclineContract:
                    return ApplyDeclineContractCommand(intentPayload.ContractGuid, clientKnownRevision);
                case ContractIntentPayloadKind.CancelContract:
                    return ApplyCancelContractCommand(intentPayload.ContractGuid, clientKnownRevision);
                case ContractIntentPayloadKind.RequestOfferGeneration:
                    return ApplyOfferGenerationRequest(clientKnownRevision);
                case ContractIntentPayloadKind.OfferObserved:
                    return ApplyProducerObservedOffer(client, intentPayload, clientKnownRevision);
                case ContractIntentPayloadKind.ParameterProgressObserved:
                    return ApplyProducerObservedActiveMutation(client, intentPayload, clientKnownRevision);
                case ContractIntentPayloadKind.ContractCompletedObserved:
                    return ApplyProducerObservedFinishedMutation(client, intentPayload, clientKnownRevision);
                case ContractIntentPayloadKind.ContractFailedObserved:
                    return ApplyProducerObservedFinishedMutation(client, intentPayload, clientKnownRevision);
                case ContractIntentPayloadKind.FullReconcile:
                    if (!ClientOwnsProducerAuthority(client))
                    {
                        return RejectedApplyResult();
                    }

                    return ApplyFullReplace(intentPayload.Contracts, clientKnownRevision);
                default:
                    return RejectedApplyResult();
            }
        }

        private PersistentSyncDomainApplyResult ApplyAcceptContractCommand(Guid contractGuid, long? clientKnownRevision)
        {
            if (!_contractsByGuid.TryGetValue(contractGuid, out var existing) || !IsOfferPoolContract(existing))
            {
                return RejectedApplyResult();
            }

            return ApplySingleCanonicalMutation(RewriteContractState(existing, "Active", ContractSnapshotPlacement.Active), clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplyDeclineContractCommand(Guid contractGuid, long? clientKnownRevision)
        {
            if (!_contractsByGuid.TryGetValue(contractGuid, out var existing) || !IsOfferPoolContract(existing))
            {
                return RejectedApplyResult();
            }

            return ApplySingleCanonicalMutation(RewriteContractState(existing, "Declined", ContractSnapshotPlacement.Finished), clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplyCancelContractCommand(Guid contractGuid, long? clientKnownRevision)
        {
            if (!_contractsByGuid.TryGetValue(contractGuid, out var existing) || !IsActiveContract(existing))
            {
                return RejectedApplyResult();
            }

            return ApplySingleCanonicalMutation(RewriteContractState(existing, "Cancelled", ContractSnapshotPlacement.Finished), clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplyOfferGenerationRequest(long? clientKnownRevision)
        {
            // Canonical snapshot only changes when someone actually mints; this intent is a signal. When the canonical
            // offer pool is empty we route the snapshot to the current producer (contract lock owner) so their
            // ContractsPersistentSyncClientDomain.FlushPendingState runs ReplenishStockOffersAfterPersistentSnapshotApply
            // and mints offers back to the server as OfferObserved proposals. Origin client is re-notified only when
            // its known revision diverges from canonical, to avoid a feedback echo when state already matches.
            var offerPoolEmpty = !HasOfferPoolContracts();
            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = false,
                ReplyToOriginClient = clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                ReplyToProducerClient = offerPoolEmpty,
                Snapshot = GetCurrentSnapshot()
            };
        }

        private PersistentSyncDomainApplyResult ApplyProducerObservedOffer(ClientStructure client, ContractIntentPayload intentPayload, long? clientKnownRevision)
        {
            if (!ClientOwnsProducerAuthority(client))
            {
                return RejectedApplyResult();
            }

            if (!TryGetSingleIntentContract(intentPayload, out var incomingContract) || !IsOfferPoolContract(incomingContract))
            {
                return RejectedApplyResult();
            }

            return ApplyRecords(new[] { incomingContract }, clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplyProducerObservedActiveMutation(ClientStructure client, ContractIntentPayload intentPayload, long? clientKnownRevision)
        {
            if (!ClientOwnsProducerAuthority(client))
            {
                return RejectedApplyResult();
            }

            if (!TryGetSingleIntentContract(intentPayload, out var incomingContract) ||
                !_contractsByGuid.TryGetValue(incomingContract.ContractGuid, out var existing) ||
                !IsActiveContract(existing))
            {
                return RejectedApplyResult();
            }

            return ApplyRecords(new[] { incomingContract }, clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplyProducerObservedFinishedMutation(ClientStructure client, ContractIntentPayload intentPayload, long? clientKnownRevision)
        {
            if (!ClientOwnsProducerAuthority(client))
            {
                return RejectedApplyResult();
            }

            if (!TryGetSingleIntentContract(intentPayload, out var incomingContract) ||
                !_contractsByGuid.TryGetValue(incomingContract.ContractGuid, out var existing) ||
                !IsActiveContract(existing))
            {
                return RejectedApplyResult();
            }

            return ApplyRecords(new[] { incomingContract }, clientKnownRevision);
        }

        private PersistentSyncDomainApplyResult ApplySingleCanonicalMutation(ContractSnapshotInfo mutatedContract, long? clientKnownRevision)
        {
            var changed = false;
            var nextOrder = _contractsByGuid.Any() ? _contractsByGuid.Values.Max(c => c.Order) + 1 : 0;
            ApplyCanonicalRecord(mutatedContract, ref nextOrder, ref changed);

            if (changed)
            {
                Revision++;
                PersistCurrentState();
            }

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = changed,
                ReplyToOriginClient = !changed && clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                Snapshot = GetCurrentSnapshot()
            };
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

        private static PersistentSyncDomainApplyResult RejectedApplyResult()
        {
            return new PersistentSyncDomainApplyResult
            {
                Accepted = false,
                Changed = false,
                ReplyToOriginClient = false,
                Snapshot = null
            };
        }

        private static bool ClientOwnsProducerAuthority(ClientStructure client)
        {
            return client != null && LockSystem.LockQuery.ContractLockBelongsToPlayer(client.PlayerName);
        }

        private bool HasOfferPoolContracts()
        {
            return _contractsByGuid.Values.Any(IsOfferPoolContract);
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

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<ContractSnapshotInfo> incomingRecords, long? clientKnownRevision)
        {
            var changed = false;
            var nextOrder = _contractsByGuid.Any() ? _contractsByGuid.Values.Max(c => c.Order) + 1 : 0;

            foreach (var incomingRecord in incomingRecords ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                ApplyCanonicalRecord(incomingRecord, ref nextOrder, ref changed);
            }

            if (changed)
            {
                Revision++;
                PersistCurrentState();
            }

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = changed,
                ReplyToOriginClient = !changed && clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                Snapshot = GetCurrentSnapshot()
            };
        }

        private PersistentSyncDomainApplyResult ApplyFullReplace(IEnumerable<ContractSnapshotInfo> incomingRecords, long? clientKnownRevision)
        {
            var previous = _contractsByGuid.ToDictionary(kv => kv.Key, kv => ContractSnapshotInfoComparer.Clone(kv.Value));

            _contractsByGuid.Clear();
            var nextOrder = 0;
            var ignoredChanged = false;
            var ignoredCleanup = false;
            foreach (var incomingRecord in incomingRecords ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                ApplyCanonicalRecord(incomingRecord, ref nextOrder, ref ignoredChanged, ref ignoredCleanup);
            }

            var changed = !AreCanonicalSetsEquivalent(previous, _contractsByGuid);
            if (changed)
            {
                Revision++;
                PersistCurrentState();
            }

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = changed,
                ReplyToOriginClient = !changed && clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                Snapshot = GetCurrentSnapshot()
            };
        }

        private void PersistCurrentState()
        {
            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                // LunaConfigNode graph edits for CONTRACTS have proven easy to get wrong (wrong layer removed,
                // leaving stale CONTRACT rows in the serialized scenario). Rewrite the CONTRACTS { ... } region in
                // the full scenario text so persistence always matches the canonical in-memory contract set while
                // preserving sibling blocks (WEIGHTS, etc.).
                var scenarioText = scenario.ToString();
                if (!TryReplaceContractsSectionInScenarioText(scenarioText, GetOrderedContracts(), out var rewritten))
                {
                    return;
                }

                ScenarioStoreSystem.CurrentScenarios[ScenarioName] = new ConfigNode(rewritten);
            }
        }

        private static bool TryReplaceContractsSectionInScenarioText(
            string scenarioText,
            IEnumerable<ContractSnapshotInfo> orderedContracts,
            out string rewritten)
        {
            rewritten = null;
            if (string.IsNullOrEmpty(scenarioText))
            {
                return false;
            }

            var patterns = new[] { "CONTRACTS\r\n{", "CONTRACTS\n{" };
            var blockStart = -1;
            var openBrace = -1;
            foreach (var p in patterns)
            {
                var idx = scenarioText.IndexOf(p, StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }

                blockStart = idx;
                openBrace = idx + p.Length - 1;
                break;
            }

            if (blockStart < 0 || openBrace < 0 || openBrace >= scenarioText.Length || scenarioText[openBrace] != '{')
            {
                return false;
            }

            var depth = 0;
            var closeBrace = -1;
            for (var i = openBrace; i < scenarioText.Length; i++)
            {
                var c = scenarioText[i];
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
                return false;
            }

            var tailStart = closeBrace + 1;
            while (tailStart < scenarioText.Length &&
                   (scenarioText[tailStart] == '\r' || scenarioText[tailStart] == '\n'))
            {
                tailStart++;
            }

            var sb = new StringBuilder(scenarioText.Length + 64);
            sb.Append(scenarioText, 0, blockStart);
            sb.AppendLine("CONTRACTS");
            sb.AppendLine("{");
            foreach (var contract in orderedContracts ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                sb.AppendLine("CONTRACT");
                sb.AppendLine("{");
                sb.AppendLine(IndentContractData(Encoding.UTF8.GetString(contract.Data, 0, contract.NumBytes)));
                sb.AppendLine("}");
            }

            sb.AppendLine("}");
            if (tailStart < scenarioText.Length)
            {
                sb.Append(scenarioText, tailStart, scenarioText.Length - tailStart);
            }

            rewritten = sb.ToString();
            return true;
        }

        private IEnumerable<ContractSnapshotInfo> GetOrderedContracts()
        {
            return _contractsByGuid.Values.OrderBy(c => c.Order).ThenBy(c => c.ContractGuid);
        }

        private static bool AreCanonicalSetsEquivalent(
            IReadOnlyDictionary<Guid, ContractSnapshotInfo> left,
            IReadOnlyDictionary<Guid, ContractSnapshotInfo> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            foreach (var kv in left)
            {
                if (!right.TryGetValue(kv.Key, out var other))
                {
                    return false;
                }

                if (kv.Value.Order != other.Order || !ContractSnapshotInfoComparer.AreEquivalent(kv.Value, other))
                {
                    return false;
                }
            }

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

        private bool ApplyCanonicalRecord(ContractSnapshotInfo incomingRecord, ref int nextOrder, ref bool changed)
        {
            var ignoredCleanup = false;
            return ApplyCanonicalRecord(incomingRecord, ref nextOrder, ref changed, ref ignoredCleanup);
        }

        private bool ApplyCanonicalRecord(ContractSnapshotInfo incomingRecord, ref int nextOrder, ref bool changed, ref bool cleanedDuringLoad)
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

            if (ShouldRejectIncomingOfferedDuplicateOfActive(normalizedRecord))
            {
                cleanedDuringLoad = true;
                return false;
            }

            if (RemoveOlderOfferedDuplicatesOf(normalizedRecord))
            {
                changed = true;
                cleanedDuringLoad = true;
            }

            if (_contractsByGuid.TryGetValue(normalizedRecord.ContractGuid, out var existingRecord))
            {
                normalizedRecord.Order = existingRecord.Order;
                if (!ContractSnapshotInfoComparer.AreEquivalent(existingRecord, normalizedRecord))
                {
                    _contractsByGuid[normalizedRecord.ContractGuid] = normalizedRecord;
                    changed = true;
                    cleanedDuringLoad = true;
                }

                return false;
            }

            _contractsByGuid[normalizedRecord.ContractGuid] = normalizedRecord;
            changed = true;
            return true;
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
            // RewriteContractState operate at the wrong node level (they'd touch the root, not the inner CONTRACT).
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

            // Only strip if the pre-brace header is "CONTRACT" (plus whitespace). Body-only text starts with
            // "key = value" and has no leading "CONTRACT\n" header.
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
                // LunaConfigNode indents children with tabs; strip one level so the body is flat (matches
                // client-produced data shape).
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
            // info.Data can arrive either wrapped ("CONTRACT { ... }") from scenario ingestion or unwrapped (body
            // only) from client-sent proposals. Canonicalize to body-only so RewriteContractState and diff/equality
            // logic always operate at the same node level.
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

        private static string IndentContractData(string data)
        {
            var lines = data.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Where(line => line.Length > 0).Select(line => "    " + line));
        }

        /// <summary>
        /// Stock can offer the same career template many times during time warp (new GUID each tick). Collapse to one
        /// offered row per contract type + title so Mission Control stays sane across clients.
        /// </summary>
        private bool RemoveOlderOfferedDuplicatesOf(ContractSnapshotInfo incoming)
        {
            if (!TryBuildContractIdentityKey(incoming, out var key))
            {
                return false;
            }

            var toRemove = new List<Guid>();
            foreach (var kv in _contractsByGuid)
            {
                if (kv.Key == incoming.ContractGuid)
                {
                    continue;
                }

                if (!TryBuildContractIdentityKey(kv.Value, out var existingKey) || existingKey != key)
                {
                    continue;
                }

                if (incoming.Placement == ContractSnapshotPlacement.Active)
                {
                    if (TryBuildOfferedDedupKey(kv.Value, out _))
                    {
                        toRemove.Add(kv.Key);
                    }

                    continue;
                }

                if (!TryBuildOfferedDedupKey(incoming, out _) || !TryBuildOfferedDedupKey(kv.Value, out _))
                {
                    continue;
                }

                toRemove.Add(kv.Key);
            }

            if (toRemove.Count == 0)
            {
                return false;
            }

            foreach (var g in toRemove)
            {
                _contractsByGuid.Remove(g);
            }

            return true;
        }

        private bool ShouldRejectIncomingOfferedDuplicateOfActive(ContractSnapshotInfo incoming)
        {
            if (!TryBuildOfferedDedupKey(incoming, out var key))
            {
                return false;
            }

            foreach (var kv in _contractsByGuid)
            {
                if (kv.Key == incoming.ContractGuid)
                {
                    continue;
                }

                if (kv.Value.Placement != ContractSnapshotPlacement.Active)
                {
                    continue;
                }

                if (!TryBuildContractIdentityKey(kv.Value, out var existingKey) || existingKey != key)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryBuildContractIdentityKey(ContractSnapshotInfo info, out string key)
        {
            key = null;
            if (info == null)
            {
                return false;
            }

            try
            {
                var text = Encoding.UTF8.GetString(info.Data, 0, info.NumBytes);
                var contractNode = new ConfigNode(text);
                var type = contractNode.GetValue(TypeFieldName)?.Value?.Trim()
                           ?? TryReadContractLineValue(text, TypeFieldName);
                // Serialized CONTRACT nodes sometimes omit `title` at the root; fall back to fields stock still writes.
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
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(title))
                {
                    return false;
                }

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
            if (info.Placement != ContractSnapshotPlacement.Current)
            {
                return false;
            }

            var state = (info.ContractState ?? string.Empty).Trim();
            if (!IsOfferLikeContractState(state))
            {
                return false;
            }

            return TryBuildContractIdentityKey(info, out key);
        }

        private static bool IsOfferLikeContractState(string state)
        {
            return string.Equals(state, "Offered", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(state, "Available", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryReadContractLineValue(string configText, string key)
        {
            if (string.IsNullOrEmpty(configText) || string.IsNullOrEmpty(key))
            {
                return null;
            }

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
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            return Regex.Replace(title.Trim(), @"\s+", " ");
        }
    }
}
