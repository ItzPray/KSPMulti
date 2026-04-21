using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.System.PersistentSync
{
    public sealed class ExperimentalPartsPersistentSyncDomainStore : ScenarioSyncDomainStore<ExperimentalPartsPersistentSyncDomainStore.Canonical>
    {
        private const string ExpPartsNodeName = "ExpParts";

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.ExperimentalParts;
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "ResearchAndDevelopment";

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        protected override Canonical CreateEmpty()
        {
            return new Canonical(new SortedDictionary<string, int>(StringComparer.Ordinal));
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (scenario == null)
            {
                return new Canonical(map);
            }

            // GetNode("ExpParts") throws if multiple siblings share that name (hand-merged saves, legacy writes).
            // Merge all ExpParts blocks into one canonical map; WriteCanonical will collapse to a single node.
            var expPartsNodes = scenario.GetNodes(ExpPartsNodeName);
            if (expPartsNodes == null || expPartsNodes.Count == 0)
            {
                return new Canonical(map);
            }

            if (expPartsNodes.Count > 1)
            {
                LunaLog.Warning(
                    $"[PersistentSync] ExperimentalParts: ResearchAndDevelopment has {expPartsNodes.Count} duplicate " +
                    $"'{ExpPartsNodeName}' nodes; merging counts (max per part) and will collapse on next save.");
            }

            foreach (var wrapper in expPartsNodes)
            {
                var expPartsNode = wrapper.Value;
                if (expPartsNode == null) continue;

                foreach (var value in expPartsNode.GetAllValues())
                {
                    if (!int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
                    {
                        continue;
                    }

                    if (map.TryGetValue(value.Key, out var existing))
                    {
                        map[value.Key] = Math.Max(existing, count);
                    }
                    else
                    {
                        map[value.Key] = count;
                    }
                }
            }

            return new Canonical(map);
        }

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
        {
            var records = ExperimentalPartsSnapshotPayloadSerializer.Deserialize(payload) ?? Enumerable.Empty<ExperimentalPartSnapshotInfo>();
            var next = new SortedDictionary<string, int>(current.Counts, StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.PartName)) continue;

                if (record.Count <= 0)
                {
                    next.Remove(record.PartName);
                    continue;
                }

                next[record.PartName] = record.Count;
            }
            return ReduceResult<Canonical>.Accept(new Canonical(next));
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
            RemoveAllExpPartsNodes(scenario);

            if (canonical.Counts.Count == 0)
            {
                return scenario;
            }

            var expPartsNode = new ConfigNode(ExpPartsNodeName, scenario);
            scenario.AddNode(expPartsNode);

            foreach (var value in canonical.Counts)
            {
                expPartsNode.CreateValue(new CfgNodeValue<string, string>(value.Key, value.Value.ToString(CultureInfo.InvariantCulture)));
            }

            return scenario;
        }

        /// <summary>
        /// Removes every top-level ExpParts node. Required when duplicates exist because <see cref="ConfigNode.GetNode"/> is single-key.
        /// </summary>
        private static void RemoveAllExpPartsNodes(ConfigNode scenario)
        {
            var nodes = scenario.GetNodes(ExpPartsNodeName);
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            foreach (var node in nodes.Select(w => w.Value).Where(v => v != null).ToList())
            {
                scenario.RemoveNode(node);
            }
        }

        protected override byte[] SerializeSnapshot(Canonical canonical)
        {
            return ExperimentalPartsSnapshotPayloadSerializer.Serialize(canonical.Counts
                .Select(pair => new ExperimentalPartSnapshotInfo { PartName = pair.Key, Count = pair.Value })
                .ToArray());
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Counts.Count != b.Counts.Count) return false;

            foreach (var kvp in a.Counts)
            {
                if (!b.Counts.TryGetValue(kvp.Key, out var other) || other != kvp.Value)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Typed canonical state: experimental part counts keyed by part name (ordinal, sorted).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, int> counts)
            {
                Counts = counts ?? new SortedDictionary<string, int>(StringComparer.Ordinal);
            }

            public SortedDictionary<string, int> Counts { get; }
        }
    }
}
