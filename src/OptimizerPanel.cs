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
        enum View { Bay, Free, Result }

        static int selectedMachineId;
        static View view = View.Bay;
        static Sprite baySprite;

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
            float inner = size.x - PanelStyle.FrameMargin;
            if (SnapshotChanged()) Invalidate();
            var snap = LiveReader.Read();
            var b = manager.CreateWindow(WindowId, Loc.T("panel_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(size.x, size.y)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(OnClosed);
            statusElement = null; messageElement=null; applyElement=null;
            progressElement = null;
            metricElements.Clear();
            b.BeginScroll(size.y - PanelStyle.FrameMargin);
            scroll = b.CurrentContainer.GetComponentInParent<UnityEngine.UI.ScrollRect>();

            if (snap.Machines.Count == 0)
            {
                b.AddHeader(Loc.T("panel_header")).AddLabel(Loc.T("panel_no_machines"));
                b.Last.SetFixedWidth(inner);
                b.AddButton(Loc.T("panel_refresh"), UserRefresh);
                b.End();
                Finish(b, position);
                PanelWatcher.Stop();
                return;
            }

            int index = snap.Machines.FindIndex(m => m.UniqueId == selectedMachineId);
            if (index < 0) index = 0;
            var machine = snap.Machines[index];
            selectedMachineId = machine.UniqueId;
            var fits = snap.Free.Where(machine.CanHold).ToList();

            b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
            b.AddButton(Loc.T("panel_prev_machine"), () => SelectMachine(snap, index - 1));
            Label(b, $"{machine.Name} ({index + 1}/{snap.Machines.Count})", inner * PanelStyle.MachineNameShare);
            b.AddButton(Loc.T("panel_next_machine"), () => SelectMachine(snap, index + 1));
            b.AddButton(Loc.T("panel_refresh"), UserRefresh);
            if (inner >= PanelStyle.WideBar)
                statusElement = Label(b, Status(), inner * PanelStyle.StatusShare);
            b.End();
            if (inner < PanelStyle.WideBar) statusElement = Label(b, Status(), inner);
            if (machine.Guessed) { b.AddWarning(Loc.T("panel_guessed")); b.Last.SetFixedWidth(inner); }

            b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
            ViewButton(b, View.Bay, Loc.T("tab_bay"));
            ViewButton(b, View.Free, Loc.T("tab_free", fits.Count));
            ViewButton(b, View.Result, Loc.T("opt_result"));
            b.End();
            b.AddSeparator();

            float listHeight = size.y * OptimizerTuning.ResultHeightShare;
            if (view == View.Bay) BayView(b, machine, inner, listHeight);
            Controls(b, inner);
            if (view == View.Free) FreeView(b, snap, fits, inner, listHeight);
            else if (view == View.Result) ResultView(b, inner, listHeight);
            else Details(b, machine, EngineBridge.EvaluateCurrent(machine, new Engine.Rules()), inner);

            if (snap.Skipped.Count > 0)
            {
                b.AddLabel(Loc.T("panel_skipped", string.Join(Loc.T("panel_list_sep"),
                    snap.Skipped.Select(kv => $"{Loc.T("skip_" + kv.Key)} ×{kv.Value}"))));
                b.Last.SetFixedWidth(inner);
            }
            b.End();
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

        static void ViewButton(CustomUIBuilder b, View which, string text)
        {
            b.AddButton(text, () => { if (view != which) { view = which; Refresh(); } });
            if (view == which) b.Last.SetInteractable(false);
        }

        static string TypeName(string moduleType)
        {
            var key = "type_" + moduleType;
            var text = Loc.T(key);
            return text == key ? moduleType : text;
        }

        static void BayView(CustomUIBuilder b, MachineInfo machine, float inner, float listHeight)
        {
            Section(b, "panel_bay_title");
            BayImage(b, machine, inner, listHeight);
            Legend(b, machine, inner, true);
            var totals = EngineBridge.EvaluateCurrent(machine, new Engine.Rules()).Total;
            b.BeginRow(OptimizerTuning.RowGap);
            for (int k = 0; k < Engine.Axis.Count; k++)
                Tile(b, Axes()[k], GameText.ColoredValue(totals[k]), (inner - 2 * OptimizerTuning.RowGap) / 3);
            b.End();
            int used = machine.Installed.SelectMany(p => p.Cells).Count(c => machine.IsUsable(c.X, c.Y));
            Label(b, Loc.T("panel_capacity", machine.Width, machine.Height, used, machine.UsableCount), inner);
            Label(b, Loc.T("panel_accepts", string.Join(Loc.T("panel_list_sep"), machine.Accepts.Select(TypeName))), inner);
        }

        static void FreeView(CustomUIBuilder b, Snapshot snap, List<ModuleInfo> fits, float inner, float listHeight)
        {
            Section(b, "panel_free_title");
            Label(b, Loc.T("panel_free", fits.Count, snap.Free.Count), inner);
            if (fits.Count == 0) b.AddLabel(Loc.T("panel_none"));
            float available = inner - PanelStyle.IconSlot - 5 * OptimizerTuning.RowGap;
            float name = available * PanelStyle.FreeNameShare, stat = available * PanelStyle.FreeStatShare;
            b.BeginRow(OptimizerTuning.RowGap);
            Label(b, "", PanelStyle.IconSlot); Label(b, Loc.T("panel_module"), name);
            foreach (var axis in Axes()) Label(b, axis, stat, true);
            Label(b, Loc.T("panel_traits"), available - name - 3 * stat); b.End();
            foreach (var m in fits.Take(PanelStyle.FreeListMax))
            {
                b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
                ModuleIcon(b, m);
                Name(b, m, name);
                foreach (int value in new[] { m.Efficiency, m.Quality, m.Performance }) Label(b, GameText.ColoredValue(value), stat, true);
                Chips(b, m, available - name - 3 * stat);
                b.End();
            }
            if (fits.Count > PanelStyle.FreeListMax) b.AddLabel(Loc.T("panel_more", fits.Count - PanelStyle.FreeListMax));
        }

        static void Finish(CustomUIBuilder b, Vector2? position)
        {
            if (position.HasValue) b.SetPosition(position.Value);
            else b.Center();
            FitButtons(b.Handle, b.Handle.Rect.rect.width - PanelStyle.FrameMargin);
            builtView = view;
            b.Show();
            CustomUIBuilder.RebuildLayout(b.Handle.Rect);
            if (scroll != null)
            {
                CustomUIBuilder.RebuildLayout(scroll.content);
                float viewportHeight = scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height;
                var offset = scroll.content.anchoredPosition;
                offset.y = Mathf.Clamp(scrollOffsets[(int)view], 0, Mathf.Max(0, scroll.content.rect.height - viewportHeight));
                scroll.content.anchoredPosition = offset;
                scroll.StopMovement();
            }
        }
    }
}
