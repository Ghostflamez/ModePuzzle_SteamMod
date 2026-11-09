using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// Development builds: where things are on the screen, for the promo recorder that moves the real mouse.
    /// Answers "x y w h" in desktop pixels with the origin at the top left (Windows cursor coordinates).
    /// </summary>
    static partial class OptimizerPanel
    {
        static readonly Regex Tags = new Regex("<[^>]+>");

        static string ScreenRect(RectTransform rect)
        {
            if (rect == null) return "none";
            var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float x = Mathf.Min(a.x, b.x), y = Screen.height - Mathf.Max(a.y, b.y);
            return $"{Mathf.RoundToInt(x)} {Mathf.RoundToInt(y)} {Mathf.RoundToInt(Mathf.Abs(b.x - a.x))} {Mathf.RoundToInt(Mathf.Abs(b.y - a.y))}";
        }

        /// <summary>
        /// The visible text (rich-text tags and brackets ignored) equal to <paramref name="text"/>, else containing it;
        /// answers its button when it sits on one. "k:text" picks the k-th match (0-based), top to bottom then left to right; "b:" looks only at buttons.
        /// </summary>
        static string Where(string query)
        {
            int pick = 0;
            bool buttons = query.StartsWith("b:");
            if (buttons) query = query.Substring(2);
            // "u:" looks only inside the mod's windows (not at the game's tooltips or item context menus).
            bool ours = query.StartsWith("u:");
            if (ours) query = query.Substring(2);
            var layer = CustomUIManager.Instance?.GetLayerRoot(CustomUIManager.LAYER_OVERLAY);
            var m = Regex.Match(query, @"^(\d+):(.*)$");
            if (m.Success) { pick = int.Parse(m.Groups[1].Value); query = m.Groups[2].Value; }
            string Clean(string s) => Tags.Replace(s ?? "", "").Replace("[", "").Replace("]", "").Trim();
            var texts = Object.FindObjectsOfType<TMP_Text>().Where(t => t.isActiveAndEnabled && t.alpha > 0.01f &&
                (!buttons || t.GetComponentInParent<Button>() != null) && (!ours || layer != null && t.transform.IsChildOf(layer))).ToList();
            var hits = texts.Where(t => Clean(t.text) == query).ToList();
            if (hits.Count == 0) hits = texts.Where(t => Clean(t.text).Contains(query)).ToList();
            if (hits.Count == 0) return "none";
            var rects = hits.Select(t => (RectTransform)(t.GetComponentInParent<Button>()?.transform ?? t.transform))
                .Distinct().OrderBy(r => -r.position.y).ThenBy(r => r.position.x).ToList();
            return pick < rects.Count ? ScreenRect(rects[pick]) : "none";
        }

        /// <summary>What the pointer would hit at desktop pixel x, y (top-left origin), topmost first.</summary>
        static string Hit(string args)
        {
            var p = args.Split(' ');
            if (p.Length < 2 || !float.TryParse(p[0], out float x) || !float.TryParse(p[1], out float y)) return "usage: hit x y";
            var events = UnityEngine.EventSystems.EventSystem.current;
            var data = new UnityEngine.EventSystems.PointerEventData(events) { position = new Vector2(x, Screen.height - y) };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(data, hits);
            return string.Join(" | ", hits.Take(6).Select(h =>
            {
                var t = h.gameObject.transform;
                string path = t.name;
                for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
                return path + (h.gameObject.GetComponentInParent<Button>() != null ? " [button]" : "");
            }));
        }

        /// <summary>An owned item of this identifier as drawn (k-th one with "k:id").</summary>
        static string WhereItem(string query)
        {
            int pick = 0;
            var m = Regex.Match(query, @"^(\d+):(.*)$");
            if (m.Success) { pick = int.Parse(m.Groups[1].Value); query = m.Groups[2].Value; }
            var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
            if (owned == null) return "none";
            var items = owned.OfType<GameItemElement>().Where(i => i.identifier == query && i.rectTransform != null && i.rectTransform.gameObject.activeInHierarchy).ToList();
            return pick < items.Count ? ScreenRect(items[pick].rectTransform) : "none";
        }
    }
}
