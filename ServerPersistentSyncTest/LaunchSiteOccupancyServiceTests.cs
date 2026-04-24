using System;
using LmpCommon.Locks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System.LaunchSite;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class LaunchSiteOccupancyServiceTests
    {
        [TestMethod]
        public void CanLockChangeOccupancy_only_accepts_vessel_owner_locks()
        {
            var vesselId = Guid.NewGuid();

            Assert.IsTrue(LaunchSiteOccupancyService.CanLockChangeOccupancy(
                new LockDefinition(LockType.Control, "player", vesselId)));
            Assert.IsTrue(LaunchSiteOccupancyService.CanLockChangeOccupancy(
                new LockDefinition(LockType.Update, "player", vesselId)));

            Assert.IsFalse(LaunchSiteOccupancyService.CanLockChangeOccupancy(
                new LockDefinition(LockType.UnloadedUpdate, "player", vesselId)));
            Assert.IsFalse(LaunchSiteOccupancyService.CanLockChangeOccupancy(
                new LockDefinition(LockType.Control, "player", Guid.Empty)));
            Assert.IsFalse(LaunchSiteOccupancyService.CanLockChangeOccupancy(
                new LockDefinition(LockType.Kerbal, "player", "Jebediah Kerman")));
            Assert.IsFalse(LaunchSiteOccupancyService.CanLockChangeOccupancy(null));
        }
    }
}
