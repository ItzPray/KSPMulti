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
    public class AchievementsPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ProgressTracking";
        private const string ProgressNodeName = "Progress";

        private readonly Dictionary<string, AchievementSnapshotInfo> _achievementsById = new Dictionary<string, AchievementSnapshotInfo>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Achievements;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _achievementsById.Clear();

            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            var progressNodeText = ExtractNamedNodeText(scenario.ToString(), ProgressNodeName);
            if (string.IsNullOrEmpty(progressNodeText))
            {
                return;
            }

            foreach (var childNodeText in SplitTopLevelNodes(progressNodeText))
            {
                var info = CreateSnapshotInfo(new ConfigNode(childNodeText));
                if (info != null)
                {
                    _achievementsById[info.Id] = info;
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = AchievementSnapshotPayloadSerializer.Serialize(_achievementsById.Values.OrderBy(value => value.Id).Select(CloneInfo).ToArray());
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
            return ApplyRecords(AchievementSnapshotPayloadSerializer.Deserialize(data.Payload), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(AchievementSnapshotPayloadSerializer.Deserialize(payload), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<AchievementSnapshotInfo> records, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var record in records ?? Enumerable.Empty<AchievementSnapshotInfo>())
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null)
                {
                    continue;
                }

                if (_achievementsById.TryGetValue(normalized.Id, out var existing) && RecordsAreEqual(existing, normalized))
                {
                    continue;
                }

                _achievementsById[normalized.Id] = normalized;
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
            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            // Universe saves can accumulate multiple top-level nodes named "Progress". GetNode() requires a
            // unique key and throws MixedCollection GetSingle — remove every Progress wrapper, then add one.
            foreach (var existing in scenario.GetNodes(ProgressNodeName).Select(n => n.Value).Where(n => n != null).ToArray())
            {
                scenario.RemoveNode(existing);
            }

            var progressNode = new ConfigNode(BuildProgressNodeText(_achievementsById.Values.OrderBy(value => value.Id)));
            scenario.AddNode(progressNode);
        }

        private static AchievementSnapshotInfo CreateSnapshotInfo(ConfigNode node)
        {
            return NormalizeSnapshotInfo(new AchievementSnapshotInfo
            {
                Id = node.Name ?? string.Empty,
                NumBytes = Encoding.UTF8.GetByteCount(node.ToString()),
                Data = Encoding.UTF8.GetBytes(node.ToString())
            });
        }

        private static AchievementSnapshotInfo NormalizeSnapshotInfo(AchievementSnapshotInfo achievement)
        {
            if (achievement == null || achievement.Data == null || achievement.NumBytes <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(achievement.Data, 0, achievement.NumBytes));
            var id = !string.IsNullOrEmpty(node.Name) ? node.Name : achievement.Id;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var normalizedBytes = Encoding.UTF8.GetBytes(node.ToString());
            return new AchievementSnapshotInfo
            {
                Id = id,
                NumBytes = normalizedBytes.Length,
                Data = normalizedBytes
            };
        }

        private static bool RecordsAreEqual(AchievementSnapshotInfo left, AchievementSnapshotInfo right)
        {
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.NumBytes), Encoding.UTF8.GetString(right.Data, 0, right.NumBytes), StringComparison.Ordinal);
        }

        private static AchievementSnapshotInfo CloneInfo(AchievementSnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new AchievementSnapshotInfo
            {
                Id = source.Id,
                NumBytes = source.NumBytes,
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
                builder.Append(IndentBlock(Encoding.UTF8.GetString(achievement.Data, 0, achievement.NumBytes), "    "));
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
    }
}
