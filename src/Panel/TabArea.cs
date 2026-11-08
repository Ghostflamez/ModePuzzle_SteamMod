using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// The lower half: 模组列表 / 属性明细 / 空闲模组 and the selected tab's content, in one container. Switching tabs
    /// clears and refills only that container, keeping the upper half and the scroll position as they are.
    /// </summary>
    static partial class OptimizerPanel
    {
        static RectTransform tabArea;
        static Snapshot tabSnap;
        static MachineInfo tabMachine, tabShown;
        static List<ModuleInfo> tabFits, tabBorrowed;
        static int freeShown = PanelStyle.FreeBatch;

        static void TabContent(CustomUIBuilder b, float inner)
        {
            Snapshot snap = tabSnap; MachineInfo machine = tabMachine, shown = tabShown;
            List<ModuleInfo> fits = tabFits, borrowed = tabBorrowed;
            Row(b, inner);
            Choice.Buttons(b, new List<string> { Loc.T("tab_modules", shown.Installed.Count), Loc.T("opt_details"), Loc.T("tab_free", fits.Count + borrowed.Count) },
                (int)tab, i => SwitchTab((Tab)i));
            EndRow(b);
            if (tab == Tab.Free)
            {
                var lenders = new Dictionary<int, string>();
                foreach (var other in snap.Machines)
                    foreach (var p in other.Installed) lenders[p.Module.UniqueId] = other.Name;
                // Cards are costly to build (several ms each): show them a batch at a time.
                var list = fits.Take(freeShown).ToList();
                CardGrid(b, list, (m, i) => new CardInfo { Values = new[] { m.Efficiency, m.Quality, m.Performance } }, inner);
                if (fits.Count > freeShown)
                {
                    Row(b, inner);
                    b.AddButton(Loc.T("panel_show_more", Mathf.Min(PanelStyle.FreeBatch, fits.Count - freeShown), fits.Count - freeShown),
                        () => { freeShown += PanelStyle.FreeBatch; SwitchTab(Tab.Free); });
                    EndRow(b);
                }
                if (fits.Count == 0) Caption(b, Loc.T("panel_none"), inner);
                if (borrowed.Count > 0)
                {
                    Label(b, Loc.T("tab_borrowed", borrowed.Count), inner);
                    CardGrid(b, borrowed, (m, i) => new CardInfo
                    {
                        Values = new[] { m.Efficiency, m.Quality, m.Performance },
                        Status = Loc.T("card_borrowed", lenders.TryGetValue(m.UniqueId, out var from) ? from : "?"),
                        StatusColor = PanelStyle.StatusBorrowed,
                    }, inner);
                }
            }
            else
            {
                var evaluation = bayShows == BayShows.Result && result?.Feasible == true
                    ? Engine.Scoring.Evaluate(result.Layout, new Engine.Rules()) : EngineBridge.EvaluateCurrent(shown, new Engine.Rules());
                if (tab == Tab.Details) Details(b, shown, evaluation, inner);
                else
                {
                    var rows = evaluation.Rows.ToDictionary(r => r.Item.Id);
                    var modules = shown.Installed.Select(p => p.Module).ToList();
                    bool resultView = bayShows == BayShows.Result && original != null && shown != machine;
                    CardGrid(b, modules, (m, i) =>
                    {
                        var info = new CardInfo { Number = i + 1, Values = rows["u" + m.UniqueId].Total, Locks = bayShows == BayShows.Current };
                        if (resultView)
                        {
                            var was = original.Installed.FirstOrDefault(o => o.Module.UniqueId == m.UniqueId);
                            var now = shown.Installed[i];
                            if (was == null) { info.Status = Loc.T("card_added"); info.StatusColor = PanelStyle.StatusAdded; }
                            else if (was.OriginX != now.OriginX || was.OriginY != now.OriginY || was.Orientation != now.Orientation || was.Flipped != now.Flipped)
                            { info.Status = Loc.T("card_moved"); info.StatusColor = PanelStyle.StatusMoved; }
                        }
                        return info;
                    }, inner);
                    if (modules.Count == 0) Caption(b, Loc.T("panel_none"), inner);
                }
            }

        }

        static void SwitchTab(Tab next)
        {
            tab = next;
            var manager = CustomUIManager.Instance;
            var window = manager?.GetWindow(WindowId);
            if (window == null || !window.IsAlive || tabArea == null || scroll == null) { Refresh(); return; }
            float y = scroll.content.anchoredPosition.y;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Hide before destroying: Destroy waits for the end of the frame and the layout must not count them.
            for (int i = tabArea.childCount - 1; i >= 0; i--)
            {
                var child = tabArea.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            rowWidths.Clear();
            // Measuring needs a visible text: the status line in the upper half.
            if (measureText == null || !measureText.isActiveAndEnabled) measureText = statusElement?.text;
            var b = new CustomUIBuilder(manager, window, tabArea);
            // Built while hidden: no layout or text updates per added element, one pass when shown.
            tabArea.gameObject.SetActive(false);
            TabContent(b, lastInner);
            tabArea.gameObject.SetActive(true);
            long built = clock.ElapsedMilliseconds;
            CustomUIBuilder.RebuildLayout(window.Rect);
            long laid = clock.ElapsedMilliseconds;
            KeepScroll(y);
#if DEV
            ModePuzzleMod.Log?.Log($"Switch tab={tab}: {clock.ElapsedMilliseconds} ms (build {built}, layout {laid - built}), y={y:0} -> {scroll.content.anchoredPosition.y:0}");
#endif
        }

        /// <summary>
        /// Keeps the content where it was: when the new content is too short for that offset, a spacer at the end
        /// of the tab area makes up the difference instead of letting the page jump.
        /// </summary>
        static void KeepScroll(float y)
        {
            float view = scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height;
            float missing = y - (scroll.content.rect.height - view);
            if (missing > 0)
            {
                var pad = new GameObject("scroll pad", typeof(RectTransform), typeof(LayoutElement));
                pad.transform.SetParent(tabArea, false);
                var le = pad.GetComponent<LayoutElement>(); le.minHeight = le.preferredHeight = missing;
                CustomUIBuilder.RebuildLayout(scroll.content);
            }
            var offset = scroll.content.anchoredPosition;
            offset.y = y;
            scroll.content.anchoredPosition = offset;
            scroll.StopMovement();
            scrollOffset = y;
        }
    }
}
