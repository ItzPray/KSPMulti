using HarmonyLib;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Systems.Label;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// D8: runs after stock <c>VesselLabels.ProcessLabel</c> returns, before KSPMP injects the owner line.
    /// </summary>
    [HarmonyPatch(typeof(VesselLabels))]
    [HarmonyPatch("ProcessLabel")]
    public class VesselLabels_ProcessLabel_BeforeLmpEventDiagnostic
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void PostfixBeforeLmpEvent(BaseLabel label)
        {
            if (!VesselLabelLockDiagnostics.IsEnabled)
                return;

            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            if (label is VesselLabel vesselLabel)
                VesselLabelLockDiagnostics.AfterStockBeforeLmpEvent(vesselLabel);
        }
    }
}
