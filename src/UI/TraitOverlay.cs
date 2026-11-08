using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ModePuzzle.Game;
using ModePuzzle.Model;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Optional: trait badges on every module in the game (inventories, machine bays, counters), drawn the way the
    /// game draws its own pin overlay. The badge layer hangs under the item's "middle" node, which does not rotate,
    /// and is laid out from the item's current shape (rotation and flip applied), so the icons stay upright and
    /// follow the cells whenever the module is turned or flipped.
    /// </summary>
    static class TraitOverlay
    {
        const string NodeName = "ModePuzzle.TraitBadges";
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<GameItemElement, GameObject> nodes = new Dictionary<GameItemElement, GameObject>();

        public static bool Enabled => ModePuzzleMod.TraitBadges?.Value == true;

        /// <summary>The setting changed: add badges to every owned module now, or remove all of them.</summary>
        public static void SettingChanged()
        {
            if (!Enabled) { Clear(); return; }
            try
            {
                var items = EmporiumEntry.Instance?.GetAllOwnedItems();
                if (items == null) return;
                foreach (var item in items)
                    if (item is GameItemElement element) Update(element);
            }
            catch (Exception e) { ModePuzzleMod.Log?.Warning("Trait badges: " + e.Message); }
        }

        public static void Clear()
        {
            foreach (var node in nodes.Values)
                if (node != null) UnityEngine.Object.Destroy(node);
            nodes.Clear();
        }

        /// <summary>Called after the game re-lays out an item's overlays (move, rotate, flip, resize).</summary>
        public static void Update(GameItemElement item)
        {
            nodes.TryGetValue(item, out var node);
            int w = 0, h = 0;
            var sprite = Enabled ? SpriteFor(item, out w, out h) : null;
            if (sprite == null)
            {
                if (node != null) node.SetActive(false);
                return;
            }
            var middle = item.middle?.rectTransform;
            if (middle == null) return;
            if (node == null)
            {
                foreach (var stale in nodes.Where(kv => kv.Value == null || kv.Key.IsDestroyed()).Select(kv => kv.Key).ToList())
                    nodes.Remove(stale);
                node = new GameObject(NodeName, typeof(RectTransform), typeof(Image));
                nodes[item] = node;
            }
            var rect = (RectTransform)node.transform;
            if (rect.parent != middle) rect.SetParent(middle, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            float grid = UISettings.current.gridSize;
            rect.sizeDelta = new Vector2(w * grid, h * grid);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.SetAsLastSibling();
            var image = node.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            node.SetActive(item.background?.image == null || item.background.image.enabled);
        }

        /// <summary>Badge image over the item's bounding box (16 px per cell), or null for no badges.</summary>
        static Sprite SpriteFor(GameItemElement item, out int w, out int h)
        {
            w = h = 0;
            if (item.isDebugMenu || !LiveReader.IsModule(item)) return null;
            var module = LiveReader.ReadModule(item);
            if (module == null || module.IsNode || TraitIcons.ForModule(module).Length == 0) return null;
            var shape = item.modifiedShape;
            if (shape == null) return null;
            var placed = new PlacedModule { Module = module };
            for (int y = shape.minY; y <= shape.maxY; y++)
                for (int x = shape.minX; x <= shape.maxX; x++)
                    if (shape.Get(x, y) != 0) placed.Cells.Add(new Cell(x - shape.minX, y - shape.minY));
            if (placed.Cells.Count == 0) return null;
            w = shape.maxX - shape.minX + 1; h = shape.maxY - shape.minY + 1;
            string key = string.Join(",", TraitIcons.ForModule(module)) + "|" + w + "x" + h + "|" +
                         string.Join(";", placed.Cells.Select(c => c.X + "," + c.Y));
            if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            int pw = w * TraitIcons.CellPixels, ph = h * TraitIcons.CellPixels;
            var px = new Px[pw * ph];
            if (TraitIcons.Stamp(px, pw, ph, placed) == 0) return null;
            return sprites[key] = Pixels.ToSprite(px, pw, ph, "modepuzzle_traits");
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "UpdatePinOverlay")]
    static class TraitOverlayPatch
    {
        static void Postfix(GameItemElement __instance)
        {
            try { TraitOverlay.Update(__instance); }
            catch (Exception e) { ModePuzzleMod.Log?.Warning("Trait badges: " + e.Message); }
        }
    }
}
