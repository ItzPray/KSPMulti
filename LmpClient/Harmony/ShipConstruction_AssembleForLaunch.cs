using HarmonyLib;
using LmpClient;
using LmpClient.Events;
using LmpClient.Systems.LaunchPadCoordination;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// This harmony patch is intended to override the "FindVesselsLandedAt" that sometimes is called to check if there are vessels in a launch site
    /// We just remove the other controlled vessels from that check and set them correctly
    /// </summary>
    [HarmonyPatch(typeof(ShipConstruction))]
    [HarmonyPatch("AssembleForLaunch")]
    [HarmonyPatch(new[]
    {
        typeof(ShipConstruct), typeof(string), typeof(string), typeof(string), typeof(Game),
        typeof(VesselCrewManifest), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
        typeof(Orbit), typeof(bool), typeof(bool)
    })]
    public class ShipConstruction_AssembleForLaunch
    {
        [HarmonyPrefix]
        private static void PrefixAssembleForLaunch(ShipConstruct ship, string landedAt, string displaylandedAt, string flagURL, Game sceneState, VesselCrewManifest crewManifest,
            bool fromShipAssembly, bool setActiveVessel, bool isLanded, bool preCreate, Orbit orbit, bool orbiting, bool isSplashed)
        {
            if (fromShipAssembly && ship != null)
                VesselAssemblyEvent.onAssemblingVessel.Fire(ship);

            if (fromShipAssembly && MainSystem.NetworkState >= ClientState.Connected)
            {
                var mode = SettingsSystem.ServerSettings.LaunchPadCoordMode;
                if (mode != LaunchPadCoordinationMode.Off)
                {
                    var siteName = !string.IsNullOrEmpty(landedAt) ? landedAt : displaylandedAt;
                    if (LaunchPadSiteKeyUtil.TryBuildSpaceCenterSiteKey(siteName, out var siteKey))
                        LaunchPadCoordinationSystem.Singleton.MessageSender.SendReserveSiteRequest(siteKey);
                }
            }
        }

        [HarmonyPostfix]
        private static void PostfixAssembleForLaunch(ShipConstruct ship, string landedAt, string displaylandedAt, string flagURL, Game sceneState, VesselCrewManifest crewManifest,
            bool fromShipAssembly, bool setActiveVessel, bool isLanded, bool preCreate, Orbit orbit, bool orbiting, bool isSplashed, Vessel __result)
        {
            if (fromShipAssembly && __result && ship != null)
                VesselAssemblyEvent.onAssembledVessel.Fire(__result, ship);
        }
    }
}
