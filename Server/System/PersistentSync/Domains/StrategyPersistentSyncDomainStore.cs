using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
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
    [PersistentSyncStockScenario("StrategySystem")]
    public sealed class StrategyPersistentSyncDomainStore : SyncDomainStore<StrategySnapshotInfo[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<StrategyPersistentSyncDomainStore>();
        }

        private const string StrategiesNodeName = "STRATEGIES";
        private const string StrategyNodeName = "STRATEGY";
        private const string StrategyNameFieldName = "name";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override StrategySnapshotInfo[] CreateDefaultPayload()
        {
            return BuildSnapshotPayload(CreateEmptyCanonical());
        }

        protected override StrategySnapshotInfo[] LoadPayload(ConfigNode scenario, bool createdFromScratch)
        {
            return BuildSnapshotPayload(LoadCanonicalState(scenario, createdFromScratch));
        }

        protected override ReduceResult<StrategySnapshotInfo[]> ReducePayload(ClientStructure client, StrategySnapshotInfo[] current, StrategySnapshotInfo[] incoming, string reason, bool isServerMutation)
        {
            var reduced = ReducePayloadState(ToCanonical(current), incoming, reason, isServerMutation);
            return reduced == null || !reduced.Accepted
                ? ReduceResult<StrategySnapshotInfo[]>.Reject()
                : ReduceResult<StrategySnapshotInfo[]>.Accept(BuildSnapshotPayload(reduced.NextState), reduced.ForceReplyToOriginClient, reduced.ReplyToProducerClient);
        }

        protected override ConfigNode WritePayload(ConfigNode scenario, StrategySnapshotInfo[] payload)
        {
            return WriteCanonicalState(scenario, ToCanonical(payload));
        }

        protected override bool PayloadsAreEqual(StrategySnapshotInfo[] left, StrategySnapshotInfo[] right)
        {
            return AreEquivalent(ToCanonical(left), ToCanonical(right));
        }

        private static Canonical CreateEmptyCanonical()
        {
            return new Canonical(new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal));
        }

        private static Canonical LoadCanonicalState(ConfigNode scenario, bool createdFromScratch)
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

        private static ReduceResult<Canonical> ReducePayloadState(Canonical current, StrategySnapshotInfo[] intent, string reason, bool isServerMutation)
        {
            var next = new SortedDictionary<string, StrategySnapshotInfo>(current.Strategies, StringComparer.Ordinal);
            foreach (var record in intent ?? Enumerable.Empty<StrategySnapshotInfo>())
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

        private static ConfigNode WriteCanonicalState(ConfigNode scenario, Canonical canonical)
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

        private static StrategySnapshotInfo[] BuildSnapshotPayload(Canonical canonical)
        {
            return canonical.Strategies.Values.Select(CloneInfo).ToArray();
        }

        private static bool AreEquivalent(Canonical a, Canonical b)
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
                Data = Encoding.UTF8.GetBytes(bareNode.ToString())
            });
        }

        private static StrategySnapshotInfo NormalizeSnapshotInfo(StrategySnapshotInfo strategy)
        {
            if (strategy == null || strategy.Data == null || strategy.Data.Length <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(strategy.Data, 0, strategy.Data.Length));
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
                Data = normalizedBytes
            };
        }

        private static ConfigNode CreateScenarioStrategyNode(StrategySnapshotInfo strategy)
        {
            return new ConfigNode(Encoding.UTF8.GetString(strategy.Data, 0, strategy.Data.Length)) { Name = StrategyNodeName };
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
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.Data.Length), Encoding.UTF8.GetString(right.Data, 0, right.Data.Length), StringComparison.Ordinal);
        }

        private static StrategySnapshotInfo CloneInfo(StrategySnapshotInfo source)
        {
            var data = new byte[source.Data.Length];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.Data.Length);
            return new StrategySnapshotInfo
            {
                Name = source.Name,
                Data = data
            };
        }

        private static Canonical ToCanonical(StrategySnapshotInfo[] payload)
        {
            var map = new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal);
            foreach (var record in payload ?? new StrategySnapshotInfo[0])
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized != null)
                {
                    map[normalized.Name] = normalized;
                }
            }

            return new Canonical(map);
        }

        /// <summary>Typed canonical state: strategies keyed by Name (ordinal, sorted for deterministic iteration).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, StrategySnapshotInfo> strategies) => Strategies = strategies ?? new SortedDictionary<string, StrategySnapshotInfo>(StringComparer.Ordinal);

            public SortedDictionary<string, StrategySnapshotInfo> Strategies { get; }
        }
    }
}

