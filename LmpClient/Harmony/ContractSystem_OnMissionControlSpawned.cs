using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Opening Mission Control must not let non-authoritative or transient clients generate mandatory contracts.
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem))]
    [HarmonyPatch("OnMissionControlSpawned")]
    public class ContractSystem_OnMissionControlSpawned
    {
        [HarmonyPrefix]
        private static bool PrefixOnMissionControlSpawned()
        {
            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return true;
            }

            var system = ShareContractsSystem.Singleton;
            if (system == null || !system.Enabled || !system.ShouldSuppressStockContractRefresh())
            {
                return true;
            }

            system.NoteSuppressedStockContractRefresh("ContractSystem.OnMissionControlSpawned");
            return false;
        }
    }
}
