using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModePuzzle.UI
{
    /// <summary>
    /// The game's item tooltip handler raycasts through everything under the pointer and shows the first item it
    /// finds, even one hidden behind a mod window. While the topmost thing under the pointer belongs to one of the
    /// optimizer's windows, the handler is skipped and its tooltip hidden; the panel's own machine tooltips
    /// (ItemHoverTooltip, drawn later in the frame) are unaffected.
    /// </summary>
    static class TooltipShield
    {
        static readonly List<RectTransform> windows = new List<RectTransform>();
        static readonly List<RaycastResult> hits = new List<RaycastResult>();
        static PointerEventData pointer;
        static EventSystem pointerSystem;

        /// <summary>The panel registers each window it builds; destroyed ones drop out on their own.</summary>
        public static void Register(RectTransform window)
        {
            windows.RemoveAll(w => w == null);
            if (window != null && !windows.Contains(window)) windows.Add(window);
        }

        public static bool PointerOverWindow()
        {
            windows.RemoveAll(w => w == null);
            var events = EventSystem.current;
            if (windows.Count == 0 || events == null) return false;
            if (pointer == null || pointerSystem != events) { pointer = new PointerEventData(events); pointerSystem = events; }
            pointer.position = Input.mousePosition;
            hits.Clear();
            events.RaycastAll(pointer, hits);
            if (hits.Count == 0) return false;
            var top = hits[0].gameObject.transform;   // RaycastAll sorts the topmost first
            foreach (var w in windows)
                if (w.gameObject.activeInHierarchy && top.IsChildOf(w)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemTooltipHandler), "OnEventUpdate")]
    static class TooltipShieldPatch
    {
        static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TooltipShield.PointerOverWindow()) return true;
                OverlayHandler.current?.HideTooltip();
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                ModePuzzleMod.Log?.Warning("Tooltip shield: " + e.Message);
                return true;
            }
        }
    }
}
