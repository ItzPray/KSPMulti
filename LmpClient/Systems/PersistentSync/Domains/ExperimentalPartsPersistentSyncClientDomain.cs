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
    public class ExperimentalPartsPersistentSyncClientDomain : SyncClientDomain<ExperimentalPartSnapshotInfo[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<ExperimentalPartsPersistentSyncClientDomain>();
        }

        private ExperimentalPartSnapshotInfo[] _pendingParts;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, ExperimentalPartSnapshotInfo[] payload)
        {
            _pendingParts = payload ?? new ExperimentalPartSnapshotInfo[0];
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

            ShareExperimentalPartsSystem.Singleton.StartIgnoringEvents();
            try
            {
                ShareExperimentalPartsSystem.Singleton.ReplaceExperimentalPartsStock(stock, "PersistentSyncSnapshotApply");
            }
            finally
            {
                ShareExperimentalPartsSystem.Singleton.StopIgnoringEvents();
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
