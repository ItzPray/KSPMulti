using HarmonyLib;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Systems.Label;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// D8: last-resort postfix on <c>VesselLabels.ProcessLabel</c>. If the owner prefix disappeared after
    /// <see cref="VesselLabels_ProcessLabel"/>, another Harmony postfix likely rewrote the label.
    /// </summary>
    [HarmonyPatch(typeof(VesselLabels))]
    [HarmonyPatch("ProcessLabel")]
    public class VesselLabels_ProcessLabel_VeryLateHarmonyDiagnostic
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PostfixVeryLate(BaseLabel label)
        {
            if (!VesselLabelLockDiagnostics.IsEnabled)
                return;

            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            if (label is VesselLabel vesselLabel)
                VesselLabelLockDiagnostics.CheckVeryLateHarmonyStrip(vesselLabel);
        }
    }
}
