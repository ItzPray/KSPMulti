using LmpCommon.Message.Data.ShareProgress;
using LunaConfigNode.CfgNode;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System.Scenario
{
    public partial class ScenarioDataUpdater
    {
        /// <summary>
        /// Multiple sibling <c>ExpParts</c> nodes break <see cref="ConfigNode.GetNode"/> (LunaConfigNode uses a
        /// unique-key map). Merges all bodies into the first node and removes the rest (hand-merged saves, rare double-writes).
        /// </summary>
        private static void ConsolidateDuplicateExpPartsSiblings(ConfigNode scenario)
        {
            const string expPartsName = "ExpParts";
            var outers = scenario.GetNodes(expPartsName);
            if (outers == null || outers.Count <= 1)
            {
                return;
            }

            var primary = outers[0].Value;
            if (primary == null)
            {
                return;
            }

            for (var i = 1; i < outers.Count; i++)
            {
                var other = outers[i].Value;
                if (other == null)
                {
                    continue;
                }

                foreach (var v in other.GetAllValues().ToArray())
                {
                    if (!int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) || c <= 0)
                    {
                        continue;
                    }

                    var existing = primary.GetValue(v.Key);
                    if (existing == null)
                    {
                        primary.CreateValue(new CfgNodeValue<string, string>(v.Key, v.Value));
                    }
                    else if (int.TryParse(existing.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var have))
                    {
                        existing.Value = global::System.Math.Max(have, c).ToString(CultureInfo.InvariantCulture);
                    }
                }

                scenario.RemoveNode(other);
            }
        }

        /// <summary>
        /// We received a experimental part message so update the scenario file accordingly
        /// </summary>
        public static void WriteExperimentalPartDataToFile(ShareProgressExperimentalPartMsgData experimentalPartMsg)
        {
            Task.Run(() =>
            {
                lock (Semaphore.GetOrAdd("ResearchAndDevelopment", new object()))
                {
                    lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                    {
                        if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue("ResearchAndDevelopment", out var scenario)) return;

                        ConsolidateDuplicateExpPartsSiblings(scenario);

                        var expPartNode = scenario.GetNode("ExpParts");
                        if (expPartNode == null && experimentalPartMsg.Count > 0)
                        {
                            scenario.AddNode(new ConfigNode("ExpParts", scenario));
                            expPartNode = scenario.GetNode("ExpParts");
                        }

                        var specificExpPart = expPartNode?.Value.GetValue(experimentalPartMsg.PartName);
                        if (specificExpPart == null)
                        {
                            var newVal = new CfgNodeValue<string, string>(experimentalPartMsg.PartName,
                                experimentalPartMsg.Count.ToString(CultureInfo.InvariantCulture));

                            expPartNode?.Value.CreateValue(newVal);
                        }
                        else
                        {
                            if (experimentalPartMsg.Count == 0)
                                expPartNode.Value.RemoveValue(specificExpPart.Key);
                            else
                                specificExpPart.Value = experimentalPartMsg.Count.ToString(CultureInfo.InvariantCulture);
                        }

                        if (expPartNode?.Value.GetAllValues().Count == 0)
                            scenario.RemoveNode(expPartNode.Value);
                    }
                }
            });
        }
    }
}
