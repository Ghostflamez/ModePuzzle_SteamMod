using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using TMPro;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// 更换机器: a small window with every supported machine, its lock state as a badge in the top-left corner and
    /// the game's own item tooltip on hover. Also the machine lock (锁定本机 / 解锁本机) and the modules that unlocked
    /// machines lend to the others.
    /// </summary>
    static partial class OptimizerPanel
    {
        const string PickerId = "modepuzzle.optimizer.machines";
        static readonly Dictionary<string, Sprite> machineArt = new Dictionary<string, Sprite>();

        static string ArtKey(MachineInfo m) => (m.SpriteFromMod ? "mod:" : "") + m.SpriteAtlas + "/" + m.SpriteName;

        /// <summary>The machine's item sprite from the game, or null.</summary>
        static Sprite MachineSprite(MachineInfo m)
        {
            if (string.IsNullOrEmpty(m.SpriteName)) return null;
            string key = ArtKey(m);
            if (machineArt.TryGetValue(key, out var sprite)) return sprite;
            try
            {
                sprite = m.SpriteFromMod ? CustomUIManager.Instance?.GetModSprite(m.SpriteAtlas, m.SpriteName, true)
                    : RenderHandler.TryLoadFromAtlas(m.SpriteAtlas, m.SpriteName);
            }
            catch (Exception e) { ModePuzzleMod.Log?.Warning($"Machine sprite {key} unreadable: {e.Message}"); sprite = null; }
            machineArt[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// The machine sprite, enlarged by a whole number to the picker's icon box, with a lock badge in its top-left
        /// corner in the trait-badge style. The badge is drawn after enlarging, at a size chosen so it shows the same
        /// on every machine however large its sprite is.
        /// </summary>
        static Sprite BadgedSprite(MachineInfo m, bool unlocked)
        {
            string key = ArtKey(m) + (unlocked ? "#unlocked" : "#locked");
            if (machineArt.TryGetValue(key, out var badged)) return badged;
            var sprite = MachineSprite(m);
            badged = null;
            if (sprite != null)
                try
                {
                    var src = Pixels.Read(sprite, out int w, out int h);
                    int side = Math.Max(w, h), k = Math.Max(1, (int)(PanelStyle.PickerIcon / side));
                    int W = w * k, H = h * k;
                    var px = new Px[W * H];
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++) px[y * W + x] = src[(y / k) * w + x / k];
                    // Shown at PickerIcon / (k * side) of its pixels; one badge pixel should cover LockPixel screen units.
                    float shown = Mathf.Min(1f, PanelStyle.PickerIcon / (k * side));
                    int s = Math.Max(1, Mathf.RoundToInt(PanelStyle.LockPixel / shown));
                    var badge = new Px[(TraitIcons.Side + 2) * (TraitIcons.Side + 2)];
                    TraitIcons.StampLock(badge, TraitIcons.Side + 2, TraitIcons.Side + 2, 1, 1, unlocked);
                    int b0 = PanelStyle.LockInset * s;
                    for (int y = 0; y < (TraitIcons.Side + 2) * s; y++)
                        for (int x = 0; x < (TraitIcons.Side + 2) * s; x++)
                        {
                            int X = b0 + x, Y = b0 + y;
                            if (X >= W || Y >= H) continue;
                            var c = badge[(y / s) * (TraitIcons.Side + 2) + x / s];
                            if (c.A == 255) px[Y * W + X] = c;
                            else if (c.A > 0) px[Y * W + X] = Blend(c, px[Y * W + X]);
                        }
                    badged = Pixels.ToSprite(px, W, H, "modepuzzle_" + key);
                }
                catch (Exception e) { ModePuzzleMod.Log?.Warning($"Machine badge {key}: {e.Message}"); }
            machineArt[key] = badged;
            return badged;
        }

        static Px Blend(Px a, Px b)
        {
            int alpha = a.A * 255 + b.A * (255 - a.A);
            if (alpha == 0) return default;
            byte Mix(byte t, byte u) => (byte)((t * a.A * 255 + u * b.A * (255 - a.A) + alpha / 2) / alpha);
            return new Px(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), (byte)((alpha + 127) / 255));
        }

        /// <summary>Integer pixel scale that fits the sprite into a box (at least 1, fractional only when larger).</summary>
        static float ArtScale(Sprite sprite, float box)
        {
            float side = Mathf.Max(sprite.rect.width, sprite.rect.height);
            return side <= box ? Mathf.Floor(box / side) : box / side;
        }

        /// <summary>Modules installed in other, unlocked machines that this machine can take.</summary>
        static List<ModuleInfo> Borrowable(Snapshot snap, MachineInfo machine) => snap.Machines
            .Where(o => o.UniqueId != machine.UniqueId && MachineLocks.IsUnlocked(o.UniqueId))
            .SelectMany(o => o.Installed.Select(p => p.Module))
            .Where(m => !m.Stuck && machine.CanHold(m)).ToList();

        static void ToggleLock(MachineInfo machine)
        {
            if (Busy) return;
            if (MachineLocks.IsUnlocked(machine.UniqueId))
            {
                MachineLocks.Set(machine.UniqueId, false);
                Invalidate(); Refresh();
                return;
            }
            void Unlock() { MachineLocks.Set(machine.UniqueId, true); Invalidate(); Refresh(); }
            var dialog = GenericConfirmationUIManager.Instance;
            if (dialog == null) { Unlock(); return; }
            // The game's dialog sits below the mod window layer: hide the window while it is up.
            var window = CustomUIManager.Instance?.GetWindow(WindowId);
            void Back() { if (window != null && window.IsAlive) window.Show(); }
            if (window != null && window.IsAlive) window.Hide();
            dialog.OpenPanel(Loc.T("machine_unlock_warning", machine.Name, machine.Installed.Count),
                Loc.T("machine_unlock_confirm"), Loc.T("machine_unlock_cancel"), () => { Back(); Unlock(); }, Back);
        }

        static void LockButton(CustomUIBuilder b, MachineInfo machine, float width)
        {
            bool unlocked = MachineLocks.IsUnlocked(machine.UniqueId);
            Button(b, Loc.T(unlocked ? "machine_lock" : "machine_unlock"), width, () => ToggleLock(machine), !Busy);
            b.Last.SetTooltip(Loc.T(unlocked ? "machine_unlocked_hint" : "machine_locked_hint"));
        }

        /// <summary>Focus a machine (the item used on a machine, or a pick in the machine window).</summary>
        public static void OpenFor(int machineUniqueId)
        {
            ClosePicker();
            if (machineUniqueId != selectedMachineId) { Invalidate(); selectedMachineId = machineUniqueId; }
            if (IsOpen) { Refresh(); var w = CustomUIManager.Instance?.GetWindow(WindowId); w?.BringToFront(); }
            else Open();
        }

        static void ClosePicker()
        {
            var manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(PickerId)) manager.CloseWindow(PickerId);
        }

        static bool PickerOpen
        {
            get { var w = CustomUIManager.Instance?.GetWindow(PickerId); return w != null && w.IsAlive; }
        }

        static void OpenPicker()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            ClosePicker();
            var snap = LiveReader.Read();
            if (snap.Machines.Count == 0) return;
            int columns = Mathf.Min(snap.Machines.Count, PanelStyle.PickerColumns);
            int rows = (snap.Machines.Count + columns - 1) / columns;
            float cellW = PanelStyle.PickerCell, cellH = PanelStyle.PickerCell + PanelStyle.PickerName;
            float width = Mathf.Max(PanelStyle.PickerMinWidth, columns * cellW + (columns - 1) * PanelStyle.PickerGap + PanelStyle.PickerChrome);
            float height = Mathf.Min(rows * cellH + (rows - 1) * PanelStyle.PickerGap + PanelStyle.PickerChromeHeight, PanelStyle.PreferredHeight);
            var b = manager.CreateWindow(PickerId, Loc.T("picker_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(width, height).SetDraggable(true).SetCloseOnEscape(true);
            b.BeginScroll(height - PanelStyle.FrameMargin);
            b.AddLabel(Loc.T("picker_hint"));
            if (b.Last.text != null) { b.Last.text.fontSize = PanelStyle.CaptionFont; b.Last.text.enableAutoSizing = false; b.Last.SetTextColor(PanelStyle.Muted); }
            b.BeginGrid(columns, cellW, cellH, PanelStyle.PickerGap);
            foreach (var m in snap.Machines)
            {
                var machine = m;
                bool unlocked = MachineLocks.IsUnlocked(m.UniqueId);
                b.BeginColumn(2, TextAnchor.UpperCenter);
                var art = BadgedSprite(m, unlocked) ?? MachineSprite(m);
                if (art != null)
                {
                    b.AddIconButton(art, PanelStyle.PickerIcon, () => OpenFor(machine.UniqueId));
                    if (b.Last.image != null) b.Last.image.preserveAspect = true;
                }
                else b.AddButton(m.Name, () => OpenFor(machine.UniqueId));
                var hover = b.Last.root.AddComponent<ItemHoverTooltip>();
                hover.Item = LiveReader.MachineItem(m.UniqueId);
                b.AddLabel(m.Name);
                if (b.Last.text != null)
                {
                    b.Last.text.fontSize = PanelStyle.CaptionFont; b.Last.text.enableAutoSizing = false;
                    b.Last.text.alignment = TextAlignmentOptions.Top; b.Last.text.overflowMode = TextOverflowModes.Ellipsis;
                    b.Last.text.enableWordWrapping = false;
                    if (m.UniqueId == selectedMachineId) b.Last.SetTextColor(PanelStyle.Selected);
                }
                b.Last.SetFixedWidth(cellW);
                b.End();
            }
            b.End().End();
            b.Center();
            b.Show();
        }
    }
}
