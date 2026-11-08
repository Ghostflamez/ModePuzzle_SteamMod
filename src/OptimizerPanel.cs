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
        // Preferred size in canvas units; never wider than half the screen nor taller than 80% of it.
        const float PreferredWidth = 980f, PreferredHeight = 680f, MaxWidthShare = 0.5f, MaxHeightShare = 0.8f;
        // Room taken by the window frame and padding around the content.
        const float FrameMargin = 48f, ColumnGap = 16f;
        const float BayShare = 0.45f;
        /// <summary>Canvas units per cell for the small module pictures in the lists.</summary>
        const float IconCell = 12f;
        const int FreeListMax = 200;

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
            rebuilding = true;
            try { CustomUIManager.Instance?.CloseWindow(WindowId); Build(position); }
            finally { rebuilding = false; }
        }

        static Vector2 WindowSize(CustomUIManager manager)
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            var root = manager.GetLayerRoot(CustomUIManager.LAYER_OVERLAY);
            if (root != null && root.rect.width > 0 && root.rect.height > 0) screen = root.rect.size;
            return new Vector2(Mathf.Min(PreferredWidth, screen.x * MaxWidthShare), Mathf.Min(PreferredHeight, screen.y * MaxHeightShare));
        }

        static void Build(Vector2? position)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var size = WindowSize(manager);
            float inner = size.x - FrameMargin;
            if (SnapshotChanged()) Invalidate();
            var snap = LiveReader.Read();
            var b = manager.CreateWindow(WindowId, Loc.T("panel_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(size.x, size.y)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(OnClosed);
            statusElement = null; messageElement=null; applyElement=null;
            b.BeginScroll(size.y - FrameMargin);

            if (snap.Machines.Count == 0)
            {
                b.AddHeader(Loc.T("panel_header")).AddLabel(Loc.T("panel_no_machines"));
                b.Last.SetFixedWidth(inner);
                b.AddButton(Loc.T("panel_refresh"), Refresh);
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

            b.BeginRow(8f, TextAnchor.MiddleLeft)
                .AddLabel(Loc.T("panel_machine"))
                .AddDropdown(snap.Machines.Select(m => m.Name).ToList(), index, i => { Invalidate(); selectedMachineId = snap.Machines[i].UniqueId; Refresh(); })
                .AddButton(Loc.T("panel_refresh"), Refresh)
                .End();
            b.AddLabel(Summary(machine));
            b.Last.SetFixedWidth(inner);
            if (machine.Guessed) { b.AddWarning(Loc.T("panel_guessed")); b.Last.SetFixedWidth(inner); }

            b.BeginRow(6f, TextAnchor.MiddleLeft);
            ViewButton(b, View.Bay, Loc.T("tab_bay"));
            ViewButton(b, View.Free, Loc.T("tab_free", fits.Count));
            ViewButton(b, View.Result, Loc.T("opt_result"));
            b.End();
            b.AddSeparator();

            Controls(b, inner);
            float listHeight = size.y * OptimizerTuning.ResultHeightShare;
            if (view == View.Bay) BayView(b, machine, inner, listHeight);
            else if (view == View.Free) FreeView(b, snap, fits, inner, listHeight);
            else ResultView(b, inner, listHeight);

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

        static void ViewButton(CustomUIBuilder b, View which, string text)
        {
            b.AddButton(text, () => { if (view != which) { view = which; Refresh(); } });
            if (view == which) b.Last.SetInteractable(false);
        }

        static string Summary(MachineInfo m)
        {
            int used = m.Installed.SelectMany(p => p.Cells).Count(c => m.IsUsable(c.X, c.Y));
            var types = string.Join(Loc.T("panel_list_sep"), m.Accepts.Select(TypeName));
            return Loc.T("panel_summary", m.Width, m.Height, used, m.UsableCount, types);
        }

        static string TypeName(string moduleType)
        {
            var key = "type_" + moduleType;
            var text = Loc.T(key);
            return text == key ? moduleType : text;
        }

        static void BayView(CustomUIBuilder b, MachineInfo machine, float inner, float listHeight)
        {
            // Whole-pixel scale keeps the pixel art crisp.
            int px = ModuleArt.CellPixels;
            float bayArea = inner * BayShare;
            int scale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(bayArea / (machine.Width * px), listHeight / (machine.Height * px))));
            float bayW = machine.Width * px * scale, bayH = machine.Height * px * scale;
            float rightW = Mathf.Max(160f, inner - bayW - ColumnGap);

            Pixels.Destroy(baySprite);
            baySprite = ModuleArt.ComposeBay(machine);
            var totals = EngineBridge.EvaluateCurrent(machine, new Engine.Rules()).Total;

            b.BeginRow(ColumnGap);
            b.BeginColumn(6f).AddHeader(Loc.T("panel_bay_title")).AddImage(baySprite, bayW, bayH);
            b.AddLabel(Loc.T("panel_totals", GameText.Efficiency, GameText.ColoredValue(totals[0]),
                GameText.Quality, GameText.ColoredValue(totals[1]), GameText.Performance, GameText.ColoredValue(totals[2])));
            b.Last.SetFixedWidth(bayW);
            b.End();

            b.BeginColumn(4f).AddHeader(Loc.T("panel_installed", machine.Installed.Count)).BeginScroll(listHeight);
            if (machine.Installed.Count == 0) b.AddLabel(Loc.T("panel_none"));
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var module = machine.Installed[i].Module;
                ModuleRow(b, module, $"{i + 1}. ", rightW);
                locks.TryGetValue(module.UniqueId, out var kind);
                b.AddDropdown(new List<string> { Loc.T("opt_lock_none"), Loc.T("opt_lock_required"), Loc.T("opt_lock_fixed") },
                    module.Stuck ? 2 : (int)kind, value => { locks[module.UniqueId] = (EngineBridge.LockKind)value; Invalidate(); Refresh(); });
                b.Last.SetInteractable(!module.Stuck && session?.Running != true);
            }
            b.End().End();
            b.End();
        }

        static void FreeView(CustomUIBuilder b, Snapshot snap, List<ModuleInfo> fits, float inner, float listHeight)
        {
            b.AddLabel(Loc.T("panel_free", fits.Count, snap.Free.Count));
            b.Last.SetFixedWidth(inner);
            b.BeginScroll(listHeight);
            if (fits.Count == 0) b.AddLabel(Loc.T("panel_none"));
            // Identical modules (same name, stats and effects) are listed once with a count.
            var groups = fits.GroupBy(GameText.Describe).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
            foreach (var g in groups.Take(FreeListMax))
                ModuleRow(b, g.First(), g.Count() > 1 ? $"×{g.Count()}  " : "", inner);
            if (groups.Count > FreeListMax) b.AddLabel(Loc.T("panel_more", groups.Count - FreeListMax));
            b.End();
        }

        /// <summary>Small unrotated module picture, then the name in its card colour and coloured stats.</summary>
        static void ModuleRow(CustomUIBuilder b, ModuleInfo m, string prefix, float width)
        {
            float iconW = Mathf.Max(1, m.ShapeWidth) * IconCell, iconH = Mathf.Max(1, m.ShapeHeight) * IconCell;
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            var sprite = ModuleArt.SpriteOf(m);
            if (sprite != null) b.AddImage(sprite, iconW, iconH);
            var card = ModuleArt.CardColorOf(m);
            b.AddLabel(prefix + GameText.DescribeRich(m, card.HasValue ? ColorUtility.ToHtmlStringRGB(card.Value) : null));
            b.Last.SetFixedWidth(Mathf.Max(80f, width - (sprite != null ? iconW : 0) - 8f));
            b.End();
        }

        static void Finish(CustomUIBuilder b, Vector2? position)
        {
            if (position.HasValue) b.SetPosition(position.Value);
            else b.Center();
            b.Show();
        }
    }
}
