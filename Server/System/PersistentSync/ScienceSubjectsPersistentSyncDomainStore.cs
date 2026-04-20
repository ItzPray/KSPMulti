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
    public class ScienceSubjectsPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ResearchAndDevelopment";
        private const string ScienceNodeName = "Science";
        private const string ScienceIdFieldName = "id";

        private readonly Dictionary<string, ScienceSubjectSnapshotInfo> _scienceSubjectById = new Dictionary<string, ScienceSubjectSnapshotInfo>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.ScienceSubjects;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _scienceSubjectById.Clear();

            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                foreach (var subjectNode in scenario.GetNodes(ScienceNodeName).Select(node => node.Value).Where(node => node != null))
                {
                    var info = CreateSnapshotInfo(subjectNode);
                    if (info != null)
                    {
                        _scienceSubjectById[info.Id] = info;
                    }
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = ScienceSubjectSnapshotPayloadSerializer.Serialize(_scienceSubjectById.Values.OrderBy(value => value.Id).Select(CloneInfo).ToArray());
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
            return ApplyRecords(ScienceSubjectSnapshotPayloadSerializer.Deserialize(data.Payload), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(ScienceSubjectSnapshotPayloadSerializer.Deserialize(payload), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<ScienceSubjectSnapshotInfo> records, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var record in records ?? Enumerable.Empty<ScienceSubjectSnapshotInfo>())
            {
                var normalized = NormalizeSnapshotInfo(record);
                if (normalized == null)
                {
                    continue;
                }

                if (_scienceSubjectById.TryGetValue(normalized.Id, out var existing) && RecordsAreEqual(existing, normalized))
                {
                    continue;
                }

                _scienceSubjectById[normalized.Id] = normalized;
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

                foreach (var existingNode in scenario.GetNodes(ScienceNodeName).Select(node => node.Value).Where(node => node != null).ToArray())
                {
                    scenario.RemoveNode(existingNode);
                }

                foreach (var subject in _scienceSubjectById.Values.OrderBy(value => value.Id))
                {
                    scenario.AddNode(new ConfigNode(Encoding.UTF8.GetString(subject.Data, 0, subject.NumBytes)) { Name = ScienceNodeName });
                }
            }
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
    }
}
