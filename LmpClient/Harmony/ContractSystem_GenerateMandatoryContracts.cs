using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Mandatory/tutorial contracts must only be generated inside LMP's controlled refresh window.
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem))]
    [HarmonyPatch("GenerateMandatoryContracts")]
    public class ContractSystem_GenerateMandatoryContracts
    {
        [HarmonyPrefix]
        private static bool PrefixGenerateMandatoryContracts()
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

            system.NoteSuppressedStockContractRefresh("ContractSystem.GenerateMandatoryContracts");
            return false;
        }
    }
}
