using System;
using System.Collections.Generic;

namespace Server.System.LaunchSite
{
    /// <summary>
    /// Tracks last server-side activity per vessel for launch-pad lease expiry.
    /// </summary>
    public static class LaunchPadVesselActivityTracker
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Guid, DateTime> LastUtc = new Dictionary<Guid, DateTime>();

        public static void Touch(Guid vesselId)
        {
            lock (Gate)
                LastUtc[vesselId] = DateTime.UtcNow;
        }

        public static void Remove(Guid vesselId)
        {
            lock (Gate)
                LastUtc.Remove(vesselId);
        }

        public static bool IsStale(Guid vesselId, int leaseTimeoutSeconds, DateTime nowUtc)
        {
            if (leaseTimeoutSeconds <= 0)
                return false;

            lock (Gate)
            {
                if (!LastUtc.TryGetValue(vesselId, out var t))
                    return false;
                return nowUtc - t > TimeSpan.FromSeconds(leaseTimeoutSeconds);
            }
        }
    }
}
