using HarmonyLib;
using KSP.UI.Screens.Mapview;
using LmpClient;
using LmpClient.Systems.Label;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Runs after <see cref="OrbitRendererBase_OnUpdateCaption"/> so map orbit captions can be checked vs locks (D8).
    /// </summary>
    [HarmonyPatch(typeof(OrbitRendererBase))]
    [HarmonyPatch("objectNode_OnUpdateCaption")]
    public class OrbitRendererBase_OnUpdateCaption_AfterLmpDiagnostic
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PostfixDiagnostic(OrbitRendererBase __instance, MapNode n, MapNode.CaptionData data)
        {
            if (!VesselLabelLockDiagnostics.IsEnabled)
                return;

            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            if (__instance?.vessel == null)
                return;

            VesselLabelLockDiagnostics.AfterMapCaptionPipeline(__instance.vessel, data, "HarmonyPostfixAfterLmpMapCaption");
        }
    }
}
