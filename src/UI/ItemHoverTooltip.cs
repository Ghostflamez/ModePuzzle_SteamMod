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

        // Better Item Tooltips draws its appraisal slip on objects of its own, outside the game's tooltip window.
        static readonly string[] TooltipModRoots = { "SectionedTooltipsTicket", "SectionedTooltipsPanels", "SectionedTooltipsOverlay" };

        sealed class Raised
        {
            public Canvas Canvas;
            public bool Added, OldOverride;
            public int OldOrder;
        }
        static readonly System.Collections.Generic.List<Raised> raised = new System.Collections.Generic.List<Raised>();

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

        /// <summary>
        /// Puts the game's tooltip window, and the tooltip mod's slip when present, above the canvas this element is
        /// drawn on. Checked every frame: the mod creates its objects on first use.
        /// </summary>
        void Raise()
        {
            int top = 0;
            foreach (var c in GetComponentsInParent<Canvas>()) top = Mathf.Max(top, c.sortingOrder);
            RaiseObject(OverlayHandler.current.GetTooltipWindow()?.handler?.gameObject, top + 1);
            foreach (var name in TooltipModRoots) RaiseObject(GameObject.Find(name), top + 1);
        }

        static void RaiseObject(GameObject go, int order)
        {
            if (go == null) return;
            var canvas = go.GetComponent<Canvas>();
            foreach (var r in raised) if (r.Canvas != null && r.Canvas == canvas) return;
            var record = new Raised { Added = canvas == null };
            if (record.Added) canvas = go.AddComponent<Canvas>();
            record.Canvas = canvas; record.OldOverride = canvas.overrideSorting; record.OldOrder = canvas.sortingOrder;
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            raised.Add(record);
        }

        static void Hide()
        {
            try { if (OverlayHandler.current != null) OverlayHandler.current.HideTooltip(); }
            catch (System.Exception) { }
            foreach (var r in raised)
            {
                if (r.Canvas == null) continue;
                if (r.Added) Destroy(r.Canvas);
                else { r.Canvas.overrideSorting = r.OldOverride; r.Canvas.sortingOrder = r.OldOrder; }
            }
            raised.Clear();
        }
    }
}
