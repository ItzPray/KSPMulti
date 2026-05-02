using LmpClient.Events;
using LmpClient.Systems.ShareExperimentalParts;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    public class ExperimentalPartsPersistentSyncClientDomain : SyncClientDomain<ExperimentalPartsPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<ExperimentalPartsPersistentSyncClientDomain>();
        }

        private ExperimentalPartSnapshotInfo[] _pendingParts;

        protected override void OnDomainEnabled()
        {
            ExperimentalPartEvent.onExperimentalPartRemoved.Add(OnExperimentalPartRemoved);
            ExperimentalPartEvent.onExperimentalPartAdded.Add(OnExperimentalPartAdded);
        }

        protected override void OnDomainDisabled()
        {
            ExperimentalPartEvent.onExperimentalPartRemoved.Remove(OnExperimentalPartRemoved);
            ExperimentalPartEvent.onExperimentalPartAdded.Remove(OnExperimentalPartAdded);
        }

        private void OnExperimentalPartRemoved(AvailablePart part, int count)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            LunaLog.Log($"Relaying experimental part removed: part: {part.name} count: {count}");
            SendExperimentalPartIntent(part.name, count);
        }

        private void OnExperimentalPartAdded(AvailablePart part, int count)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            LunaLog.Log($"Relaying experimental part added: part: {part.name} count: {count}");
            SendExperimentalPartIntent(part.name, count);
        }

        private void SendExperimentalPartIntent(string partName, int count)
        {
            SendLocalPayload(
                new ExperimentalPartsPayload
                {
                    Items = new[]
                    {
                        new ExperimentalPartSnapshotInfo
                        {
                            PartName = partName,
                            Count = count
                        }
                    }
                },
                $"ExperimentalPart:{partName}");
        }

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, ExperimentalPartsPayload payload)
        {
            _pendingParts = payload?.Items ?? Array.Empty<ExperimentalPartSnapshotInfo>();
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingParts == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var stock = new Dictionary<AvailablePart, int>();
            try
            {
                foreach (var snapshot in _pendingParts.Where(value => value != null && !string.IsNullOrEmpty(value.PartName) && value.Count > 0))
                {
                    var part = ResolvePart(snapshot.PartName);
                    if (part == null)
                    {
                        return PersistentSyncApplyOutcome.Rejected;
                    }

                    stock[part] = snapshot.Count;
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            using (PersistentSyncDomainSuppressionScope.Begin(
                PersistentSyncEventSuppressorRegistry.Resolve(PersistentSyncDomainNames.ExperimentalParts),
                restoreOldValueOnDispose: false))
            {
                ShareExperimentalPartsSystem.Singleton.ReplaceExperimentalPartsStock(stock, "PersistentSyncSnapshotApply");
            }

            _pendingParts = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static AvailablePart ResolvePart(string partName)
        {
            if (string.IsNullOrEmpty(partName))
            {
                return null;
            }

            var partInfo = PartLoader.getPartInfoByName(partName);
            if (partInfo != null)
            {
                return partInfo;
            }

            return PartLoader.getPartInfoByName(partName.Replace('_', '.').Trim());
        }
    }
}
