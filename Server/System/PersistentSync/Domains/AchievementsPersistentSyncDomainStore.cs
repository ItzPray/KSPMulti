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
    [PersistentSyncStockScenario("ProgressTracking")]
    public sealed class AchievementsPersistentSyncDomainStore : SyncDomainStore<AchievementSnapshotInfo[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<AchievementsPersistentSyncDomainStore>();
        }

        private const string ProgressNodeName = "Progress";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override AchievementSnapshotInfo[] CreateDefaultPayload()
        {
            return BuildSnapshotPayload(CreateEmptyCanonical());
        }

        protected override AchievementSnapshotInfo[] LoadPayload(ConfigNode scenario, bool createdFromScratch)
        {
            return BuildSnapshotPayload(LoadCanonicalState(scenario, createdFromScratch));
        }

        protected override ReduceResult<AchievementSnapshotInfo[]> ReducePayload(ClientStructure client, AchievementSnapshotInfo[] current, AchievementSnapshotInfo[] incoming, string reason, bool isServerMutation)
        {
            var reduced = ReducePayloadState(ToCanonical(current), incoming, reason, isServerMutation);
            return reduced == null || !reduced.Accepted
                ? ReduceResult<AchievementSnapshotInfo[]>.Reject()
                : ReduceResult<AchievementSnapshotInfo[]>.Accept(BuildSnapshotPayload(reduced.NextState), reduced.ForceReplyToOriginClient, reduced.ReplyToProducerClient);
        }

        protected override ConfigNode WritePayload(ConfigNode scenario, AchievementSnapshotInfo[] payload)
        {
            return WriteCanonicalState(scenario, ToCanonical(payload));
        }

        protected override bool PayloadsAreEqual(AchievementSnapshotInfo[] left, AchievementSnapshotInfo[] right)
        {
            return AreEquivalent(ToCanonical(left), ToCanonical(right));
        }

        private static Canonical CreateEmptyCanonical()
        {
            return new Canonical(new SortedDictionary<string, AchievementSnapshotInfo>(StringComparer.Ordinal));
        }

        private static Canonical LoadCanonicalState(ConfigNode scenario, bool createdFromScratch)
        {
            var map = new SortedDictionary<string, AchievementSnapshotInfo>(StringComparer.Ordinal);
            if (scenario == null)
            {
                return new Canonical(map);
            }

            var progressNodeText = ExtractNamedNodeText(scenario.ToString(), ProgressNodeName);
            if (string.IsNullOrEmpty(progressNodeText))
            {
                return new Canonical(map);
            }

            foreach (var childNodeText in SplitTopLevelNodes(progressNodeText))
            {
                var info = CreateSnapshotInfo(new ConfigNode(childNodeText));
                if (info != null)
                {
                    map[info.Id] = info;
                }
            }
            return new Canonical(map);
        }

        private static ReduceResult<Canonical> ReducePayloadState(Canonical current, AchievementSnapshotInfo[] intent, string reason, bool isServerMutation)
        {
            var next = new SortedDictionary<string, AchievementSnapshotInfo>(current.Achievements, StringComparer.Ordinal);
            foreach (var record in intent ?? Enumerable.Empty<AchievementSnapshotInfo>())
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null) continue;
                next[normalized.Id] = normalized;
            }
            return ReduceResult<Canonical>.Accept(new Canonical(next));
        }

        private static ConfigNode WriteCanonicalState(ConfigNode scenario, Canonical canonical)
        {
            // Universe saves can accumulate multiple top-level nodes named "Progress". GetNode() requires a
            // unique key and throws MixedCollection GetSingle. Remove every Progress wrapper, then add one.
            foreach (var existing in scenario.GetNodes(ProgressNodeName).Select(n => n.Value).Where(n => n != null).ToArray())
            {
                scenario.RemoveNode(existing);
            }

            var progressNode = new ConfigNode(BuildProgressNodeText(canonical.Achievements.Values));
            scenario.AddNode(progressNode);
            return scenario;
        }

        private static AchievementSnapshotInfo[] BuildSnapshotPayload(Canonical canonical)
        {
            return canonical.Achievements.Values.Select(CloneInfo).ToArray();
        }

        private static bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Achievements.Count != b.Achievements.Count) return false;

            foreach (var kvp in a.Achievements)
            {
                if (!b.Achievements.TryGetValue(kvp.Key, out var other) || !RecordsAreEqual(kvp.Value, other))
                {
                    return false;
                }
            }
            return true;
        }

        private static AchievementSnapshotInfo CreateSnapshotInfo(ConfigNode node)
        {
            return NormalizeSnapshotInfo(new AchievementSnapshotInfo
            {
                Id = node.Name ?? string.Empty,
                Data = Encoding.UTF8.GetBytes(node.ToString())
            });
        }

        private static AchievementSnapshotInfo NormalizeSnapshotInfo(AchievementSnapshotInfo achievement)
        {
            if (achievement == null || achievement.Data == null || achievement.Data.Length <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(achievement.Data, 0, achievement.Data.Length));
            var id = !string.IsNullOrEmpty(node.Name) ? node.Name : achievement.Id;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var normalizedBytes = Encoding.UTF8.GetBytes(node.ToString());
            return new AchievementSnapshotInfo
            {
                Id = id,
                Data = normalizedBytes
            };
        }

        private static bool RecordsAreEqual(AchievementSnapshotInfo left, AchievementSnapshotInfo right)
        {
            if (left == null || right == null) return left == right;
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.Data.Length), Encoding.UTF8.GetString(right.Data, 0, right.Data.Length), StringComparison.Ordinal);
        }

        private static AchievementSnapshotInfo CloneInfo(AchievementSnapshotInfo source)
        {
            var data = new byte[source.Data.Length];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.Data.Length);
            return new AchievementSnapshotInfo
            {
                Id = source.Id,
                Data = data
            };
        }

        private static string BuildProgressNodeText(IEnumerable<AchievementSnapshotInfo> achievements)
        {
            var builder = new StringBuilder();
            builder.AppendLine(ProgressNodeName);
            builder.AppendLine("{");
            foreach (var achievement in achievements)
            {
                builder.Append(IndentBlock(Encoding.UTF8.GetString(achievement.Data, 0, achievement.Data.Length), "    "));
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private static IEnumerable<string> SplitTopLevelNodes(string progressNodeText)
        {
            var lines = progressNodeText.Replace("\r\n", "\n").Split('\n');
            var childBuilder = new StringBuilder();
            var depth = 0;
            var sawWrapperName = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (!sawWrapperName && trimmed == ProgressNodeName)
                {
                    sawWrapperName = true;
                    continue;
                }

                if (sawWrapperName && childBuilder.Length == 0 && depth == 0 && trimmed == "{")
                {
                    sawWrapperName = false;
                    continue;
                }

                childBuilder.AppendLine(line);
                if (trimmed == "{")
                {
                    depth++;
                }
                else if (trimmed.EndsWith("{"))
                {
                    depth++;
                }

                if (trimmed == "}")
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return childBuilder.ToString();
                        childBuilder.Clear();
                    }
                }
            }
        }

        private static string IndentBlock(string value, string indent)
        {
            var lines = value.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Where(line => line.Length > 0).Select(line => indent + line)) + "\n";
        }

        private static string ExtractNamedNodeText(string rootText, string nodeName)
        {
            var lines = rootText.Replace("\r\n", "\n").Split('\n');
            var builder = new StringBuilder();
            var capture = false;
            var depth = 0;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();
                if (!capture)
                {
                    if (trimmed == nodeName)
                    {
                        capture = true;
                        builder.AppendLine(line);
                    }

                    continue;
                }

                builder.AppendLine(line);
                if (trimmed == "{")
                {
                    depth++;
                }
                else if (trimmed.EndsWith("{"))
                {
                    depth++;
                }
                else if (trimmed == "}")
                {
                    depth--;
                    if (depth == 0)
                    {
                        return builder.ToString();
                    }
                }
            }

            return string.Empty;
        }

        private static Canonical ToCanonical(AchievementSnapshotInfo[] payload)
        {
            var map = new SortedDictionary<string, AchievementSnapshotInfo>(StringComparer.Ordinal);
            foreach (var record in payload ?? new AchievementSnapshotInfo[0])
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized != null)
                {
                    map[normalized.Id] = normalized;
                }
            }

            return new Canonical(map);
        }

        /// <summary>Typed canonical state: achievements keyed by Id (ordinal, sorted for deterministic iteration).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, AchievementSnapshotInfo> achievements) => Achievements = achievements ?? new SortedDictionary<string, AchievementSnapshotInfo>(StringComparer.Ordinal);

            public SortedDictionary<string, AchievementSnapshotInfo> Achievements { get; }
        }
    }
}

