using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.System.PersistentSync
{
    public class ContractsPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ContractSystem";
        private const string ContractsNodeName = "CONTRACTS";
        private const string ContractNodeName = "CONTRACT";
        private const string GuidFieldName = "guid";
        private const string StateFieldName = "state";

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
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

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
            switch (contractState)
            {
                case "Active":
                    return ContractSnapshotPlacement.Active;
                case "Completed":
                case "DeadlineExpired":
                case "Failed":
                case "Cancelled":
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
    }
}
