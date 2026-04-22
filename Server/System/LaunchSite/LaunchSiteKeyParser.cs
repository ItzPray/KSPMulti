using LunaConfigNode;
using LunaConfigNode.CfgNode;
using System;
using System.Globalization;
using VesselCfg = Server.System.Vessel.Classes.Vessel;

namespace Server.System.LaunchSite
{
    /// <summary>
    /// Derives a stable launch-pad key from server-side vessel config (same fields stock / mods write into vessel protos).
    /// </summary>
    public static class LaunchSiteKeyParser
    {
        /// <summary>
        /// KSP 1.8+ commonly uses sit = 14 for PRELAUNCH in vessel saves; also accept enum name.
        /// </summary>
        public static bool IsPrelaunchSituation(string sitValue)
        {
            if (string.IsNullOrEmpty(sitValue)) return false;
            if (sitValue.Equals("PRELAUNCH", StringComparison.OrdinalIgnoreCase))
                return true;
            if (int.TryParse(sitValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n == 14;
            return false;
        }

        /// <summary>
        /// Returns null if no recognizable launch site binding exists for pad coordination.
        /// </summary>
        public static string TryBuildSiteKey(VesselCfg vessel)
        {
            if (vessel?.Fields == null || vessel.Orbit == null)
                return null;

            if (!TryGetField(vessel.Fields, "sit", out var sit) || !IsPrelaunchSituation(sit))
                return null;

            if (!TryGetField(vessel.Orbit, "ref", out var refBody))
                refBody = "0";

            var bodyKey = $"rb{refBody.Trim()}";

            if (TryGetField(vessel.Fields, "landedAt", out var landedAt) && !string.IsNullOrWhiteSpace(landedAt))
                return $"{bodyKey}:{landedAt.Trim()}";

            if (TryGetField(vessel.Fields, "launchID", out var launchId) && !string.IsNullOrWhiteSpace(launchId))
                return $"{bodyKey}:{launchId.Trim()}";

            return $"{bodyKey}:unknown";
        }

        private static bool TryGetField(MixedCollection<string, string> fields, string key, out string value)
        {
            value = null;
            try
            {
                value = fields.GetSingle(key).Value;
                return !string.IsNullOrEmpty(value);
            }
            catch
            {
                return false;
            }
        }
    }
}
