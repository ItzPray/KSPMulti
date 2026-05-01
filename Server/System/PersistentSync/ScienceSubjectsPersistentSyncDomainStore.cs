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
    public sealed class ScienceSubjectsPersistentSyncDomainStore : ScenarioSyncDomainStore<ScienceSubjectsPersistentSyncDomainStore.Canonical, ScienceSubjectSnapshotInfo[], ScienceSubjectSnapshotInfo[]>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("ScienceSubjects", 8);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("ResearchAndDevelopment")
                .UsesServerDomain<ScienceSubjectsPersistentSyncDomainStore>();
        }

        private const string ScienceNodeName = "Science";
        private const string ScienceIdFieldName = "id";

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;
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

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, ScienceSubjectSnapshotInfo[] intent, string reason, bool isServerMutation)
        {
            var next = new SortedDictionary<string, ScienceSubjectSnapshotInfo>(current.Subjects, StringComparer.Ordinal);
            foreach (var record in intent ?? Enumerable.Empty<ScienceSubjectSnapshotInfo>())
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
                scenario.AddNode(new ConfigNode(Encoding.UTF8.GetString(subject.Data, 0, subject.Data.Length)) { Name = ScienceNodeName });
            }

            return scenario;
        }

        protected override ScienceSubjectSnapshotInfo[] BuildSnapshotPayload(Canonical canonical)
        {
            return canonical.Subjects.Values.Select(CloneInfo).ToArray();
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
                Data = Encoding.UTF8.GetBytes(bareNode.ToString())
            });
        }

        private static ScienceSubjectSnapshotInfo NormalizeSnapshotInfo(ScienceSubjectSnapshotInfo subject)
        {
            if (subject == null || subject.Data == null || subject.Data.Length <= 0)
            {
                return null;
            }

            var node = new ConfigNode(Encoding.UTF8.GetString(subject.Data, 0, subject.Data.Length));
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
                Data = normalizedBytes
            };
        }

        private static bool RecordsAreEqual(ScienceSubjectSnapshotInfo left, ScienceSubjectSnapshotInfo right)
        {
            if (left == null || right == null) return left == right;
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(Encoding.UTF8.GetString(left.Data, 0, left.Data.Length), Encoding.UTF8.GetString(right.Data, 0, right.Data.Length), StringComparison.Ordinal);
        }

        private static ScienceSubjectSnapshotInfo CloneInfo(ScienceSubjectSnapshotInfo source)
        {
            var data = new byte[source.Data.Length];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.Data.Length);
            return new ScienceSubjectSnapshotInfo
            {
                Id = source.Id,
                Data = data
            };
        }

        /// <summary>Typed canonical state: science subjects keyed by Id (ordinal, sorted for deterministic iteration).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, ScienceSubjectSnapshotInfo> subjects) => Subjects = subjects ?? new SortedDictionary<string, ScienceSubjectSnapshotInfo>(StringComparer.Ordinal);

            public SortedDictionary<string, ScienceSubjectSnapshotInfo> Subjects { get; }
        }
    }
}

