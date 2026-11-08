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
        static float fitWidth, choiceRowWidth;
        static bool refitting;

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
            float width = Mathf.Max(PanelStyle.PreferredWidth, fitWidth);
            return new Vector2(Mathf.Min(width, screen.x * PanelStyle.MaxWidthShare), Mathf.Min(PanelStyle.PreferredHeight, screen.y * PanelStyle.MaxHeightShare));
        }

        static void Build(Vector2? position)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var size = WindowSize(manager);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var marks = new System.Text.StringBuilder();
            void Mark(string what) { marks.Append($" {what}={clock.ElapsedMilliseconds}"); }
            if (SnapshotChanged()) Invalidate();
            var snap = LiveReader.Read();
            Mark("read");
            var b = manager.CreateWindow(WindowId, Loc.T("panel_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(size.x, size.y)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(OnClosed);
            rowWidths.Clear(); measureText = null; ResetCustomBackdrop(); ForgetDrops(false);
            statusElement = null; messageElement=null; applyElement=null;
            progressElement = null; elapsedElement = null;
            metricElements.Clear();
            TooltipShield.Register(b.Handle.Rect);
            b.BeginScroll(size.y - PanelStyle.FrameMargin);
            scroll = b.CurrentContainer.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            b.Show();
            CustomUIBuilder.RebuildLayout(b.Handle.Rect);
            Canvas.ForceUpdateCanvases();
            float inner = ContentWidth(size.x);
            lastInner = inner;
            Mark("window");
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
            var fits = snap.Free.Where(m => machine.CanHold(m) && !IsBroken(m)).ToList();
            var borrowed = Borrowable(snap, machine);

            MachineBar(b, snap, machine, index, inner);
            Mark("bar");
            var shown = Dashboard(b, machine, inner);
            Mark("dashboard");
            Separator(b, inner);
            // The tabs and their content live in one container so switching tabs rebuilds only that part.
            Column(b, inner);
            tabArea = b.CurrentContainer;
            Mark("tabs");
            tabSnap = snap; tabMachine = machine; tabShown = shown; tabFits = fits; tabBorrowed = borrowed;
            TabContent(b, inner);
            b.End();
            if (snap.Skipped.Count > 0)
            {
                Label(b, Loc.T("panel_skipped", string.Join(Loc.T("panel_list_sep"),
                    snap.Skipped.Select(kv => $"{Loc.T("skip_" + kv.Key)} ×{kv.Value}"))), inner);
            }
            b.End().End();
            Mark("content");
            Finish(b, position);
            Mark("finish");
#if DEV
            ModePuzzleMod.Log?.Log($"Build tab={tab}:{marks}");
#endif
            PanelWatcher.Watch(machine.UniqueId);
            // The choice rows decide the data column's width; in a language where they are longer the window
            // widens once (to what a standard 7x5 bay at 3x leaves for them) and is built again.
            float wanted = Mathf.Ceil(PanelStyle.StdBayWidth + PanelStyle.ColumnGap + choiceRowWidth + (size.x - inner));
            if (!refitting && Mathf.Abs(Mathf.Max(PanelStyle.PreferredWidth, wanted) - Mathf.Max(PanelStyle.PreferredWidth, fitWidth)) > 2)
            {
                fitWidth = wanted;
                refitting = true;
                try { Refresh(); } finally { refitting = false; }
            }
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
                offset.y = Mathf.Clamp(scrollOffset, 0, Mathf.Max(0, scroll.content.rect.height - viewportHeight));
                scroll.content.anchoredPosition = offset;
                scroll.StopMovement();
            }
            AlignBayToTarget(b.Handle.Rect);
            PlaceCustomBackdrop();
        }
    }
}
