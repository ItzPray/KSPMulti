using System;
using System.Collections;
using HarmonyLib;
using LmpClient;
using LmpClient.Systems.LaunchPadCoordination;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace LmpClient.Harmony
{
    /// <summary>
    /// Kerbal Konstructs <c>LaunchsiteSelectorGUI</c> (KSC Enhanced stack): grey toggles and block Set Launchsite when server occupancy blocks.
    /// </summary>
    public static class KerbalKonstructsLaunchPadHarmony
    {
        private static bool _registered;

        public static void TryRegister(HarmonyLib.Harmony harmony)
        {
            if (_registered || !LaunchPadKsceCompatibility.KsceHarmonyPatchesAllowed)
                return;

            var t = AccessTools.TypeByName("KerbalKonstructs.UI.LaunchsiteSelectorGUI, KerbalKonstructs")
                    ?? AccessTools.TypeByName("KerbalKonstructs.UI.LaunchsiteSelectorGUI");
            if (t == null)
                return;

            try
            {
                var build = AccessTools.Method(t, "BuildLaunchsites");
                var set = AccessTools.Method(t, "SetLaunchsite");
                if (build != null)
                    harmony.Patch(build, postfix: new HarmonyMethod(typeof(KerbalKonstructsLaunchPadHarmony), nameof(PostfixBuildLaunchsites)));
                if (set != null)
                    harmony.Patch(set, prefix: new HarmonyMethod(typeof(KerbalKonstructsLaunchPadHarmony), nameof(PrefixSetLaunchsite)));
                _registered = true;
                LunaLog.Log("[LaunchPad][KK] Registered Kerbal Konstructs launch site UI patches.");
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[LaunchPad][KK] Failed to register patches: {ex.Message}");
            }
        }

        private static void PostfixBuildLaunchsites(object __instance)
        {
            try
            {
                if (MainSystem.NetworkState < ClientState.Connected) return;
                if (SettingsSystem.ServerSettings.LaunchPadCoordMode == LaunchPadCoordinationMode.Off) return;

                if (SettingsSystem.ServerSettings.LaunchPadCoordMode == LaunchPadCoordinationMode.LockAndOverflowBubble &&
                    LaunchPadCoordinationSystem.Singleton.OverflowBubbleActive)
                    return;

                var listObj = Traverse.Create(__instance).Field("launchsiteItems").GetValue();
                if (!(listObj is IList ilist)) return;

                var content = Traverse.Create(listObj).Property("Content").GetValue<object>();
                var rt = Traverse.Create(content).Property("rectTransform").GetValue<RectTransform>();
                if (rt == null) return;

                for (var i = 0; i < ilist.Count && i < rt.childCount; i++)
                {
                    var item = ilist[i];
                    if (item == null) continue;

                    var ls = Traverse.Create(item).Field("launchsite").GetValue<object>();
                    if (ls == null) continue;

                    var siteName = Traverse.Create(ls).Property("LaunchSiteName").GetValue<string>()
                                   ?? Traverse.Create(ls).Field("LaunchSiteName").GetValue<string>();
                    if (string.IsNullOrEmpty(siteName)) continue;

                    if (!LaunchPadSiteKeyUtil.TryBuildSpaceCenterSiteKey(siteName, out var siteKey)) continue;

                    var blocked = LaunchPadCoordinationSystem.Singleton.IsSiteBlockedForLocalPlayer(siteKey);
                    var toggle = rt.GetChild(i).GetComponent<Toggle>();
                    if (toggle != null)
                    {
                        toggle.interactable = !blocked;
                        ApplyOccupancyHoverHint(toggle, blocked, siteKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[LaunchPad][KK] BuildLaunchsites postfix: {ex.Message}");
            }
        }

        private static void ApplyOccupancyHoverHint(Toggle toggle, bool blocked, string siteKey)
        {
            if (toggle == null)
                return;

            var hint = toggle.gameObject.GetComponent<LaunchPadOccupiedHoverHint>();
            if (!blocked)
            {
                if (hint != null)
                    UnityEngine.Object.Destroy(hint);
                return;
            }

            if (!LaunchPadCoordinationSystem.Singleton.TryGetBlockingOccupant(siteKey, out var occupant) ||
                string.IsNullOrEmpty(occupant))
            {
                if (hint != null)
                    UnityEngine.Object.Destroy(hint);
                return;
            }

            if (hint == null)
                hint = toggle.gameObject.AddComponent<LaunchPadOccupiedHoverHint>();
            hint.SiteKey = siteKey ?? string.Empty;
            hint.OccupantName = occupant;
        }

        private static bool PrefixSetLaunchsite()
        {
            try
            {
                if (MainSystem.NetworkState < ClientState.Connected) return true;
                var t = AccessTools.TypeByName("KerbalKonstructs.UI.LaunchsiteSelectorGUI, KerbalKonstructs")
                        ?? AccessTools.TypeByName("KerbalKonstructs.UI.LaunchsiteSelectorGUI");
                if (t == null) return true;

                var sel = Traverse.Create(t).Field("selectedSite").GetValue<object>();
                if (sel == null) return true;

                var siteName = Traverse.Create(sel).Property("LaunchSiteName").GetValue<string>()
                               ?? Traverse.Create(sel).Field("LaunchSiteName").GetValue<string>();
                if (string.IsNullOrEmpty(siteName)) return true;

                if (!LaunchPadSiteKeyUtil.TryBuildSpaceCenterSiteKey(siteName, out var key))
                    return true;

                if (LaunchPadCoordinationSystem.Singleton.IsSiteBlockedForLocalPlayer(key))
                {
                    LunaLog.LogWarning("[LaunchPad][KK] Blocked SetLaunchsite — pad occupied on server.");
                    return false;
                }

                LaunchPadCoordinationSystem.Singleton.MessageSender.SendReserveSiteRequest(key);
                return true;
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[LaunchPad][KK] SetLaunchsite prefix: {ex.Message}");
                return true;
            }
        }
    }
}
