using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// With 自定义 chosen, a tinted area joins the 自定义 button and the rows it opened, like a tab and its page: one
    /// outline with rounded outer corners and rounded inner corners where the tab meets the page. Drawn after
    /// layout, behind the data column's content, at one texture pixel per layout unit.
    /// </summary>
    static partial class OptimizerPanel
    {
        static RectTransform customButton, customBlock, dataColumn;
        static Sprite backdropSprite;

        static LayoutElement bayTopSpacer;
        static RectTransform bayFrame;

        static void ResetCustomBackdrop() { customButton = customBlock = dataColumn = bayFrame = null; bayTopSpacer = null; }

        /// <summary>With the custom target open, moves the bay down so its top edge is level with the 目标 row's.</summary>
        static void AlignBayToTarget(RectTransform window)
        {
            if (bayTopSpacer == null || customButton == null || dataColumn == null || bayFrame == null) return;
            var row = customButton.parent as RectTransform;
            if (row == null) return;
            var w = new Vector3[4];
            row.GetWorldCorners(w);
            float rowTop = dataColumn.InverseTransformPoint(w[1]).y;
            bayFrame.GetWorldCorners(w);
            float bayTop = dataColumn.InverseTransformPoint(w[1]).y;
            bayTopSpacer.minHeight = bayTopSpacer.preferredHeight = Mathf.Max(0, bayTopSpacer.preferredHeight + bayTop - rowTop);
            CustomUIBuilder.RebuildLayout(window);
        }

        static void PlaceCustomBackdrop()
        {
            if (customButton == null || customBlock == null || dataColumn == null) return;
            Rect Local(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = dataColumn.InverseTransformPoint(w[0]), c = dataColumn.InverseTransformPoint(w[2]);
                return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
            }
            float p = PanelStyle.TabPadding;
            var button = Local(customButton);
            var block = Local(customBlock);
            // Tab: the button with padding. Page: from the tab's bottom to below the opened rows, as wide as the data.
            var tab = Rect.MinMaxRect(button.xMin - p, button.yMin - p, button.xMax + p, button.yMax + p);
            var page = Rect.MinMaxRect(block.xMin - p, block.yMin - p, Mathf.Max(block.xMax, button.xMax) + p, tab.yMin);
            float left = Mathf.Min(tab.xMin, page.xMin), right = Mathf.Max(tab.xMax, page.xMax);
            float top = tab.yMax, bottom = page.yMin;
            int w = Mathf.CeilToInt(right - left), h = Mathf.CeilToInt(top - bottom);
            if (w <= 0 || h <= 0 || w > 2048 || h > 2048) return;

            // Shape in texture pixels, top-left origin.
            int tl = Mathf.RoundToInt(tab.xMin - left), tr = Mathf.RoundToInt(tab.xMax - left), tb = Mathf.RoundToInt(top - tab.yMin);
            int pl = Mathf.RoundToInt(page.xMin - left), pr = Mathf.RoundToInt(page.xMax - left);
            int r = PanelStyle.TabRadius;
            bool InRounded(int x, int y, int x0, int y0, int x1, int y1, bool roundTop, bool roundBottom)
            {
                if (x < x0 || x >= x1 || y < y0 || y >= y1) return false;
                int cx = x < x0 + r ? x0 + r : x >= x1 - r ? x1 - r - 1 : x;
                int cy = roundTop && y < y0 + r ? y0 + r : roundBottom && y >= y1 - r ? y1 - r - 1 : y;
                int dx = x - cx, dy = y - cy;
                return dx * dx + dy * dy <= r * r;
            }
            bool Inside(int x, int y)
            {
                if (InRounded(x, y, tl, 0, tr, tb + r, true, false)) return true;       // tab, square at its foot
                if (InRounded(x, y, pl, tb, pr, h, false, true))                         // page
                {
                    // Round the page's top corners that are not under the tab.
                    if (y < tb + r && x < pl + r && pl < tl) { int dx = x - (pl + r), dy = y - (tb + r); if (dx * dx + dy * dy > r * r) return false; }
                    if (y < tb + r && x >= pr - r && pr > tr) { int dx = x - (pr - r - 1), dy = y - (tb + r); if (dx * dx + dy * dy > r * r) return false; }
                    return true;
                }
                // Inner fillets where the tab's sides meet the page's top edge.
                if (y >= tb - r && y < tb)
                {
                    if (x >= tl - r && x < tl && tl - r >= pl) { int dx = x - (tl - r), dy = y - (tb - r); return dx * dx + dy * dy > r * r; }
                    if (x >= tr && x < tr + r && tr + r <= pr) { int dx = x - (tr + r - 1), dy = y - (tb - r); return dx * dx + dy * dy > r * r; }
                }
                return false;
            }
            var px = new Px[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Inside(x, y)) continue;
                    bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                    px[y * w + x] = edge ? PanelStyle.TabEdge : PanelStyle.TabFill;
                }

            var go = new GameObject("custom backdrop", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(dataColumn, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            rect.SetAsFirstSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.localPosition = new Vector3((left + right) / 2, (top + bottom) / 2, 0);
            var image = go.GetComponent<Image>();
            Pixels.Destroy(backdropSprite);
            image.sprite = backdropSprite = Pixels.ToSprite(px, w, h, "modepuzzle_custom_backdrop");
            image.raycastTarget = false;
        }
    }
}
