using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.System.PersistentSync
{
    public class StrategyPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "StrategySystem";
        private const string StrategiesNodeName = "STRATEGIES";
        private const string StrategyNodeName = "STRATEGY";
        private const string StrategyNameFieldName = "name";

        private readonly Dictionary<string, StrategySnapshotInfo> _strategiesByName = new Dictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Strategy;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _strategiesByName.Clear();

            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                var strategiesNode = scenario.GetNode(StrategiesNodeName)?.Value;
                if (strategiesNode == null)
                {
                    return;
                }

                foreach (var strategyNode in strategiesNode.GetNodes(StrategyNodeName).Select(node => node.Value).Where(node => node != null))
                {
                    var info = CreateSnapshotInfo(strategyNode);
                    if (info != null)
                    {
                        _strategiesByName[info.Name] = info;
                    }
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = StrategySnapshotPayloadSerializer.Serialize(_strategiesByName.Values.OrderBy(value => value.Name).Select(CloneInfo).ToArray());
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
            return ApplyRecords(StrategySnapshotPayloadSerializer.Deserialize(data.Payload), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(StrategySnapshotPayloadSerializer.Deserialize(payload), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<StrategySnapshotInfo> records, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var record in records ?? Enumerable.Empty<StrategySnapshotInfo>())
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null)
                {
                    continue;
                }

                if (_strategiesByName.TryGetValue(normalized.Name, out var existing) && RecordsAreEqual(existing, normalized))
                {
                    continue;
                }

                _strategiesByName[normalized.Name] = normalized;
                changed = true;
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

                var strategiesNode = scenario.GetNode(StrategiesNodeName)?.Value;
                if (strategiesNode == null)
                {
                    strategiesNode = new ConfigNode(StrategiesNodeName, scenario);
                    scenario.AddNode(strategiesNode);
                }

                foreach (var existingNode in strategiesNode.GetNodes(StrategyNodeName).Select(node => node.Value).Where(node => node != null).ToArray())
                {
                    strategiesNode.RemoveNode(existingNode);
                }

                foreach (var strategy in _strategiesByName.Values.OrderBy(value => value.Name))
                {
                    strategiesNode.AddNode(CreateScenarioStrategyNode(strategy));
                }
            }
        }

        private static StrategySnapshotInfo CreateSnapshotInfo(ConfigNode strategyNode)
        {
            var bareNodeText = BuildBareNodeText(strategyNode);
            var bareNode = new ConfigNode(bareNodeText);
            return NormalizeSnapshotInfo(new StrategySnapshotInfo
            {
                Name = bareNode.GetValue(StrategyNameFieldName)?.Value ?? string.Empty,
                NumBytes = Encoding.UTF8.GetByteCount(bareNode.ToString()),
                Data = Encoding.UTF8.GetBytes(bareNode.ToString())
            });
        }

        private static StrategySnapshotInfo NormalizeSnapshotInfo(StrategySnapshotInfo strategy)
        {
            if (strategy == null || strategy.Data == null || strategy.NumBytes <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(strategy.Data, 0, strategy.NumBytes));
            var name = node.GetValue(StrategyNameFieldName)?.Value;
            if (string.IsNullOrEmpty(name))
            {
                name = strategy.Name;
            }

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var normalizedBytes = Encoding.UTF8.GetBytes(node.ToString());
            return new StrategySnapshotInfo
            {
                Name = name,
                NumBytes = normalizedBytes.Length,
                Data = normalizedBytes
            };
        }

        private static ConfigNode CreateScenarioStrategyNode(StrategySnapshotInfo strategy)
        {
            return new ConfigNode(Encoding.UTF8.GetString(strategy.Data, 0, strategy.NumBytes)) { Name = StrategyNodeName };
        }

        private static string BuildBareNodeText(ConfigNode strategyNode)
        {
            return string.Join("\n", strategyNode.GetAllValues()
                .Select(value => $"{value.Key} = {value.Value}")
                .Where(line => !string.IsNullOrEmpty(line))) + "\n";
        }

        private static bool RecordsAreEqual(StrategySnapshotInfo left, StrategySnapshotInfo right)
        {
            return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.NumBytes), Encoding.UTF8.GetString(right.Data, 0, right.NumBytes), StringComparison.Ordinal);
        }

        private static StrategySnapshotInfo CloneInfo(StrategySnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new StrategySnapshotInfo
            {
                Name = source.Name,
                NumBytes = source.NumBytes,
                Data = data
            };
        }
    }
}
