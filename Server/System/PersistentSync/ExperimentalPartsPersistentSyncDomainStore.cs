using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.System;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.System.PersistentSync
{
    public class ExperimentalPartsPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ResearchAndDevelopment";
        private const string ExpPartsNodeName = "ExpParts";

        private readonly Dictionary<string, int> _partCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.ExperimentalParts;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _partCounts.Clear();

            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                var expPartsNode = scenario.GetNode(ExpPartsNodeName)?.Value;
                if (expPartsNode == null)
                {
                    return;
                }

                foreach (var value in expPartsNode.GetAllValues())
                {
                    if (int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
                    {
                        _partCounts[value.Key] = count;
                    }
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = ExperimentalPartsSnapshotPayloadSerializer.Serialize(_partCounts
                .OrderBy(value => value.Key)
                .Select(value => new ExperimentalPartSnapshotInfo { PartName = value.Key, Count = value.Value })
                .ToArray());
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
            return ApplyRecords(ExperimentalPartsSnapshotPayloadSerializer.Deserialize(data.Payload), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(ExperimentalPartsSnapshotPayloadSerializer.Deserialize(payload), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<ExperimentalPartSnapshotInfo> records, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var record in records ?? Enumerable.Empty<ExperimentalPartSnapshotInfo>())
            {
                if (record == null || string.IsNullOrEmpty(record.PartName))
                {
                    continue;
                }

                if (record.Count <= 0)
                {
                    if (_partCounts.Remove(record.PartName))
                    {
                        changed = true;
                    }

                    continue;
                }

                if (_partCounts.TryGetValue(record.PartName, out var currentCount) && currentCount == record.Count)
                {
                    continue;
                }

                _partCounts[record.PartName] = record.Count;
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

                var expPartsNode = scenario.GetNode(ExpPartsNodeName)?.Value;
                if (!_partCounts.Any())
                {
                    if (expPartsNode != null)
                    {
                        scenario.RemoveNode(expPartsNode);
                    }

                    return;
                }

                if (expPartsNode == null)
                {
                    expPartsNode = new ConfigNode(ExpPartsNodeName, scenario);
                    scenario.AddNode(expPartsNode);
                }

                foreach (var existingValue in expPartsNode.GetAllValues().ToArray())
                {
                    expPartsNode.RemoveValue(existingValue.Key);
                }

                foreach (var value in _partCounts.OrderBy(pair => pair.Key))
                {
                    expPartsNode.CreateValue(new CfgNodeValue<string, string>(value.Key, value.Value.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }
    }
}
