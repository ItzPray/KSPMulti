using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.System;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.System.PersistentSync
{
    public class PartPurchasesPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ResearchAndDevelopment";
        private const string TechNodeName = "Tech";
        private const string TechIdFieldName = "id";
        private const string TechPartFieldName = "part";

        private readonly Dictionary<string, HashSet<string>> _partNamesByTechId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.PartPurchases;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _partNamesByTechId.Clear();

            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            foreach (var techNode in scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var techId = techNode.GetValue(TechIdFieldName)?.Value;
                if (string.IsNullOrEmpty(techId))
                {
                    continue;
                }

                var parts = new HashSet<string>(
                    techNode.GetValues(TechPartFieldName).Select(value => value.Value).Where(value => !string.IsNullOrEmpty(value)),
                    StringComparer.Ordinal);
                // Omit techs with no persisted purchases: including them produced all-empty PartNames entries in
                // snapshots and the client overwrote every tech's partsPurchased with an empty list.
                if (parts.Count > 0)
                {
                    _partNamesByTechId[techId] = parts;
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = PartPurchasesSnapshotPayloadSerializer.Serialize(_partNamesByTechId
                .Where(value => value.Value != null && value.Value.Count > 0)
                .OrderBy(value => value.Key)
                .Select(value => new PartPurchaseSnapshotInfo
                {
                    TechId = value.Key,
                    PartNames = value.Value.OrderBy(partName => partName).ToArray()
                })
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
            return ApplyRecords(PartPurchasesSnapshotPayloadSerializer.Deserialize(data.Payload), data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            return ApplyRecords(PartPurchasesSnapshotPayloadSerializer.Deserialize(payload), null);
        }

        private PersistentSyncDomainApplyResult ApplyRecords(IEnumerable<PartPurchaseSnapshotInfo> records, long? clientKnownRevision)
        {
            var changed = false;
            foreach (var record in records ?? Enumerable.Empty<PartPurchaseSnapshotInfo>())
            {
                if (record == null || string.IsNullOrEmpty(record.TechId))
                {
                    continue;
                }

                var normalizedParts = new HashSet<string>((record.PartNames ?? new string[0]).Where(value => !string.IsNullOrEmpty(value)), StringComparer.Ordinal);
                if (_partNamesByTechId.TryGetValue(record.TechId, out var existing) && existing.SetEquals(normalizedParts))
                {
                    continue;
                }

                _partNamesByTechId[record.TechId] = normalizedParts;
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

            foreach (var techNode in scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var techId = techNode.GetValue(TechIdFieldName)?.Value;
                if (string.IsNullOrEmpty(techId) || !_partNamesByTechId.TryGetValue(techId, out var parts))
                {
                    // Do not strip existing part= lines for techs we are not tracking (was wiping stock parts).
                    continue;
                }

                while (techNode.GetValues(TechPartFieldName).Any())
                {
                    techNode.RemoveValue(TechPartFieldName);
                }

                foreach (var partName in parts.OrderBy(value => value))
                {
                    techNode.CreateValue(new CfgNodeValue<string, string>(TechPartFieldName, partName));
                }
            }
        }
    }
}
