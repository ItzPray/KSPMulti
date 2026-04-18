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
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer(eventData);
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnDrag")]
    public class ScrollRect_OnDrag_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer(eventData);
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnEndDrag")]
    public class ScrollRect_OnEndDrag_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer(eventData);
        }
    }

    [HarmonyPatch(typeof(ScrollRect), "OnScroll")]
    public class ScrollRect_OnScroll_BlockLmpImgui
    {
        [HarmonyPrefix]
        private static bool Prefix(PointerEventData eventData)
        {
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer(eventData);
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
            return !LmpFacilityImguiPointerBlock.ShouldSuppressFacilityUguiPointer(eventData);
        }
    }
}
