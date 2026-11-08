using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// The optimizer window, in the game's machinery window style (CustomUIManager.Style.Machinery). Laid out
    /// like the external tool's machine view: machine picker, the bay drawn with the game's own sprites and
    /// the current totals, a legend of installed modules, and the free modules that fit. Rebuilt automatically
    /// when the focused machine changes (PanelWatcher). Search runs on a plain-data worker; apply and preview stay on the main thread.
    /// </summary>
    static partial class OptimizerPanel
    {
        const string WindowId = "modepuzzle.optimizer.panel";
        enum BayShows { Current, Result }
        enum Tab { Modules, Details, Free }

        static int selectedMachineId;
        static BayShows bayShows;
        static Tab tab;
        static int detailAxis = Engine.Axis.Performance;
        static Sprite baySprite;
        static float lastInner;

        public static bool IsOpen
        {
            get
            {
                var w = CustomUIManager.Instance?.GetWindow(WindowId);
                return w != null && w.IsAlive;
            }
        }

        public static void Open()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var open = manager.GetWindow(WindowId);
            if (open != null && open.IsAlive) { open.Show(); open.BringToFront(); return; }
            Build(null);
        }

        public static void Close()
        {
            session?.Stop();
            var manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(WindowId)) manager.CloseWindow(WindowId);
            ClosePicker();
            PanelWatcher.Stop();
        }

        /// <summary>Rebuilds the window from a fresh read of the live game, keeping its position and view.</summary>
        public static void Refresh()
        {
            var open = CustomUIManager.Instance?.GetWindow(WindowId);
            Vector2? position = open != null && open.IsAlive ? open.Rect.anchoredPosition : (Vector2?)null;
            SaveScroll(open);
            rebuilding = true;
            try { CustomUIManager.Instance?.CloseWindow(WindowId); Build(position); }
            catch (System.Exception e) { ModePuzzleMod.Log?.Warning("Panel rebuild failed: " + e); }
            finally { rebuilding = false; }
        }

        static Vector2 WindowSize(CustomUIManager manager)
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            var root = manager.GetLayerRoot(CustomUIManager.LAYER_OVERLAY);
            if (root != null && root.rect.width > 0 && root.rect.height > 0) screen = root.rect.size;
            return new Vector2(Mathf.Min(PanelStyle.PreferredWidth, screen.x * PanelStyle.MaxWidthShare), Mathf.Min(PanelStyle.PreferredHeight, screen.y * PanelStyle.MaxHeightShare));
        }

        static void Build(Vector2? position)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var size = WindowSize(manager);
            if (SnapshotChanged()) Invalidate();
            var snap = LiveReader.Read();
            var b = manager.CreateWindow(WindowId, Loc.T("panel_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(size.x, size.y)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(OnClosed);
            rowWidths.Clear(); measureText = null; ResetCustomBackdrop();
            statusElement = null; messageElement=null; applyElement=null;
            progressElement = null; elapsedElement = null;
            metricElements.Clear();
            b.BeginScroll(size.y - PanelStyle.FrameMargin);
            scroll = b.CurrentContainer.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            b.Show();
            CustomUIBuilder.RebuildLayout(b.Handle.Rect);
            Canvas.ForceUpdateCanvases();
            float inner = ContentWidth(size.x);
            lastInner = inner;
            Column(b, inner);

            if (snap.Machines.Count == 0)
            {
                b.AddHeader(Loc.T("panel_header"));
                Label(b, Loc.T("panel_no_machines"), inner);
                Row(b, inner); b.AddButton(Loc.T("panel_refresh"), UserRefresh); EndRow(b);
                b.End().End();
                Finish(b, position);
                PanelWatcher.Stop();
                return;
            }

            int index = snap.Machines.FindIndex(m => m.UniqueId == selectedMachineId);
            if (index < 0) index = 0;
            var machine = snap.Machines[index];
            selectedMachineId = machine.UniqueId;
            var fits = snap.Free.Where(machine.CanHold).ToList();
            var borrowed = Borrowable(snap, machine);

            MachineBar(b, snap, machine, index, inner);
            var shown = Dashboard(b, machine, inner);
            Separator(b, inner);
            Row(b, inner);
            Choice.Buttons(b, new List<string> { Loc.T("tab_modules", shown.Installed.Count), Loc.T("opt_details"), Loc.T("tab_free", fits.Count + borrowed.Count) },
                (int)tab, i => { tab = (Tab)i; Refresh(); });
            EndRow(b);
            if (tab == Tab.Free)
            {
                var lenders = new Dictionary<int, string>();
                foreach (var other in snap.Machines)
                    foreach (var p in other.Installed) lenders[p.Module.UniqueId] = other.Name;
                var list = fits.Take(PanelStyle.FreeListMax).ToList();
                CardGrid(b, list, (m, i) => new CardInfo { Values = new[] { m.Efficiency, m.Quality, m.Performance } }, inner);
                if (fits.Count > PanelStyle.FreeListMax) Caption(b, Loc.T("panel_more", fits.Count - PanelStyle.FreeListMax), inner);
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

            if (snap.Skipped.Count > 0)
            {
                Label(b, Loc.T("panel_skipped", string.Join(Loc.T("panel_list_sep"),
                    snap.Skipped.Select(kv => $"{Loc.T("skip_" + kv.Key)} ×{kv.Value}"))), inner);
            }
            b.End().End();
            Finish(b, position);
            PanelWatcher.Watch(machine.UniqueId);
        }

        /// <summary>Previous / next machine, wrapping around; re-reads everything.</summary>
        static void SelectMachine(Snapshot snap, int index)
        {
            int count = snap.Machines.Count;
            if (count == 0) return;
            Invalidate();
            selectedMachineId = snap.Machines[((index % count) + count) % count].UniqueId;
            Refresh();
        }

        static string TypeName(string moduleType)
        {
            var key = "type_" + moduleType;
            var text = Loc.T(key);
            return text == key ? moduleType : text;
        }

        static void Finish(CustomUIBuilder b, Vector2? position)
        {
            if (position.HasValue) b.SetPosition(position.Value);
            else b.Center();
            builtTab = tab;
            b.Show();
            CustomUIBuilder.RebuildLayout(b.Handle.Rect);
            if (scroll != null)
            {
                CustomUIBuilder.RebuildLayout(scroll.content);
                float viewportHeight = scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height;
                var offset = scroll.content.anchoredPosition;
                offset.y = Mathf.Clamp(scrollOffsets[(int)tab], 0, Mathf.Max(0, scroll.content.rect.height - viewportHeight));
                scroll.content.anchoredPosition = offset;
                scroll.StopMovement();
            }
            AlignBayToTarget(b.Handle.Rect);
            PlaceCustomBackdrop();
        }
    }
}
