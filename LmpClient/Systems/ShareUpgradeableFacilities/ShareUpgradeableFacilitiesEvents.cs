using LmpClient.Base;
using Upgradeables;

namespace LmpClient.Systems.ShareUpgradeableFacilities
{
    public class ShareUpgradeableFacilitiesEvents : SubSystem<ShareUpgradeableFacilitiesSystem>
    {
        #region EventHandlers

        public void FacilityUpgraded(UpgradeableFacility facility, int levelEventArg)
        {
            if (System.IgnoreEvents) return;

            // GameEvents.OnKSCFacilityUpgraded: use FacilityLevel after the upgrade, not the event int
            // (OnKSCFacilityUpgrading's argument often matches the pre-upgrade value and caused server no-ops).
            var level = facility.FacilityLevel;
            LunaLog.Log($"Facility {facility.id} upgraded eventArg={levelEventArg} FacilityLevel={level} norm={facility.GetNormLevel()} (sending PersistentSync intent)");
            System.MessageSender.SendFacilityUpgradeMessage(facility.id, level, facility.GetNormLevel());
        }

        #endregion
    }
}
