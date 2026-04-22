using UnityEngine;

namespace LmpClient.Systems.LaunchPadCoordination
{
    /// <summary>
    /// Builds the same site key string the server derives from PRELAUNCH vessel protos (orbit ref + landedAt).
    /// Space center launch uses the home body and the internal launch site id (matches vessel landedAt).
    /// </summary>
    public static class LaunchPadSiteKeyUtil
    {
        public static bool TryBuildSpaceCenterSiteKey(string siteInternalName, out string siteKey)
        {
            siteKey = null;
            if (string.IsNullOrEmpty(siteInternalName))
                return false;

            var body = FlightGlobals.GetHomeBody();
            if (body == null)
                return false;

            siteKey = $"rb{body.flightGlobalsIndex}:{siteInternalName.Trim()}";
            return true;
        }
    }
}
