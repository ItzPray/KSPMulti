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
    public sealed class ScienceSubjectsPersistentSyncDomainStore : ScenarioSyncDomainStore<ScienceSubjectsPersistentSyncDomainStore.Canonical>
    {
        private const string ScienceNodeName = "Science";
        private const string ScienceIdFieldName = "id";

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.ScienceSubjects;
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "ResearchAndDevelopment";

        protected override Canonical CreateEmpty()
        {
            return new Canonical(new SortedDictionary<string, ScienceSubjectSnapshotInfo>(StringComparer.Ordinal));
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var map = new SortedDictionary<string, ScienceSubjectSnapshotInfo>(StringComparer.Ordinal);
            if (scenario == null)
            {
                return new Canonical(map);
            }

            foreach (var subjectNode in scenario.GetNodes(ScienceNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var info = CreateSnapshotInfo(subjectNode);
                if (info != null)
                {
                    map[info.Id] = info;
                }
            }
            return new Canonical(map);
        }

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
        {
            var records = ScienceSubjectSnapshotPayloadSerializer.Deserialize(payload) ?? Enumerable.Empty<ScienceSubjectSnapshotInfo>();
            var next = new SortedDictionary<string, ScienceSubjectSnapshotInfo>(current.Subjects, StringComparer.Ordinal);
            foreach (var record in records)
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null) continue;
                next[normalized.Id] = normalized;
            }
            return ReduceResult<Canonical>.Accept(new Canonical(next));
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
            foreach (var existingNode in scenario.GetNodes(ScienceNodeName).Select(node => node.Value).Where(node => node != null).ToArray())
            {
                scenario.RemoveNode(existingNode);
            }

            foreach (var subject in canonical.Subjects.Values)
            {
                scenario.AddNode(new ConfigNode(Encoding.UTF8.GetString(subject.Data, 0, subject.NumBytes)) { Name = ScienceNodeName });
            }

            return scenario;
        }

        protected override byte[] SerializeSnapshot(Canonical canonical)
        {
            return ScienceSubjectSnapshotPayloadSerializer.Serialize(canonical.Subjects.Values.Select(CloneInfo).ToArray());
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Subjects.Count != b.Subjects.Count) return false;

            foreach (var kvp in a.Subjects)
            {
                if (!b.Subjects.TryGetValue(kvp.Key, out var other) || !RecordsAreEqual(kvp.Value, other))
                {
                    return false;
                }
            }
            return true;
        }

        private static ScienceSubjectSnapshotInfo CreateSnapshotInfo(ConfigNode subjectNode)
        {
            var bareNodeText = string.Join("\n", subjectNode.GetAllValues()
                .Select(value => $"{value.Key} = {value.Value}")
                .Where(line => !string.IsNullOrEmpty(line))) + "\n";
            var bareNode = new ConfigNode(bareNodeText);
            return NormalizeSnapshotInfo(new ScienceSubjectSnapshotInfo
            {
                Id = bareNode.GetValue(ScienceIdFieldName)?.Value ?? string.Empty,
                NumBytes = Encoding.UTF8.GetByteCount(bareNode.ToString()),
                Data = Encoding.UTF8.GetBytes(bareNode.ToString())
            });
        }

        private static ScienceSubjectSnapshotInfo NormalizeSnapshotInfo(ScienceSubjectSnapshotInfo subject)
        {
            if (subject == null || subject.Data == null || subject.NumBytes <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(subject.Data, 0, subject.NumBytes));
            var id = node.GetValue(ScienceIdFieldName)?.Value;
            if (string.IsNullOrEmpty(id))
            {
                id = subject.Id;
            }

            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var normalizedBytes = Encoding.UTF8.GetBytes(node.ToString());
            return new ScienceSubjectSnapshotInfo
            {
                Id = id,
                NumBytes = normalizedBytes.Length,
                Data = normalizedBytes
            };
        }

        private static bool RecordsAreEqual(ScienceSubjectSnapshotInfo left, ScienceSubjectSnapshotInfo right)
        {
            if (left == null || right == null) return left == right;
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.NumBytes), Encoding.UTF8.GetString(right.Data, 0, right.NumBytes), StringComparison.Ordinal);
        }

        private static ScienceSubjectSnapshotInfo CloneInfo(ScienceSubjectSnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new ScienceSubjectSnapshotInfo
            {
                Id = source.Id,
                NumBytes = source.NumBytes,
                Data = data
            };
        }

        /// <summary>Typed canonical state: science subjects keyed by Id (ordinal, sorted for deterministic iteration).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, ScienceSubjectSnapshotInfo> subjects)
            {
                Subjects = subjects ?? new SortedDictionary<string, ScienceSubjectSnapshotInfo>(StringComparer.Ordinal);
            }

            public SortedDictionary<string, ScienceSubjectSnapshotInfo> Subjects { get; }
        }
    }
}
