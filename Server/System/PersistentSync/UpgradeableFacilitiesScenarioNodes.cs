using LunaConfigNode.CfgNode;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// KSP may serialize <c>ScenarioUpgradeableFacilities</c> either with slash-named nodes
    /// (<c>SpaceCenter/LaunchPad { lvl = ... }</c>, as LMP templates do) or nested
    /// (<c>SpaceCenter { LaunchPad { lvl = ... } }</c>). LunaConfigNode <c>GetNode</c> does not
    /// traverse slashes, so both layouts must be handled for load/persist and legacy writers.
    /// </summary>
    internal static class UpgradeableFacilitiesScenarioNodes
    {
        internal const string LevelValueName = "lvl";

        internal static bool TryGetFacilityNode(ConfigNode scenario, string facilityId, out ConfigNode facilityNode)
        {
            facilityNode = scenario.GetNode(facilityId)?.Value;
            if (facilityNode != null)
            {
                return true;
            }

            var slash = facilityId.IndexOf('/');
            if (slash <= 0 || slash >= facilityId.Length - 1)
            {
                return false;
            }

            var parentName = facilityId.Substring(0, slash);
            var childName = facilityId.Substring(slash + 1);
            var parentNode = scenario.GetNode(parentName)?.Value;
            if (parentNode == null)
            {
                return false;
            }

            facilityNode = parentNode.GetNode(childName)?.Value;
            return facilityNode != null;
        }

        internal static void EnsureFacilityLevelValue(ConfigNode scenario, string facilityId, string lvlValue)
        {
            if (TryGetFacilityNode(scenario, facilityId, out var existingNode))
            {
                existingNode.UpdateValue(LevelValueName, lvlValue);
                return;
            }

            var facilityNode = new ConfigNode(facilityId, scenario);
            scenario.AddNode(facilityNode);
            facilityNode.UpdateValue(LevelValueName, lvlValue);
        }
    }
}
