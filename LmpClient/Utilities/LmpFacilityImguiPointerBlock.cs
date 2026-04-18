using LmpClient;
using LmpClient.Systems.SettingsSys;
using LmpClient.Windows;
using LmpCommon.Enums;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LmpClient.Utilities
{
    /// <summary>
    /// Facility uGUI (R&amp;D tech tree, etc.) still receives EventSystem input when LMP draws only IMGUI on top.
    /// Harmony patches call <see cref="ShouldSuppressFacilityUguiPointer"/> to skip stock drag/scroll handlers.
    /// </summary>
    public static class LmpFacilityImguiPointerBlock
    {
        private static int _cachedFrame = -1;
        private static bool _cachedShouldSuppress;

        /// <summary>
        /// When true, Harmony should skip the patched uGUI handler (prefix returns false).
        /// </summary>
        /// <param name="eventData">Unused for hit-testing; IMGUI overlap uses <see cref="Input.mousePosition"/> (same as window locks).</param>
        public static bool ShouldSuppressFacilityUguiPointer(PointerEventData eventData = null)
        {
            _ = eventData;

            if (!PassesSceneAndSessionGates())
            {
                return false;
            }

            var frame = Time.frameCount;
            if (_cachedFrame != frame)
            {
                _cachedFrame = frame;
                _cachedShouldSuppress = WindowsHandler.IsPointerOverVisibleLmpImguiOverlay(WindowsHandler.MousePositionImGui());
            }

            return _cachedShouldSuppress;
        }

        private static bool PassesSceneAndSessionGates()
        {
            if (MainSystem.Singleton == null || MainSystem.NetworkState < ClientState.Running)
            {
                return false;
            }

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene < GameScenes.SPACECENTER)
            {
                return false;
            }

            if (!SettingsSystem.CurrentSettings.DisclaimerAccepted || !MainSystem.ToolbarShowGui)
            {
                return false;
            }

            return true;
        }
    }
}
