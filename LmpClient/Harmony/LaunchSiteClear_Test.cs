using HarmonyLib;
using LmpClient;
using LmpClient.Systems.LaunchPadCoordination;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using PreFlightTests;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock preflight test for launch site obstruction. When connected, LMP historically forced this test to pass.
    /// With launch pad coordination enabled, we restore blocking when another player occupies the same server site key,
    /// except in overflow+bubble mode while the server has raised the effective bubble.
    /// </summary>
    [HarmonyPatch(typeof(LaunchSiteClear))]
    [HarmonyPatch("Test")]
    public class LaunchSiteClear_Test
    {
        [HarmonyPostfix]
        private static void PostfixTest(LaunchSiteClear __instance, ref bool __result)
        {
            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            var mode = SettingsSystem.ServerSettings.LaunchPadCoordMode;
            if (mode == LaunchPadCoordinationMode.Off)
            {
                __result = true;
                return;
            }

            if (mode == LaunchPadCoordinationMode.LockAndOverflowBubble && LaunchPadCoordinationSystem.Singleton.OverflowBubbleActive)
            {
                __result = true;
                return;
            }

            var siteName = Traverse.Create(__instance).Field<string>("siteName").Value;
            if (!LaunchPadSiteKeyUtil.TryBuildSpaceCenterSiteKey(siteName, out var siteKey))
            {
                __result = true;
                return;
            }

            if (LaunchPadCoordinationSystem.Singleton.IsSiteBlockedForLocalPlayer(siteKey))
                __result = false;
            else
                __result = true;
        }
    }
}
