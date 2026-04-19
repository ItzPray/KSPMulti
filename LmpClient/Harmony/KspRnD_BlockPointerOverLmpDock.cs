using HarmonyLib;
using KSP.UI.Screens;
using LmpClient.Utilities;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Blocks R&amp;D (and other) facility <see cref="ScrollRect"/> drags when the pointer is over any visible LMP IMGUI window.
    /// Stock uses <see cref="ScrollRectDragOverride"/> on the tech tree; patching <see cref="ScrollRect"/> covers that and any plain scroll rects.
    /// </summary>
    [HarmonyPatch(typeof(ScrollRect), "OnBeginDrag")]
    public class ScrollRect_OnBeginDrag_BlockLmpImgui
    {
        /// <summary>Parameter name must match UnityEngine.UI.ScrollRect (Harmony injects by name).</summary>
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            _ = eventData;
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer();
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnDrag")]
    public class ScrollRect_OnDrag_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            _ = eventData;
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer();
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnEndDrag")]
    public class ScrollRect_OnEndDrag_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            _ = eventData;
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer();
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnScroll")]
    public class ScrollRect_OnScroll_BlockLmpImgui
    {
        /// <summary>ScrollRect.OnScroll uses <c>data</c>, not <c>eventData</c> — name must match for Harmony.</summary>
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData data)
        {
            _ = data;
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer();
        }
    }

    /// <summary>
    /// Blocks R&amp;D grid zoom from scroll wheel when the pointer is over LMP IMGUI.
    /// </summary>
    [HarmonyPatch(typeof(UIGridArea), nameof(UIGridArea.OnScroll))]
    public class UIGridArea_OnScroll_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            _ = eventData;
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer();
        }
    }
}
