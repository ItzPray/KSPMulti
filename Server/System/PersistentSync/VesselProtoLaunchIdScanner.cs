using System.Globalization;
using System.Text.RegularExpressions;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Extracts the maximum <c>launchID</c> token from serialized vessel ConfigNode text (PART snapshots).
    /// </summary>
    public static class VesselProtoLaunchIdScanner
    {
        private static readonly Regex LaunchIdRegex = new Regex(
            @"\blaunchID\s*=\s*(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Returns false when no <c>launchID</c> keys are found.</summary>
        public static bool TryGetMaxLaunchId(string vesselConfigText, out uint maxLaunchId)
        {
            maxLaunchId = 0;
            if (string.IsNullOrEmpty(vesselConfigText))
            {
                return false;
            }

            foreach (Match m in LaunchIdRegex.Matches(vesselConfigText))
            {
                if (m.Groups.Count > 1 &&
                    uint.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    maxLaunchId = global::System.Math.Max(maxLaunchId, n);
                }
            }

            return maxLaunchId > 0;
        }
    }
}
