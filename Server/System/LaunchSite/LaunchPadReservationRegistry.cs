using System;
using System.Collections.Generic;
using System.Linq;
using LmpCommon.Message.Data.LaunchPad;
using Server.Settings.Structures;

namespace Server.System.LaunchSite
{
    /// <summary>
    /// Short-lived server-side pad reservations (Plan B) before a PRELAUNCH vessel proto exists.
    /// </summary>
    public static class LaunchPadReservationRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (string PlayerName, DateTime ExpiryUtc)> Reservations =
            new Dictionary<string, (string, DateTime)>(StringComparer.Ordinal);

        public static void ClearExpired()
        {
            var now = DateTime.UtcNow;
            lock (Gate)
            {
                var stale = Reservations.Where(kv => kv.Value.ExpiryUtc <= now).Select(kv => kv.Key).ToList();
                foreach (var k in stale)
                    Reservations.Remove(k);
            }
        }

        public static bool TryReserve(string playerName, string siteKey, out string denyReason)
        {
            denyReason = null;
            if (string.IsNullOrEmpty(siteKey))
            {
                denyReason = "Invalid site key.";
                return false;
            }

            var duration = TimeSpan.FromSeconds(Math.Max(30, GeneralSettings.SettingsStore.LaunchPadReservationDurationSeconds));
            var now = DateTime.UtcNow;
            lock (Gate)
            {
                ClearExpiredUnlocked(now);
                if (Reservations.TryGetValue(siteKey, out var existing))
                {
                    if (string.Equals(existing.PlayerName, playerName, StringComparison.Ordinal))
                    {
                        Reservations[siteKey] = (playerName, now + duration);
                        return true;
                    }

                    if (existing.ExpiryUtc > now)
                    {
                        denyReason = $"Launch site '{siteKey}' is reserved by {existing.PlayerName}.";
                        return false;
                    }
                }

                Reservations[siteKey] = (playerName, now + duration);
                return true;
            }
        }

        public static void ReleaseForPlayer(string playerName)
        {
            lock (Gate)
            {
                var keys = Reservations.Where(kv => string.Equals(kv.Value.PlayerName, playerName, StringComparison.Ordinal))
                    .Select(kv => kv.Key).ToList();
                foreach (var k in keys)
                    Reservations.Remove(k);
            }
        }

        public static void ReleaseSite(string siteKey)
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(siteKey))
                    Reservations.Remove(siteKey);
            }
        }

        /// <summary>Called when a vessel proto is accepted for this site by this player — reservation is no longer needed.</summary>
        public static void OnProtoAcceptedForSite(string playerName, string siteKey)
        {
            if (string.IsNullOrEmpty(siteKey)) return;
            lock (Gate)
            {
                if (Reservations.TryGetValue(siteKey, out var v) && string.Equals(v.PlayerName, playerName, StringComparison.Ordinal))
                    Reservations.Remove(siteKey);
            }
        }

        public static bool IsBlockedByOtherReservation(string siteKey, string playerName, DateTime nowUtc)
        {
            lock (Gate)
            {
                ClearExpiredUnlocked(nowUtc);
                if (!Reservations.TryGetValue(siteKey, out var v))
                    return false;
                if (v.ExpiryUtc <= nowUtc)
                    return false;
                return !string.Equals(v.PlayerName, playerName, StringComparison.Ordinal);
            }
        }

        public static void AppendReservationEntries(ICollection<LaunchPadOccupancyEntry> target, DateTime nowUtc)
        {
            lock (Gate)
            {
                ClearExpiredUnlocked(nowUtc);
                foreach (var kv in Reservations)
                {
                    if (kv.Value.ExpiryUtc <= nowUtc) continue;
                    target.Add(new LaunchPadOccupancyEntry
                    {
                        SiteKey = kv.Key,
                        PlayerName = kv.Value.PlayerName,
                        VesselId = Guid.Empty
                    });
                }
            }
        }

        private static void ClearExpiredUnlocked(DateTime nowUtc)
        {
            var stale = Reservations.Where(kv => kv.Value.ExpiryUtc <= nowUtc).Select(kv => kv.Key).ToList();
            foreach (var k in stale)
                Reservations.Remove(k);
        }
    }
}
