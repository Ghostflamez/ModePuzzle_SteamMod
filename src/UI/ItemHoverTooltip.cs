using UnityEngine;
using UnityEngine.EventSystems;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Shows a game item's own tooltip while the pointer is over this UI element, through the same calls the
    /// game's item tooltip handler makes (GameItemElement.GetTooltip, OverlayHandler.ShowTooltip), so tooltip
    /// mods that patch those (e.g. Better Item Tooltips) apply too. Lives in the main assembly: Unity cannot add a
    /// MonoBehaviour from the hot-reloaded panel assembly.
    /// </summary>
    sealed class ItemHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const int MachineDelay = 72;   // what the game's handler passes for machines
        static readonly Vector2 Translation = new Vector2(32, 0);
        public GameItem Item;
        bool hovered;

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Hide(); }
        void OnDisable() { if (hovered) Hide(); hovered = false; }

        // After the game's handler, which hides the tooltip when nothing of its own is under the pointer.
        void LateUpdate()
        {
            if (!hovered || OverlayHandler.current == null) return;
            var element = Item as GameItemElement;
            if (element == null || !element.HasTooltip()) { Hide(); return; }
            try { OverlayHandler.current.ShowTooltip(Input.mousePosition, Translation, element.GetTooltip(), MachineDelay); }
            catch (System.Exception e) { ModePuzzleMod.Log?.Warning("Item tooltip: " + e.Message); hovered = false; }
        }

        static void Hide()
        {
            try { if (OverlayHandler.current != null) OverlayHandler.current.HideTooltip(); }
            catch (System.Exception) { }
        }
    }
}
