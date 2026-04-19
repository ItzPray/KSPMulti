using Contracts;
using HarmonyLib;
using KSP.UI.Screens;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;
using System.Diagnostics;
using System.Text;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Traces stock Mission Control / ContractSystem UI refresh entrypoints so we can correlate tab switches with
    /// <see cref="ContractSystem.Instance"/> contents (grep <c>[MC-Diag]</c> in persistent.log).
    /// </summary>
    [HarmonyPatch(typeof(MissionControl), "RebuildContractList")]
    public class MissionControl_RebuildContractListMcDiag
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (MainSystem.NetworkState < ClientState.Connected || HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                return;
            }

            ShareContractsSystem.LogMcUiContractInventory("MissionControl.RebuildContractList:stock-postfix" + ShortCaller());
        }

        private static string ShortCaller()
        {
            try
            {
                var st = new StackTrace(fNeedFileInfo: true);
                var sb = new StringBuilder();
                for (var i = 2; i < System.Math.Min(12, st.FrameCount); i++)
                {
                    var f = st.GetFrame(i);
                    var m = f?.GetMethod();
                    if (m == null)
                    {
                        continue;
                    }

                    sb.Append(" <- ");
                    sb.Append(m.DeclaringType?.Name ?? "?");
                    sb.Append(".");
                    sb.Append(m.Name);
                }

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    [HarmonyPatch(typeof(MissionControl), "RefreshContracts")]
    public class MissionControl_RefreshContractsMcDiag
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (MainSystem.NetworkState < ClientState.Connected || HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                return;
            }

            ShareContractsSystem.LogMcUiContractInventory("MissionControl.RefreshContracts:stock-postfix");
        }
    }

    [HarmonyPatch(typeof(ContractSystem), "RefreshContracts")]
    public class ContractSystem_RefreshContractsMcDiag
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (MainSystem.NetworkState < ClientState.Connected || HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                return;
            }

            ShareContractsSystem.LogMcUiContractInventory("ContractSystem.RefreshContracts:stock-postfix");
        }
    }
}
