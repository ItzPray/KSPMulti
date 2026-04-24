using HarmonyLib;
using LmpClient.Events;
using LmpClient.Systems.Label;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// This harmony patch is intended to trigger an event when drawing a label
    /// </summary>
    [HarmonyPatch(typeof(VesselLabels))]
    [HarmonyPatch("ProcessLabel")]
    public class VesselLabels_ProcessLabel
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void PostfixProcessLabel(BaseLabel label)
        {
            // Always append lock owner text when ProcessLabel runs. Gating on activeSelf caused vessels whose label
            // GameObject was temporarily inactive (distance/culling) to miss the owner line on some clients while
            // the other client still saw the full label — asymmetric HUD around other players' craft.
            LabelEvent.onLabelProcessed.Fire(label);

            if (VesselLabelLockDiagnostics.IsEnabled && label is VesselLabel vesselLabel)
            {
                VesselLabelLockDiagnostics.AfterFlightLabelPipeline(
                    vesselLabel,
                    VesselLabelLockDiagnostics.AfterLmpHarmonyDiagnosticStage);
            }
        }
    }
}
