using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// The optimizer window, in the game's machinery window style (CustomUIManager.Style.Machinery: the frame,
    /// title bar and colours of machine windows). Laid out like the external tool's machine view: machine
    /// picker, bay with a legend of installed modules and the current totals, and the free modules that fit.
    /// Read-only for now; optimisation controls come with step 5.
    /// </summary>
    static class OptimizerPanel
    {
        const string WindowId = "modepuzzle.optimizer.panel";
        // Preferred size in canvas units; never wider than half the screen nor taller than 80% of it.
        const float PreferredWidth = 980f, PreferredHeight = 680f, MaxWidthShare = 0.5f, MaxHeightShare = 0.8f;
        const float MinCell = 16f, MaxCell = 40f, CellGap = 2f, Swatch = 14f, BayShare = 0.42f;
        const float ListHeightShare = 0.52f;
        const int FreeListMax = 300;

        static readonly Color Blocked = new Color(0f, 0f, 0f, 0f);
        // Module colours from the game's own palette (hypertech, neural core and machinery accents).
        static readonly Color[] ModuleColors =
        {
            new Color32(0xd5, 0x9f, 0x75, 255), new Color32(0x67, 0xaa, 0xa5, 255), new Color32(0xa2, 0x72, 0xac, 255),
            new Color32(0xcc, 0xb9, 0x87, 255), new Color32(0x8a, 0xbf, 0xbb, 0xff), new Color32(0xb2, 0x6b, 0x4e, 255),
            new Color32(0xc0, 0xa0, 0xa4, 255), new Color32(0x45, 0x91, 0x8b, 255), new Color32(0xe0, 0xd0, 0x9b, 255),
            new Color32(0x95, 0x56, 0x99, 255),
        };

        static int selectedMachineId;
        static Sprite white, cell;

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
            var manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(WindowId)) manager.CloseWindow(WindowId);
        }

        /// <summary>Rebuilds the window from fresh live data, keeping its position.</summary>
        static void Rebuild()
        {
            var open = CustomUIManager.Instance?.GetWindow(WindowId);
            Vector2? position = open != null && open.IsAlive ? open.Rect.anchoredPosition : (Vector2?)null;
            Close();
            Build(position);
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
            var snap = LiveReader.Read();
            var b = manager.CreateWindow(WindowId, Loc.T("panel_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(size.x, size.y)
                .SetDraggable(true)
                .SetCloseOnEscape(true);

            if (snap.Machines.Count == 0)
            {
                b.AddHeader(Loc.T("panel_header")).AddLabel(Loc.T("panel_no_machines")).AddButton(Loc.T("panel_refresh"), Rebuild);
                Finish(b, position);
                return;
            }

            int index = snap.Machines.FindIndex(m => m.UniqueId == selectedMachineId);
            if (index < 0) index = 0;
            var machine = snap.Machines[index];
            selectedMachineId = machine.UniqueId;

            b.BeginRow(8f, TextAnchor.MiddleLeft)
                .AddLabel(Loc.T("panel_machine"))
                .AddDropdown(snap.Machines.Select(m => m.Name).ToList(), index, i => { selectedMachineId = snap.Machines[i].UniqueId; Rebuild(); })
                .AddButton(Loc.T("panel_refresh"), Rebuild)
                .End();
            b.AddLabel(Summary(machine));
            if (machine.Guessed) b.AddWarning(Loc.T("panel_guessed"));
            b.AddSeparator();

            var fits = snap.Free.Where(machine.CanHold).ToList();
            float listHeight = size.y * ListHeightShare;
            b.BeginTabs();
            b.BeginTab(Loc.T("tab_bay"));
            BayTab(b, machine, size, listHeight);
            b.End();
            b.BeginTab(Loc.T("tab_free", fits.Count));
            FreeTab(b, snap, fits, listHeight);
            b.End();
            b.EndTabs();

            if (snap.Skipped.Count > 0)
                b.AddLabel(Loc.T("panel_skipped", string.Join(Loc.T("panel_list_sep"),
                    snap.Skipped.Select(kv => $"{Loc.T("skip_" + kv.Key)} ×{kv.Value}"))));
            Finish(b, position);
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

        static void BayTab(CustomUIBuilder b, MachineInfo machine, Vector2 size, float listHeight)
        {
            var colors = new Dictionary<PlacedModule, Color>();
            for (int i = 0; i < machine.Installed.Count; i++) colors[machine.Installed[i]] = ModuleColors[i % ModuleColors.Length];
            var owner = new Dictionary<(int, int), PlacedModule>();
            foreach (var p in machine.Installed)
                foreach (var c in p.Cells) owner[(c.X, c.Y)] = p;

            float cellSize = Mathf.Clamp(Mathf.Floor(size.x * BayShare / Mathf.Max(machine.Width, machine.Height)) - CellGap, MinCell, MaxCell);
            b.BeginRow(16f);

            b.BeginColumn(6f).AddHeader(Loc.T("panel_bay_title")).BeginGrid(machine.Width, cellSize, cellSize, CellGap);
            for (int y = 0; y < machine.Height; y++)
                for (int x = 0; x < machine.Width; x++)
                {
                    if (!machine.IsUsable(x, y)) { b.AddImage(WhiteSprite(), cellSize, cellSize); b.Last.SetImageColor(Blocked); continue; }
                    if (owner.TryGetValue((x, y), out var p)) { b.AddImage(WhiteSprite(), cellSize, cellSize); b.Last.SetImageColor(colors[p]); }
                    else if (CellSprite() != null) b.AddImage(CellSprite(), cellSize, cellSize);
                    else { b.AddImage(WhiteSprite(), cellSize, cellSize); b.Last.SetImageColor(new Color32(32, 30, 36, 204)); }
                }
            b.End();
            var totals = EngineBridge.EvaluateCurrent(machine, new Engine.Rules()).Total;
            b.AddLabel(Loc.T("panel_totals", GameText.Efficiency, Signed(totals[0]), GameText.Quality, Signed(totals[1]),
                GameText.Performance, Signed(totals[2])));
            b.End();

            b.BeginColumn(4f).AddHeader(Loc.T("panel_installed", machine.Installed.Count)).BeginScroll(listHeight);
            if (machine.Installed.Count == 0) b.AddLabel(Loc.T("panel_none"));
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var p = machine.Installed[i];
                b.BeginRow(6f, TextAnchor.MiddleLeft).AddImage(WhiteSprite(), Swatch, Swatch);
                b.Last.SetImageColor(colors[p]);
                b.AddLabel($"{i + 1}. {GameText.Describe(p.Module)}").End();
            }
            b.End().End();

            b.End();
        }

        static void FreeTab(CustomUIBuilder b, Snapshot snap, List<ModuleInfo> fits, float listHeight)
        {
            b.AddLabel(Loc.T("panel_free", fits.Count, snap.Free.Count)).BeginScroll(listHeight);
            if (fits.Count == 0) b.AddLabel(Loc.T("panel_none"));
            // Identical modules (same name, stats and effects) are listed once with a count.
            var groups = fits.GroupBy(GameText.Describe).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
            foreach (var g in groups.Take(FreeListMax)) b.AddLabel(g.Count() > 1 ? $"×{g.Count()}  {g.Key}" : g.Key);
            if (groups.Count > FreeListMax) b.AddLabel(Loc.T("panel_more", groups.Count - FreeListMax));
            b.End();
        }

        static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        static void Finish(CustomUIBuilder b, Vector2? position)
        {
            if (position.HasValue) b.SetPosition(position.Value);
            else b.Center();
            b.Show();
        }

        /// <summary>The game's own bay cell (Window/machinery "cell"), read at runtime; null if unavailable.</summary>
        static Sprite CellSprite()
        {
            if (cell == null) cell = RenderHandler.TryLoadFromAtlas("Window/machinery", "cell");
            return cell;
        }

        /// <summary>A plain white sprite tinted per cell (made once; no game image involved).</summary>
        static Sprite WhiteSprite()
        {
            if (white != null) return white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            return white;
        }
    }
}
