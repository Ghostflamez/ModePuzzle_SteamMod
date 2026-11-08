using System.Collections.Generic;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// 时长 as a drop-down: a button with the current time and a small arrow; clicking it opens the four times right
    /// under it, and clicking a time or anywhere else closes it. The game's TMP drop-down is not usable in the
    /// machinery window style (no arrow, its list fails to open), so this one is built from ordinary buttons.
    /// </summary>
    static partial class OptimizerPanel
    {
        static RectTransform timeButton, timeMenu, timeBlocker;

        static void TimeDropdown(CustomUIBuilder b, float width)
        {
            var window = b.Handle.Rect;
            b.AddButton(Loc.T("opt_seconds", OptimizerTuning.TimeLimitsSeconds[timeIndex]), OpenTimeMenu);
            var button = b.Last;
            button.SetFixedWidth(width); button.SetInteractable(!Busy);
            button.SetTooltip(Loc.T("opt_time_limit"));
            if (button.text != null) button.text.margin = new Vector4(0, 0, PanelStyle.DropArrowSpace, 0);
            timeButton = button.Rect;
            var arrow = new GameObject("arrow", typeof(RectTransform), typeof(Image));
            var ar = (RectTransform)arrow.transform;
            ar.SetParent(timeButton, false);
            arrow.AddComponent<LayoutElement>().ignoreLayout = true;
            ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(1, .5f);
            ar.sizeDelta = new Vector2(PanelStyle.DropArrowWidth, PanelStyle.DropArrowHeight);
            ar.anchoredPosition = new Vector2(-PanelStyle.DropArrowInset, 0);
            var image = arrow.GetComponent<Image>();
            image.sprite = ArrowSprite(false, true); image.raycastTarget = false; image.preserveAspect = true;

            // The blocker covers the window and closes the menu; the menu sits on top of it. Both start hidden.
            timeBlocker = new GameObject("time blocker", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            timeBlocker.SetParent(window, false);
            timeBlocker.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            timeBlocker.anchorMin = Vector2.zero; timeBlocker.anchorMax = Vector2.one; timeBlocker.offsetMin = timeBlocker.offsetMax = Vector2.zero;
            timeBlocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            timeBlocker.GetComponent<Button>().onClick.AddListener(CloseTimeMenu);

            timeMenu = new GameObject("time menu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            timeMenu.SetParent(window, false);
            timeMenu.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            timeMenu.GetComponent<Image>().color = PanelStyle.MenuPlate;
            var group = timeMenu.GetComponent<VerticalLayoutGroup>();
            int pad = (int)PanelStyle.MenuPadding;
            group.padding = new RectOffset(pad, pad, pad, pad); group.spacing = PanelStyle.MenuPadding;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false; group.childControlWidth = true; group.childControlHeight = true;
            var fit = timeMenu.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            timeMenu.pivot = new Vector2(0, 1);
            timeMenu.sizeDelta = new Vector2(width, 0);
            var seconds = OptimizerTuning.TimeLimitsSeconds;
            for (int i = 0; i < seconds.Length; i++)
            {
                int index = i;
                string text = Loc.T("opt_seconds", seconds[i]);
                b.AddButton(i == timeIndex ? $"[ {text} ]" : text, () => { CloseTimeMenu(); if (index != timeIndex) { timeIndex = index; Refresh(); } });
                if (i == timeIndex) b.Last.SetTextColor(PanelStyle.Selected);
                b.Last.Rect.SetParent(timeMenu, false);
            }
            timeBlocker.gameObject.SetActive(false);
            timeMenu.gameObject.SetActive(false);
        }

        static void OpenTimeMenu()
        {
            if (Busy || timeMenu == null || timeButton == null) return;
            var corners = new Vector3[4];
            timeButton.GetWorldCorners(corners);
            timeBlocker.gameObject.SetActive(true);
            timeMenu.gameObject.SetActive(true);
            timeBlocker.SetAsLastSibling(); timeMenu.SetAsLastSibling();
            timeMenu.position = corners[0];   // below the button, left edges aligned
            timeMenu.anchoredPosition += new Vector2(0, -PanelStyle.MenuGap);
            LayoutRebuilder.ForceRebuildLayoutImmediate(timeMenu);
        }

        static void CloseTimeMenu()
        {
            if (timeMenu != null) timeMenu.gameObject.SetActive(false);
            if (timeBlocker != null) timeBlocker.gameObject.SetActive(false);
        }
    }
}
