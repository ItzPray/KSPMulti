using System.Collections;
using HarmonyLib;
using KSP.UI;
using LmpClient;
using LmpClient.Systems.LaunchPadCoordination;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock launch site list (<see cref="UILaunchsiteController"/>): disable Launch for pads another player occupies.
    /// </summary>
    public static class LaunchPadStockLaunchSiteUi
    {
        public static void RefreshAllInScene()
        {
            if (MainSystem.NetworkState < ClientState.Connected)
                return;

            foreach (var controller in Object.FindObjectsOfType<UILaunchsiteController>())
                ApplyOccupancyToController(controller);
        }

        internal static void ApplyOccupancyToController(UILaunchsiteController controller)
        {
            if (MainSystem.NetworkState < ClientState.Connected || controller == null)
                return;

            if (SettingsSystem.ServerSettings.LaunchPadCoordMode == LaunchPadCoordinationMode.Off)
            {
                SetAllLaunchButtonsInteractable(controller, true);
                return;
            }

            if (SettingsSystem.ServerSettings.LaunchPadCoordMode == LaunchPadCoordinationMode.LockAndOverflowBubble &&
                LaunchPadCoordinationSystem.Singleton.OverflowBubbleActive)
            {
                SetAllLaunchButtonsInteractable(controller, true);
                return;
            }

            var listObj = Traverse.Create(controller).Field("launchPadItems").GetValue();
            if (!(listObj is IList ilist))
                return;

            for (var i = 0; i < ilist.Count; i++)
            {
                var item = ilist[i];
                if (item == null)
                    continue;

                var siteName = Traverse.Create(item).Field<string>("siteName").Value;
                if (!LaunchPadSiteKeyUtil.TryBuildSpaceCenterSiteKey(siteName, out var siteKey))
                    continue;

                var blocked = LaunchPadCoordinationSystem.Singleton.IsSiteBlockedForLocalPlayer(siteKey);
                var button = Traverse.Create(item).Field<Button>("buttonLaunch").Value;
                if (button != null)
                {
                    button.interactable = !blocked;
                    ApplyOccupancyHoverHint(button, blocked, siteKey);
                }
            }
        }

        private static void SetAllLaunchButtonsInteractable(UILaunchsiteController controller, bool interactable)
        {
            var listObj = Traverse.Create(controller).Field("launchPadItems").GetValue();
            if (!(listObj is IList ilist))
                return;

            for (var i = 0; i < ilist.Count; i++)
            {
                var item = ilist[i];
                if (item == null)
                    continue;

                var button = Traverse.Create(item).Field<Button>("buttonLaunch").Value;
                if (button != null)
                {
                    button.interactable = interactable;
                    ApplyOccupancyHoverHint(button, false, string.Empty);
                }
            }
        }

        /// <summary>Hovering a disabled Launch button shows which player occupies the pad (server name).</summary>
        private static void ApplyOccupancyHoverHint(Button button, bool blocked, string siteKey)
        {
            if (button == null)
                return;

            var hint = button.gameObject.GetComponent<LaunchPadOccupiedHoverHint>();
            if (!blocked)
            {
                if (hint != null)
                    Object.Destroy(hint);
                return;
            }

            if (!LaunchPadCoordinationSystem.Singleton.TryGetBlockingOccupant(siteKey, out var occupant) ||
                string.IsNullOrEmpty(occupant))
            {
                if (hint != null)
                    Object.Destroy(hint);
                return;
            }

            if (hint == null)
                hint = button.gameObject.AddComponent<LaunchPadOccupiedHoverHint>();
            hint.SiteKey = siteKey ?? string.Empty;
            hint.OccupantName = occupant;
        }
    }

    [HarmonyPatch(typeof(UILaunchsiteController))]
    [HarmonyPatch("resetItems")]
    public class UILaunchsiteController_resetItems
    {
        [HarmonyPostfix]
        private static void PostfixResetItems(UILaunchsiteController __instance)
        {
            LaunchPadStockLaunchSiteUi.ApplyOccupancyToController(__instance);
        }
    }

    [HarmonyPatch(typeof(UILaunchsiteController))]
    [HarmonyPatch("addlaunchPadItem")]
    public class UILaunchsiteController_addlaunchPadItem
    {
        [HarmonyPostfix]
        private static void PostfixAddLaunchPadItem(UILaunchsiteController __instance)
        {
            LaunchPadStockLaunchSiteUi.ApplyOccupancyToController(__instance);
        }
    }
}
