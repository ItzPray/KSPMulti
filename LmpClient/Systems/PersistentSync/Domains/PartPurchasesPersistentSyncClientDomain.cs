using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    [PersistentSyncStockScenario("ResearchAndDevelopment")]
    public class PartPurchasesPersistentSyncClientDomain : SyncClientDomain<PartPurchaseSnapshotInfo[]>
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

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, PartPurchaseSnapshotInfo[] payload)
        {
            _pendingPurchases = (payload ?? new PartPurchaseSnapshotInfo[0])
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

            SharePurchasePartsSystem.Singleton.StartIgnoringEvents();
            try
            {
                foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
                {
                    var techState = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                    if (techState == null)
                    {
                        continue;
                    }

                    // Sparse snapshot: only techs the server tracks as purchased are present. Do not clear
                    // partsPurchased for omitted techs; Available+empty is normalized afterward by
                    // EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded (legacy LMP: full tech ownership).
                    if (!_pendingPurchases.TryGetValue(tech.techID, out var purchase))
                    {
                        continue;
                    }

                    techState.partsPurchased = (purchase.PartNames ?? new string[0]).Select(ResolvePart).Where(part => part != null).Distinct().ToList();
                    ResearchAndDevelopment.Instance.SetTechState(tech.techID, techState);
                }

                // R&D UI refresh is coalesced with Technology flush (same reconciler pass); see ShareTechnologySystem.SchedulePersistentSyncRnDUiCoalescedRefresh.
                ShareTechnologySystem.Singleton.SchedulePersistentSyncRnDUiCoalescedRefresh(false);

                TechnologyPersistentSyncClientDomain.SyncRnDTechTreeFromResearchInstance();
                TechnologyPersistentSyncClientDomain.EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded("PartPurchasesFlush");
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                SharePurchasePartsSystem.Singleton.StopIgnoringEvents();
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
