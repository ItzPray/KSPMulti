using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LmpClient.Utilities;

namespace LmpClient.Harmony
{
    /// <summary>
    /// ClickThroughBlocker writes PluginData/PopUpShown.cfg but its OneTimePopup path only trusts
    /// the current save's GameParameters. KSPMP creates/loads game state during join, so mirror the
    /// user's saved CTB config back into the runtime parameters before CTB decides to show the popup.
    /// </summary>
    public static class ClickThroughBlockerOneTimePopupHarmony
    {
        private const string CtbAssemblyName = "ClickThroughBlocker";
        private const string CtbParamsTypeName = "ClickThroughFix.CTB";
        private const string OneTimePopupTypeName = "ClickThroughFix.OneTimePopup";
        private const string ClearInputLocksTypeName = "ClickThroughFix.ClearInputLocks";
        private const string PopUpShownRelativePath = "GameData/000_ClickThroughBlocker/PluginData/PopUpShown.cfg";
        private const string GlobalRelativePath = "GameData/000_ClickThroughBlocker/Global.cfg";

        private static bool _registered;
        private static MethodInfo _customParamsMethod;

        public static void TryRegister(HarmonyLib.Harmony harmony)
        {
            if (_registered)
                return;

            var popupType = AccessTools.TypeByName($"{OneTimePopupTypeName}, {CtbAssemblyName}") ??
                            AccessTools.TypeByName(OneTimePopupTypeName);
            if (popupType == null)
                return;

            var awake = AccessTools.Method(popupType, "Awake");
            if (awake == null)
                return;

            try
            {
                harmony.Patch(awake, prefix: new HarmonyMethod(typeof(ClickThroughBlockerOneTimePopupHarmony), nameof(PrefixAwake)));
                _registered = true;
                LunaLog.Log("[ClickThroughBlocker] Registered one-time popup config bridge.");
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[ClickThroughBlocker] Failed to register one-time popup config bridge: {ex.Message}");
            }
        }

        private static void PrefixAwake()
        {
            try
            {
                if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Parameters == null)
                    return;

                if (IsManualModeWindowOpen())
                    return;

                if (!TryGetSavedPopupDecision(out var focusFollowsClick))
                    return;

                var ctbParams = GetCtbCustomParams();
                if (ctbParams == null)
                    return;

                Traverse.Create(ctbParams).Field("showPopup").SetValue(false);
                if (focusFollowsClick.HasValue)
                    Traverse.Create(ctbParams).Field("focusFollowsclick").SetValue(focusFollowsClick.Value);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[ClickThroughBlocker] One-time popup config bridge failed: {ex.Message}");
            }
        }

        private static bool IsManualModeWindowOpen()
        {
            var clearInputLocksType = AccessTools.TypeByName($"{ClearInputLocksTypeName}, {CtbAssemblyName}") ??
                                      AccessTools.TypeByName(ClearInputLocksTypeName);
            var modeWindow = clearInputLocksType == null ? null : AccessTools.Field(clearInputLocksType, "modeWindow");
            return modeWindow != null && modeWindow.GetValue(null) != null;
        }

        private static bool TryGetSavedPopupDecision(out bool? focusFollowsClick)
        {
            focusFollowsClick = null;

            var globalPath = GetKspRelativePath(GlobalRelativePath);
            if (TryReadFocusFollowsClick(globalPath, out var savedFocusFollowsClick))
                focusFollowsClick = savedFocusFollowsClick;

            var popupShownPath = GetKspRelativePath(PopUpShownRelativePath);
            if (TryReadPopupShown(popupShownPath))
                return true;

            return focusFollowsClick.HasValue;
        }

        private static bool TryReadPopupShown(string path)
        {
            if (!File.Exists(path))
                return false;

            var text = File.ReadAllText(path);
            return text.IndexOf("popupshown", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   text.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadFocusFollowsClick(string path, out bool focusFollowsClick)
        {
            focusFollowsClick = false;
            if (!File.Exists(path))
                return false;

            var node = ConfigNode.Load(path);
            if (node != null && node.TryGetValue("focusFollowsClick", ref focusFollowsClick))
                return true;

            var text = File.ReadAllText(path);
            var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => l.TrimStart().StartsWith("focusFollowsClick", StringComparison.OrdinalIgnoreCase));
            if (line == null)
                return false;

            var value = line.Split('=').LastOrDefault();
            return value != null && bool.TryParse(value.Trim(), out focusFollowsClick);
        }

        private static object GetCtbCustomParams()
        {
            var ctbType = AccessTools.TypeByName($"{CtbParamsTypeName}, {CtbAssemblyName}") ??
                          AccessTools.TypeByName(CtbParamsTypeName);
            if (ctbType == null)
                return null;

            var method = _customParamsMethod ?? (_customParamsMethod = typeof(GameParameters).GetMethods()
                .FirstOrDefault(m => m.Name == "CustomParams" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0));
            return method?.MakeGenericMethod(ctbType).Invoke(HighLogic.CurrentGame.Parameters, null);
        }

        private static string GetKspRelativePath(string relativePath)
        {
            return CommonUtil.CombinePaths(KSPUtil.ApplicationRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
