using System.Globalization;
using System.Threading.Tasks;
using Server.System.PersistentSync;

namespace Server.System.Scenario
{
    public partial class ScenarioDataUpdater
    {
        /// <summary>
        /// We received a facility upgrade message so update the scenario file accordingly
        /// </summary>
        public static void WriteFacilityLevelDataToFile(string facilityId, float level)
        {
            Task.Run(() =>
            {
                lock (Semaphore.GetOrAdd("ScenarioUpgradeableFacilities", new object()))
                {
                    if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue("ScenarioUpgradeableFacilities", out var scenario)) return;

                    if (!UpgradeableFacilitiesScenarioNodes.TryGetFacilityNode(scenario, facilityId, out var facilityNode))
                    {
                        return;
                    }

                    facilityNode.UpdateValue(UpgradeableFacilitiesScenarioNodes.LevelValueName, level.ToString(CultureInfo.InvariantCulture));
                }
            });
        }
    }
}
