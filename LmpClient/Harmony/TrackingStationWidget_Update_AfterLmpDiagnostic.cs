using HarmonyLib;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Systems.Label;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Runs after <see cref="TrackingStationWidget_Update"/> for tracking-station name vs control lock (D8).
    /// </summary>
    [HarmonyPatch(typeof(TrackingStationWidget))]
    [HarmonyPatch("Update")]
    public class TrackingStationWidget_Update_AfterLmpDiagnostic
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PostfixDiagnostic(TrackingStationWidget __instance)
        {
            if (!VesselLabelLockDiagnostics.IsEnabled)
                return;

            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            VesselLabelLockDiagnostics.AfterTrackingWidgetPipeline(__instance, "HarmonyPostfixAfterLmpTrackingWidget");
        }
    }
}
