using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.System.PersistentSync
{
    public sealed class StrategyPersistentSyncDomainStore : ScenarioSyncDomainStore<StrategyPersistentSyncDomainStore.Canonical>
    {
        private const string StrategiesNodeName = "STRATEGIES";
        private const string StrategyNodeName = "STRATEGY";
        private const string StrategyNameFieldName = "name";

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Strategy;
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "StrategySystem";

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        protected override Canonical CreateEmpty()
        {
            return new Canonical(new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal));
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var map = new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal);
            var strategiesNode = scenario?.GetNode(StrategiesNodeName)?.Value;
            if (strategiesNode == null)
            {
                return new Canonical(map);
            }

            foreach (var strategyNode in strategiesNode.GetNodes(StrategyNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var info = CreateSnapshotInfo(strategyNode);
                if (info != null)
                {
                    map[info.Name] = info;
                }
            }

            return new Canonical(map);
        }

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
        {
            var records = StrategySnapshotPayloadSerializer.Deserialize(payload) ?? Enumerable.Empty<StrategySnapshotInfo>();
            var next = new SortedDictionary<string, StrategySnapshotInfo>(current.Strategies, StringComparer.Ordinal);
            foreach (var record in records)
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null)
                {
                    continue;
                }

                next[normalized.Name] = normalized;
            }

            return ReduceResult<Canonical>.Accept(new Canonical(next));
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
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

            foreach (var strategy in canonical.Strategies.Values)
            {
                strategiesNode.AddNode(CreateScenarioStrategyNode(strategy));
            }

            return scenario;
        }

        protected override byte[] SerializeSnapshot(Canonical canonical)
        {
            return StrategySnapshotPayloadSerializer.Serialize(canonical.Strategies.Values.Select(CloneInfo).ToArray());
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Strategies.Count != b.Strategies.Count) return false;

            foreach (var kvp in a.Strategies)
            {
                if (!b.Strategies.TryGetValue(kvp.Key, out var other) || !RecordsAreEqual(kvp.Value, other))
                {
                    return false;
                }
            }
            return true;
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
            if (left == null || right == null) return left == right;
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

        /// <summary>Typed canonical state: strategies keyed by Name (ordinal, sorted for deterministic iteration).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, StrategySnapshotInfo> strategies)
            {
                Strategies = strategies ?? new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal);
            }

            public SortedDictionary<string, StrategySnapshotInfo> Strategies { get; }
        }
    }
}
