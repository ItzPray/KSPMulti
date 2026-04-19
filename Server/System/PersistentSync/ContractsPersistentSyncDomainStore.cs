using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
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

        // The current contract lock owner is the only client allowed to produce canonical contract state for the server.
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.LockOwnerIntent;

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

                IngestContractsFromScenario(scenario);

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

        private void IngestContractsFromScenario(ConfigNode scenario)
        {
            _contractsByGuid.Clear();

            var contractsNode = scenario.GetNode(ContractsNodeName)?.Value;
            if (contractsNode == null)
            {
                return;
            }

            var order = 0;
            foreach (var contractNode in contractsNode.GetNodes(ContractNodeName).Select(n => n.Value).Where(n => n != null))
            {
                var snapshotInfo = CreateSnapshotInfo(contractNode, order++);
                if (snapshotInfo != null)
                {
                    _contractsByGuid[snapshotInfo.ContractGuid] = snapshotInfo;
                }
            }
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
            var order = 0;
            foreach (var templateContractWrapper in templateContracts.GetNodes(ContractNodeName))
            {
                var templateContract = templateContractWrapper.Value;
                if (templateContract == null)
                {
                    continue;
                }

                var snapshotInfo = CreateSnapshotInfo(templateContract, order++);
                if (snapshotInfo == null)
                {
                    continue;
                }

                _contractsByGuid[snapshotInfo.ContractGuid] = snapshotInfo;
                addedAny = true;
            }

            if (addedAny)
            {
                LunaLog.Warning("[PersistentSync] Contracts: universe ContractSystem had no readable offers; seeded starter contracts from embedded template.");
            }

            return addedAny;
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var orderedContracts = GetOrderedContracts().Select(CloneInfo).ToArray();
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

        public PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data)
        {
            return ApplyRecords(ContractSnapshotPayloadSerializer.Deserialize(data.Payload, data.NumBytes), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(ContractSnapshotPayloadSerializer.Deserialize(payload, numBytes), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<ContractSnapshotInfo> incomingRecords, long? clientKnownRevision)
        {
            var changed = false;
            var nextOrder = _contractsByGuid.Any() ? _contractsByGuid.Values.Max(c => c.Order) + 1 : 0;

            foreach (var incomingRecord in incomingRecords ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                if (incomingRecord == null || incomingRecord.ContractGuid == Guid.Empty)
                {
                    continue;
                }

                var normalizedRecord = NormalizeRecord(incomingRecord, nextOrder);
                if (normalizedRecord.Order == nextOrder)
                {
                    nextOrder++;
                }

                if (ShouldRejectIncomingOfferedDuplicateOfActive(normalizedRecord))
                {
                    continue;
                }

                if (RemoveOlderOfferedDuplicatesOf(normalizedRecord))
                {
                    changed = true;
                }

                if (_contractsByGuid.TryGetValue(normalizedRecord.ContractGuid, out var existingRecord))
                {
                    normalizedRecord.Order = existingRecord.Order;
                    if (!RecordsAreEqual(existingRecord, normalizedRecord))
                    {
                        _contractsByGuid[normalizedRecord.ContractGuid] = normalizedRecord;
                        changed = true;
                    }
                }
                else
                {
                    _contractsByGuid[normalizedRecord.ContractGuid] = normalizedRecord;
                    changed = true;
                }
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

        private void PersistCurrentState()
        {
            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                var contractsNode = scenario.GetNode(ContractsNodeName)?.Value;
                if (contractsNode == null)
                {
                    contractsNode = new ConfigNode(ContractsNodeName, scenario);
                    scenario.AddNode(contractsNode);
                }

                foreach (var existingContract in contractsNode.GetNodes(ContractNodeName).Select(n => n.Value).Where(n => n != null).ToArray())
                {
                    contractsNode.RemoveNode(existingContract);
                }

                foreach (var contract in GetOrderedContracts())
                {
                    contractsNode.AddNode(DeserializeContractNode(contract));
                }
            }
        }

        private IEnumerable<ContractSnapshotInfo> GetOrderedContracts()
        {
            return _contractsByGuid.Values.OrderBy(c => c.Order).ThenBy(c => c.ContractGuid);
        }

        private static ContractSnapshotInfo NormalizeRecord(ContractSnapshotInfo incomingRecord, int nextOrder)
        {
            var normalized = CloneInfo(incomingRecord);
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

            return CanonicalizeRecordData(new ContractSnapshotInfo
            {
                ContractGuid = contractGuid,
                ContractState = contractNode.GetValue(StateFieldName)?.Value ?? string.Empty,
                Placement = DeterminePlacement(contractNode.GetValue(StateFieldName)?.Value ?? string.Empty),
                Order = order,
                NumBytes = Encoding.UTF8.GetByteCount(contractNode.ToString()),
                Data = Encoding.UTF8.GetBytes(contractNode.ToString())
            });
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

        private static ConfigNode DeserializeContractNode(ContractSnapshotInfo info)
        {
            return ParseBareContractData(Encoding.UTF8.GetString(info.Data, 0, info.NumBytes));
        }

        private static ContractSnapshotInfo CanonicalizeRecordData(ContractSnapshotInfo info)
        {
            var contractNode = new ConfigNode(Encoding.UTF8.GetString(info.Data, 0, info.NumBytes));
            var normalizedData = Encoding.UTF8.GetBytes(contractNode.ToString());
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

        private static ConfigNode ParseBareContractData(string data)
        {
            var wrappedNode = $"CONTRACT\n{{\n{IndentContractData(data)}\n}}";
            return new ConfigNode(wrappedNode);
        }

        private static bool RecordsAreEqual(ContractSnapshotInfo left, ContractSnapshotInfo right)
        {
            return left.ContractGuid == right.ContractGuid &&
                   left.ContractState == right.ContractState &&
                   left.Placement == right.Placement &&
                   NormalizeContractText(left) == NormalizeContractText(right);
        }

        private static string NormalizeContractText(ContractSnapshotInfo info)
        {
            return new string(Encoding.UTF8.GetString(info.Data, 0, info.NumBytes)
                .Where(c => !char.IsWhiteSpace(c))
                .ToArray());
        }

        private static ContractSnapshotInfo CloneInfo(ContractSnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new ContractSnapshotInfo
            {
                ContractGuid = source.ContractGuid,
                ContractState = source.ContractState,
                Placement = source.Placement,
                Order = source.Order,
                NumBytes = source.NumBytes,
                Data = data
            };
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
