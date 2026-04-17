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
    public class TechnologyPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ResearchAndDevelopment";
        private const string TechNodeName = "Tech";
        private const string TechIdFieldName = "id";
        private const string TechStateFieldName = "state";
        private const string TechCostFieldName = "cost";

        private readonly Dictionary<string, TechnologySnapshotInfo> _technologyById = new Dictionary<string, TechnologySnapshotInfo>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Technology;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _technologyById.Clear();

            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            foreach (var techNode in scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var info = CreateSnapshotInfo(techNode);
                if (info != null)
                {
                    _technologyById[info.TechId] = info;
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = TechnologySnapshotPayloadSerializer.Serialize(_technologyById.Values.OrderBy(v => v.TechId).Select(CloneInfo));
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
            var technologies = TechnologySnapshotPayloadSerializer.Deserialize(data.Payload, data.NumBytes);
            return ApplyTechnologyRecords(technologies, data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            var technologies = TechnologySnapshotPayloadSerializer.Deserialize(payload, numBytes);
            return ApplyTechnologyRecords(technologies, null);
        }

        private PersistentSyncDomainApplyResult ApplyTechnologyRecords(IEnumerable<TechnologySnapshotInfo> technologies, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var technology in technologies ?? Enumerable.Empty<TechnologySnapshotInfo>())
            {
                var normalized = NormalizeSnapshotInfo(technology);
                if (normalized == null)
                {
                    continue;
                }

                if (_technologyById.TryGetValue(normalized.TechId, out var existing) && RecordsAreEqual(existing, normalized))
                {
                    continue;
                }

                _technologyById[normalized.TechId] = normalized;
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

            foreach (var existingTechNode in scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null).ToArray())
            {
                scenario.RemoveNode(existingTechNode);
            }

            foreach (var technology in _technologyById.Values.OrderBy(value => value.TechId))
            {
                scenario.AddNode(CreateScenarioTechNode(technology));
            }
        }

        private static TechnologySnapshotInfo CreateSnapshotInfo(ConfigNode techNode)
        {
            var bareNode = new ConfigNode(BuildBareNodeText(techNode));
            return NormalizeSnapshotInfo(new TechnologySnapshotInfo
            {
                TechId = bareNode.GetValue(TechIdFieldName)?.Value ?? string.Empty,
                Data = Encoding.UTF8.GetBytes(bareNode.ToString()),
                NumBytes = Encoding.UTF8.GetByteCount(bareNode.ToString())
            });
        }

        private static TechnologySnapshotInfo NormalizeSnapshotInfo(TechnologySnapshotInfo technology)
        {
            if (technology == null || technology.Data == null || technology.NumBytes <= 0)
            {
                return null;
            }

            var node = ParseSnapshotNode(technology.Data, technology.NumBytes);
            var techId = node?.GetValue(TechIdFieldName)?.Value;
            if (string.IsNullOrEmpty(techId))
            {
                return null;
            }

            var normalizedText = BuildBareNodeText(new ConfigNode($@"{TechIdFieldName} = {techId}
{TechStateFieldName} = {node.GetValue(TechStateFieldName)?.Value}
{TechCostFieldName} = {node.GetValue(TechCostFieldName)?.Value}
"));
            var normalizedBytes = Encoding.UTF8.GetBytes(normalizedText);
            return new TechnologySnapshotInfo
            {
                TechId = techId,
                NumBytes = normalizedBytes.Length,
                Data = normalizedBytes
            };
        }

        private static ConfigNode ParseSnapshotNode(byte[] data, int numBytes)
        {
            return new ConfigNode(Encoding.UTF8.GetString(data, 0, numBytes));
        }

        private static ConfigNode CreateScenarioTechNode(TechnologySnapshotInfo technology)
        {
            return new ConfigNode(Encoding.UTF8.GetString(technology.Data, 0, technology.NumBytes)) { Name = TechNodeName };
        }

        private static string BuildBareNodeText(ConfigNode techNode)
        {
            var lines = new List<string>();
            AppendLine(lines, TechIdFieldName, techNode.GetValue(TechIdFieldName)?.Value);
            AppendLine(lines, TechStateFieldName, techNode.GetValue(TechStateFieldName)?.Value);
            AppendLine(lines, TechCostFieldName, techNode.GetValue(TechCostFieldName)?.Value);

            return string.Join("\n", lines.Where(line => !string.IsNullOrEmpty(line))) + "\n";
        }

        private static void AppendLine(ICollection<string> lines, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                lines.Add($"{key} = {value}");
            }
        }

        private static bool RecordsAreEqual(TechnologySnapshotInfo left, TechnologySnapshotInfo right)
        {
            return string.Equals(left.TechId, right.TechId, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.NumBytes), Encoding.UTF8.GetString(right.Data, 0, right.NumBytes), StringComparison.Ordinal);
        }

        private static TechnologySnapshotInfo CloneInfo(TechnologySnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new TechnologySnapshotInfo
            {
                TechId = source.TechId,
                NumBytes = source.NumBytes,
                Data = data
            };
        }
    }
}
