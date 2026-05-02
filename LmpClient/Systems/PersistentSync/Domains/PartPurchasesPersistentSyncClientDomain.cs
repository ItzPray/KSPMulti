using LmpCommon.PersistentSync.Payloads.PartPurchases;
using System;
using System.Collections.Generic;
using System.Linq;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class PartPurchasesPersistentSyncClientDomain : SyncClientDomain<PartPurchasesPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .ProducerRequiresPartPurchaseMechanism()
                .ProjectsFrom(PersistentSyncDomainNames.Technology)
                .UsesClientDomain<PartPurchasesPersistentSyncClientDomain>();
        }

        private Dictionary<string, PartPurchaseSnapshotInfo> _pendingPurchases;

        /// <summary>
        /// Last deserialized server purchased-parts map. Mirrors the reassert pattern used by
        /// TechnologyPersistentSyncClientDomain: when KSP re-hydrates Research and Development state the
        /// purchased parts ride along with techStates, so we must re-stage both together.
        /// </summary>
        private Dictionary<string, PartPurchaseSnapshotInfo> _authoritativePurchases;

        protected override void OnDomainEnabled()
        {
            GameEvents.OnPartPurchased.Add(OnPartPurchased);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnPartPurchased.Remove(OnPartPurchased);
        }

        private void OnPartPurchased(AvailablePart part)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            var techState = ResearchAndDevelopment.Instance?.GetTechState(part.TechRequired);
            if (techState == null)
            {
                return;
            }

            LunaLog.Log($"Relaying part purchased on tech: {techState.techID}; part: {part.name}");
            SendLocalPayload(
                new PartPurchasesPayload
                {
                    Items = new[]
                    {
                        new PartPurchaseSnapshotInfo
                        {
                            TechId = techState.techID,
                            PartNames = techState.partsPurchased.Where(p => p != null).Select(p => p.name).Distinct().ToArray()
                        }
                    }
                },
                $"PartPurchase:{techState.techID}:{part.name}");
        }

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, PartPurchasesPayload payload)
        {
            _pendingPurchases = (payload?.Items ?? Array.Empty<PartPurchaseSnapshotInfo>())
                .Where(value => value != null && !string.IsNullOrEmpty(value.TechId))
                .ToDictionary(value => value.TechId, value => value, StringComparer.Ordinal);
            _authoritativePurchases = new Dictionary<string, PartPurchaseSnapshotInfo>(_pendingPurchases, StringComparer.Ordinal);
        }

        public bool TryStageReassertFromLastServerSnapshot()
        {
            if (_authoritativePurchases == null || _authoritativePurchases.Count == 0)
            {
                return false;
            }

            _pendingPurchases = new Dictionary<string, PartPurchaseSnapshotInfo>(_authoritativePurchases, StringComparer.Ordinal);
            return true;
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingPurchases == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            using (PersistentSyncDomainSuppressionScope.Begin(
                PersistentSyncEventSuppressorRegistry.Resolve(PersistentSyncDomainNames.PartPurchases),
                restoreOldValueOnDispose: false))
            {
                try
                {
                    foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
                    {
                        var techState = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                        if (techState == null)
                        {
                            continue;
                        }

                        if (!_pendingPurchases.TryGetValue(tech.techID, out var purchase))
                        {
                            continue;
                        }

                        techState.partsPurchased = (purchase.PartNames ?? new string[0]).Select(ResolvePart).Where(part => part != null).Distinct().ToList();
                        ResearchAndDevelopment.Instance.SetTechState(tech.techID, techState);
                    }

                    // Stage 3: move Technology producer + R&amp;D UI ownership; keep coalesced refresh here.
                    ShareTechnologySystem.Singleton.SchedulePersistentSyncRnDUiCoalescedRefresh(false);

                    TechnologyPersistentSyncClientDomain.SyncRnDTechTreeFromResearchInstance();
                    TechnologyPersistentSyncClientDomain.EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded("PartPurchasesFlush");
                }
                catch
                {
                    return PersistentSyncApplyOutcome.Rejected;
                }
            }

            _pendingPurchases = null;
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
