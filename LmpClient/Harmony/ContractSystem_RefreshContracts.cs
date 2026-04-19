using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// ContractSystem owns contract replenishment. During transient warp/subspace states, or when this client does not
    /// own the contract lock, suppress stock refresh so offers are regenerated only once time has settled.
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem))]
    [HarmonyPatch("RefreshContracts")]
    public class ContractSystem_RefreshContracts
    {
        [HarmonyPrefix]
        private static bool PrefixRefreshContracts()
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

            system.NoteSuppressedStockContractRefresh("ContractSystem.RefreshContracts");
            return false;
        }
    }
}
