using UnityEngine;
using UnityEngine.EventSystems;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Shows a game item's own tooltip while the pointer is over this UI element, through the same calls the
    /// game's item tooltip handler makes (GameItemElement.GetTooltip, OverlayHandler.ShowTooltip), so tooltip
    /// mods that patch those (e.g. Better Item Tooltips) apply too. The game's tooltip window sits below the mod
    /// window layer, so while shown from here it is drawn above this element's canvas. Lives in the main assembly:
    /// Unity cannot add a MonoBehaviour from the hot-reloaded panel assembly.
    /// </summary>
    sealed class ItemHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const int MachineCharWidth = 72;   // what the game's handler passes for machines
        static readonly Vector2 Translation = new Vector2(32, 0);
        public GameItem Item;
        bool hovered;

        static Canvas raised;
        static bool addedCanvas, oldOverride;
        static int oldOrder;

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Hide(); }
        void OnDisable() { if (hovered) Hide(); hovered = false; }

        // After the game's handler, which hides the tooltip when nothing of its own is under the pointer.
        void LateUpdate()
        {
            if (!hovered || OverlayHandler.current == null) return;
            var element = Item as GameItemElement;
            if (element == null || !element.HasTooltip()) { Hide(); return; }
            try
            {
                OverlayHandler.current.ShowTooltip(Input.mousePosition, Translation, element.GetTooltip(), MachineCharWidth);
                Raise();
            }
            catch (System.Exception e) { ModePuzzleMod.Log?.Warning("Item tooltip: " + e.Message); hovered = false; }
        }

        /// <summary>Puts the game's tooltip window above the canvas this element is drawn on.</summary>
        void Raise()
        {
            if (raised != null) return;
            var go = OverlayHandler.current.GetTooltipWindow()?.handler?.gameObject;
            if (go == null) return;
            int top = 0;
            foreach (var c in GetComponentsInParent<Canvas>()) top = Mathf.Max(top, c.sortingOrder);
            raised = go.GetComponent<Canvas>();
            addedCanvas = raised == null;
            if (addedCanvas) raised = go.AddComponent<Canvas>();
            oldOverride = raised.overrideSorting; oldOrder = raised.sortingOrder;
            raised.overrideSorting = true;
            raised.sortingOrder = top + 1;
        }

        static void Hide()
        {
            try { if (OverlayHandler.current != null) OverlayHandler.current.HideTooltip(); }
            catch (System.Exception) { }
            if (raised == null) return;
            if (addedCanvas) Destroy(raised);
            else { raised.overrideSorting = oldOverride; raised.sortingOrder = oldOrder; }
            raised = null;
        }
    }
}
