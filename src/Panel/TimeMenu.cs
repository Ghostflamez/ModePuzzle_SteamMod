using System;
using System.Collections.Generic;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// Drop-downs built from ordinary buttons (the game's TMP drop-down is not usable in the machinery window
    /// style: no arrow, its list fails to open): a button showing the current choice and a small arrow; clicking it
    /// opens the options right under it, and picking one or clicking anywhere else closes the list. Used for 时长
    /// and for the 空闲模组 filters.
    /// </summary>
    static partial class OptimizerPanel
    {
        sealed class Drop
        {
            public RectTransform Button, Menu, Blocker;
            public bool InTabArea;   // destroyed when only the tab area is rebuilt
        }

        static readonly List<Drop> drops = new List<Drop>();
        static readonly HashSet<RectTransform> dropButtons = new HashSet<RectTransform>();
        static Drop timeDrop;

        static void TimeDropdown(CustomUIBuilder b, float width)
        {
            var seconds = OptimizerTuning.TimeLimitsSeconds;
            var options = new List<string>();
            foreach (var s in seconds) options.Add(Loc.T("opt_seconds", s));
            timeDrop = Dropdown(b, options[timeIndex], width, options, timeIndex, i => { if (i != timeIndex) { timeIndex = i; Refresh(); } },
                Loc.T("opt_time_limit"), !Busy, false);
        }

        static Drop Dropdown(CustomUIBuilder b, string label, float width, IList<string> options, int selected, Action<int> pick,
            string tooltip, bool enabled, bool inTabArea)
        {
            var window = b.Handle.Rect;
            var drop = new Drop { InTabArea = inTabArea };
            b.AddButton(label, () => OpenDrop(drop));
            var button = b.Last;
            button.SetFixedWidth(width); button.SetInteractable(enabled);
            if (!string.IsNullOrEmpty(tooltip)) button.SetTooltip(tooltip);
            if (button.text != null) button.text.margin = new Vector4(0, 0, PanelStyle.DropArrowSpace, 0);
            drop.Button = button.Rect;
            dropButtons.Add(drop.Button);
            var arrow = new GameObject("arrow", typeof(RectTransform), typeof(Image));
            var ar = (RectTransform)arrow.transform;
            ar.SetParent(drop.Button, false);
            arrow.AddComponent<LayoutElement>().ignoreLayout = true;
            ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(1, .5f);
            ar.sizeDelta = new Vector2(PanelStyle.DropArrowWidth, PanelStyle.DropArrowHeight);
            ar.anchoredPosition = new Vector2(-PanelStyle.DropArrowInset, 0);
            var image = arrow.GetComponent<Image>();
            image.sprite = ArrowSprite(false, true); image.raycastTarget = false; image.preserveAspect = true;

            // The blocker covers the window and closes the list; the list sits on top of it. Both start hidden.
            drop.Blocker = new GameObject("drop blocker", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            drop.Blocker.SetParent(window, false);
            drop.Blocker.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            drop.Blocker.anchorMin = Vector2.zero; drop.Blocker.anchorMax = Vector2.one; drop.Blocker.offsetMin = drop.Blocker.offsetMax = Vector2.zero;
            drop.Blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            drop.Blocker.GetComponent<Button>().onClick.AddListener(() => CloseDrop(drop));

            drop.Menu = new GameObject("drop menu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            drop.Menu.SetParent(window, false);
            drop.Menu.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            drop.Menu.GetComponent<Image>().color = PanelStyle.MenuPlate;
            var group = drop.Menu.GetComponent<VerticalLayoutGroup>();
            int pad = (int)PanelStyle.MenuPadding;
            group.padding = new RectOffset(pad, pad, pad, pad); group.spacing = PanelStyle.MenuPadding;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false; group.childControlWidth = true; group.childControlHeight = true;
            drop.Menu.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            drop.Menu.pivot = new Vector2(0, 1);
            drop.Menu.sizeDelta = new Vector2(Mathf.Max(width, PanelStyle.DropMenuMinWidth), 0);
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                b.AddButton(i == selected ? $"[ {options[i]} ]" : options[i], () => { CloseDrop(drop); pick(index); });
                if (i == selected) b.Last.SetTextColor(PanelStyle.Selected);
                b.Last.Rect.SetParent(drop.Menu, false);
            }
            drop.Blocker.gameObject.SetActive(false);
            drop.Menu.gameObject.SetActive(false);
            drops.Add(drop);
            return drop;
        }

        static void OpenDrop(Drop drop)
        {
            if (drop?.Menu == null || drop.Button == null) return;
            var corners = new Vector3[4];
            drop.Button.GetWorldCorners(corners);
            drop.Blocker.gameObject.SetActive(true);
            drop.Menu.gameObject.SetActive(true);
            drop.Blocker.SetAsLastSibling(); drop.Menu.SetAsLastSibling();
            // Below the button, left edges aligned; above it when the list would run past the window's bottom.
            drop.Menu.pivot = new Vector2(0, 1);
            drop.Menu.position = corners[0];
            drop.Menu.anchoredPosition += new Vector2(0, -PanelStyle.MenuGap);
            LayoutRebuilder.ForceRebuildLayoutImmediate(drop.Menu);
            var menu = new Vector3[4];
            var frame = new Vector3[4];
            drop.Menu.GetWorldCorners(menu);
            drop.Blocker.parent.GetComponent<RectTransform>().GetWorldCorners(frame);
            if (menu[0].y < frame[0].y)
            {
                drop.Menu.pivot = new Vector2(0, 0);
                drop.Menu.position = corners[1];
                drop.Menu.anchoredPosition += new Vector2(0, PanelStyle.MenuGap);
            }
        }

        static void CloseDrop(Drop drop)
        {
            if (drop?.Menu != null) drop.Menu.gameObject.SetActive(false);
            if (drop?.Blocker != null) drop.Blocker.gameObject.SetActive(false);
        }

        /// <summary>Forgets every drop-down (a new window) or only those of the tab area (it is being rebuilt).</summary>
        static void ForgetDrops(bool tabAreaOnly)
        {
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                if (tabAreaOnly && !d.InTabArea) continue;
                if (d.Menu != null) UnityEngine.Object.Destroy(d.Menu.gameObject);
                if (d.Blocker != null) UnityEngine.Object.Destroy(d.Blocker.gameObject);
                dropButtons.Remove(d.Button);
                drops.RemoveAt(i);
            }
            dropButtons.RemoveWhere(r => r == null);
        }

        static void OpenTimeMenu() { if (!Busy) OpenDrop(timeDrop); }
        static void CloseTimeMenu() => CloseDrop(timeDrop);
    }
}
