using LunaConfigNode.CfgNode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Server.System
{
    public static class ContractVesselReferenceSystem
    {
        private const string ContractSystemScenarioName = "ContractSystem";
        private const string ContractsNodeName = "CONTRACTS";
        private const string ContractNodeName = "CONTRACT";
        private const string StateFieldName = "state";
        private const string GuidFieldName = "guid";

        private static readonly Regex GuidPattern = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])",
            RegexOptions.Compiled);

        public static bool IsReferencedByActiveContract(Guid vesselId)
        {
            if (vesselId == Guid.Empty)
                return false;

            return GetActiveContractReferencedVessels().Contains(vesselId);
        }

        public static HashSet<Guid> GetActiveContractReferencedVessels()
        {
            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ContractSystemScenarioName, out var scenario))
                {
                    return new HashSet<Guid>();
                }

                return GetActiveContractReferencedVessels(scenario);
            }
        }

        internal static HashSet<Guid> GetActiveContractReferencedVessels(ConfigNode contractSystemScenario)
        {
            var vesselIds = new HashSet<Guid>();
            if (contractSystemScenario == null)
            {
                return vesselIds;
            }

            foreach (var contractsNode in EnumerateNodesByName(contractSystemScenario, ContractsNodeName))
            {
                foreach (var contractNode in contractsNode.GetNodes(ContractNodeName).Select(n => n.Value))
                {
                    if (!IsActiveContract(contractNode))
                    {
                        continue;
                    }

                    CollectReferencedGuids(contractNode, vesselIds, skipContractRootGuid: true);
                }
            }

            return vesselIds;
        }

        private static IEnumerable<ConfigNode> EnumerateNodesByName(ConfigNode node, string name)
        {
            if (node == null)
            {
                yield break;
            }

            if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return node;
            }

            foreach (var childNode in node.GetAllNodes())
            {
                foreach (var match in EnumerateNodesByName(childNode, name))
                {
                    yield return match;
                }
            }
        }

        private static bool IsActiveContract(ConfigNode contractNode)
        {
            var state = contractNode.GetValue(StateFieldName)?.Value;
            return string.Equals(state?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectReferencedGuids(ConfigNode node, ISet<Guid> vesselIds, bool skipContractRootGuid)
        {
            foreach (var value in node.GetAllValues())
            {
                if (skipContractRootGuid && string.Equals(value.Key, GuidFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CollectGuidsFromValue(value.Value, vesselIds);
            }

            foreach (var childNode in node.GetAllNodes())
            {
                CollectReferencedGuids(childNode, vesselIds, skipContractRootGuid: false);
            }
        }

        private static void CollectGuidsFromValue(string value, ISet<Guid> vesselIds)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (Match match in GuidPattern.Matches(value))
            {
                if (Guid.TryParse(match.Value, out var guid) && guid != Guid.Empty)
                {
                    vesselIds.Add(guid);
                }
            }
        }
    }
}
