using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// The optimizer window, built with the game's CustomUI builder. Step 3: read-only view of the live
    /// machines — bay shape, installed modules and the free modules that fit the selected machine.
    /// </summary>
    static class OptimizerPanel
    {
        const string WindowId = "modepuzzle.optimizer.panel";
        const float Width = 780f, Height = 600f;
        const float Cell = 24f, CellGap = 2f, Swatch = 14f;
        const float InstalledListHeight = 230f, FreeListHeight = 150f;
        const int FreeListMax = 200;

        static readonly Color Blocked = new Color(0f, 0f, 0f, 0.15f);
        static readonly Color Empty = new Color(0.22f, 0.2f, 0.27f, 1f);
        static readonly Color[] ModuleColors =
        {
            new Color32(0xd5, 0x9f, 0x75, 255), new Color32(0x67, 0xaa, 0xa5, 255), new Color32(0xa2, 0x72, 0xac, 255),
            new Color32(0xcc, 0xb9, 0x87, 255), new Color32(0x8a, 0xbf, 0xbb, 255), new Color32(0xb2, 0x6b, 0x4e, 255),
            new Color32(0xc0, 0xa0, 0xa4, 255), new Color32(0x45, 0x91, 0x8b, 255), new Color32(0xe0, 0xd0, 0x9b, 255),
            new Color32(0x95, 0x56, 0x99, 255),
        };

        static int selectedMachineId;
        static Sprite white;

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
            var manager = CustomUIManager.Instance;
            var open = manager?.GetWindow(WindowId);
            Vector2? position = open != null && open.IsAlive ? open.Rect.anchoredPosition : (Vector2?)null;
            Close();
            Build(position);
        }

        static void Build(Vector2? position)
        {
            var snap = LiveReader.Read();
            var b = CustomUIManager.Window(WindowId, Loc.T("panel_title"))
                .SetSize(Width, Height)
                .SetDraggable(true)
                .SetCloseOnEscape(true);

            if (snap.Machines.Count == 0)
            {
                b.AddHeader(Loc.T("panel_header")).AddLabel(Loc.T("panel_no_machines"));
                Finish(b, position);
                return;
            }

            int index = snap.Machines.FindIndex(m => m.UniqueId == selectedMachineId);
            if (index < 0) index = 0;
            var machine = snap.Machines[index];
            selectedMachineId = machine.UniqueId;

            b.BeginRow(8f, TextAnchor.MiddleLeft)
                .AddLabel(Loc.T("panel_machine"))
                .AddDropdown(snap.Machines.Select(m => m.Name).ToList(), index, i =>
                {
                    selectedMachineId = snap.Machines[i].UniqueId;
                    Rebuild();
                })
                .AddButton(Loc.T("panel_refresh"), Rebuild)
                .End();
            if (machine.Guessed) b.AddWarning(Loc.T("panel_guessed"));

            var colors = new Dictionary<PlacedModule, Color>();
            for (int i = 0; i < machine.Installed.Count; i++) colors[machine.Installed[i]] = ModuleColors[i % ModuleColors.Length];

            b.BeginRow(16f);
            BayGrid(b, machine, colors);
            InstalledList(b, machine, colors);
            b.End();

            b.AddSeparator();
            FreeList(b, snap, machine);
            if (snap.Skipped.Count > 0)
                b.AddLabel(Loc.T("panel_skipped", string.Join(Loc.T("panel_list_sep"),
                    snap.Skipped.Select(kv => $"{Loc.T("skip_" + kv.Key)} ×{kv.Value}"))));
            Finish(b, position);
        }

        static void BayGrid(CustomUIBuilder b, MachineInfo machine, Dictionary<PlacedModule, Color> colors)
        {
            var owner = new Dictionary<(int, int), PlacedModule>();
            foreach (var p in machine.Installed)
                foreach (var c in p.Cells) owner[(c.X, c.Y)] = p;
            int used = owner.Keys.Count(k => machine.IsUsable(k.Item1, k.Item2));

            b.BeginColumn(4f)
                .AddLabel(Loc.T("panel_bay", used, machine.UsableCount))
                .BeginGrid(machine.Width, Cell, Cell, CellGap);
            for (int y = 0; y < machine.Height; y++)
                for (int x = 0; x < machine.Width; x++)
                {
                    var color = !machine.IsUsable(x, y) ? Blocked
                        : owner.TryGetValue((x, y), out var p) ? colors[p] : Empty;
                    b.AddImage(WhiteSprite(), Cell, Cell);
                    b.Last.SetImageColor(color);
                }
            b.End().End();
        }

        static void InstalledList(CustomUIBuilder b, MachineInfo machine, Dictionary<PlacedModule, Color> colors)
        {
            b.BeginColumn(4f).AddLabel(Loc.T("panel_installed", machine.Installed.Count)).BeginScroll(InstalledListHeight);
            if (machine.Installed.Count == 0) b.AddLabel(Loc.T("panel_none"));
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var p = machine.Installed[i];
                b.BeginRow(6f, TextAnchor.MiddleLeft)
                    .AddImage(WhiteSprite(), Swatch, Swatch);
                b.Last.SetImageColor(colors[p]);
                b.AddLabel($"{i + 1}. {GameText.Describe(p.Module)}").End();
            }
            b.End().End();
        }

        static void FreeList(CustomUIBuilder b, Snapshot snap, MachineInfo machine)
        {
            var fits = snap.Free.Where(machine.CanHold).ToList();
            b.AddLabel(Loc.T("panel_free", fits.Count, snap.Free.Count)).BeginScroll(FreeListHeight);
            if (fits.Count == 0) b.AddLabel(Loc.T("panel_none"));
            foreach (var m in fits.Take(FreeListMax)) b.AddLabel(GameText.Describe(m));
            if (fits.Count > FreeListMax) b.AddLabel(Loc.T("panel_more", fits.Count - FreeListMax));
            b.End();
        }

        static void Finish(CustomUIBuilder b, Vector2? position)
        {
            if (position.HasValue) b.SetPosition(position.Value);
            else b.Center();
            b.Show();
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
