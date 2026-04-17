using Contracts;
using LmpClient.Systems.KscScene;
using LmpClient.Systems.ShareUpgradeableFacilities;
using LmpCommon.PersistentSync;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Upgradeables;

namespace LmpClient.Systems.PersistentSync
{
    public class UpgradeableFacilitiesPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private Dictionary<string, int> _pendingFacilityLevels;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.UpgradeableFacilities;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingFacilityLevels = UpgradeableFacilitiesSnapshotPayloadSerializer.Deserialize(snapshot.Payload, snapshot.NumBytes);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingFacilityLevels == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var facilitiesById = Object.FindObjectsOfType<UpgradeableFacility>().ToDictionary(f => f.id);
            if (_pendingFacilityLevels.Keys.Any(facilityId => !facilitiesById.ContainsKey(facilityId)))
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            ShareUpgradeableFacilitiesSystem.Singleton.StartIgnoringEvents();
            try
            {
                foreach (var facilityLevel in _pendingFacilityLevels.OrderBy(kvp => kvp.Key))
                {
                    facilitiesById[facilityLevel.Key].SetLevel(facilityLevel.Value);
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ShareUpgradeableFacilitiesSystem.Singleton.StopIgnoringEvents();
            }

            RefreshFacilityAdapters();
            _pendingFacilityLevels = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static void RefreshFacilityAdapters()
        {
            KscSceneSystem.Singleton.RefreshTrackingStationVessels();
            GameEvents.Contract.onContractsListChanged.Fire();
        }
    }
}
