using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// 更换机器: a small window with every supported machine drawn as it looks right now, a clickable lock badge in
    /// each corner and the game's own item tooltip on hover. Also the machine lock (锁定本机 / 解锁本机, with its
    /// confirmation) and the modules that unlocked machines lend to the others.
    /// </summary>
    static partial class OptimizerPanel
    {
        const string PickerId = "modepuzzle.optimizer.machines", ConfirmId = "modepuzzle.optimizer.unlock";
        static readonly Dictionary<string, Sprite> machineArt = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> liveArt = new Dictionary<string, Sprite>();

        static int Fingerprint(Px[] px)
        {
            unchecked
            {
                int h = 17;
                foreach (var p in px) h = h * 31 + ((p.R << 24) | (p.G << 16) | (p.B << 8) | p.A);
                return h;
            }
        }
        static Sprite lockedBadge, unlockedBadge;

        static string ArtKey(MachineInfo m) => (m.SpriteFromMod ? "mod:" : "") + m.SpriteAtlas + "/" + m.SpriteName;

        /// <summary>The machine's base item sprite from the game, or null (fallback when it cannot be read live).</summary>
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
        /// The machine as the game draws it now (state sprites, rotation and flip included), read again on every
        /// call; the base sprite when that fails.
        /// </summary>
        static Sprite LiveSprite(MachineInfo m)
        {
            try
            {
                var px = LiveItemArt.Capture(LiveReader.MachineItem(m.UniqueId) as GameItemElement, out int w, out int h);
                if (px != null)
                {
                    // Reuse the sprite while the picture is unchanged. A replaced one is not destroyed: another window
                    // (the panel's first row, the picker) may still show it.
                    string key = m.UniqueId + ":" + w + "x" + h + ":" + Fingerprint(px);
                    if (liveArt.TryGetValue(key, out var same) && same != null) return same;
                    var sprite = Pixels.ToSprite(px, w, h, "modepuzzle_machine_" + m.UniqueId);
                    liveArt[key] = sprite;
                    return sprite;
                }
            }
            catch (Exception e) { ModePuzzleMod.Log?.Warning($"Machine {m.UniqueId} live sprite: {e.Message}"); }
            return MachineSprite(m);
        }

        // Original 9x9 pixel art: # outline, + body, o keyhole. Locked: shackle closed into the body. Unlocked: shackle
        // lifted with its left leg out of the body.
        static readonly string[] LockedArt = { "..#####..", ".#.....#.", ".#.....#.", ".#.....#.", "#########", "#+++++++#", "#+++o+++#", "#+++o+++#", "#########" };
        static readonly string[] UnlockedArt = { "...#####.", "..#.....#", "..#.....#", "........#", "#########", "#+++++++#", "#+++o+++#", "#+++o+++#", "#########" };

        /// <summary>The lock badge: the icon on a dark plate with a 1px border, red when locked, green when unlocked.</summary>
        static Sprite Badge(bool unlocked)
        {
            ref Sprite slot = ref unlocked ? ref unlockedBadge : ref lockedBadge;
            if (slot != null) return slot;
            var art = unlocked ? UnlockedArt : LockedArt;
            Px plate = PanelStyle.LockPlate, ink = unlocked ? PanelStyle.UnlockedInk : PanelStyle.LockedInk,
                body = unlocked ? PanelStyle.UnlockedBody : PanelStyle.LockedBody, hole = PanelStyle.LockPlate;
            int side = art.Length + 2;
            var px = new Px[side * side];
            for (int i = 0; i < px.Length; i++) px[i] = plate;
            for (int y = 0; y < art.Length; y++)
                for (int x = 0; x < art[y].Length; x++)
                {
                    char c = art[y][x];
                    if (c == '#') px[(y + 1) * side + x + 1] = ink;
                    else if (c == '+') px[(y + 1) * side + x + 1] = body;
                    else if (c == 'o') px[(y + 1) * side + x + 1] = hole;
                }
            slot = Pixels.ToSprite(px, side, side, unlocked ? "modepuzzle_unlocked" : "modepuzzle_locked");
            return slot;
        }

        /// <summary>Integer pixel scale that fits the sprite into a box (at least 1, fractional only when larger).</summary>
        static float ArtScale(Sprite sprite, float box)
        {
            float side = Mathf.Max(sprite.rect.width, sprite.rect.height);
            return side <= box ? Mathf.Floor(box / side) : box / side;
        }

        /// <summary>
        /// The machine's live picture in a square box (clickable when <paramref name="click"/> is set) with its lock
        /// badge over the picture's top-left corner. The badge is a separate button of a fixed size, so it looks the
        /// same on every machine, and clicking it locks or unlocks the machine.
        /// </summary>
        static void MachineArt(CustomUIBuilder b, MachineInfo m, float box, Action click, bool badged = true)
        {
            var sprite = LiveSprite(m);
            if (sprite == null) { Label(b, "", box); return; }
            float scale = ArtScale(sprite, box);
            float w = Mathf.Round(sprite.rect.width * scale), h = Mathf.Round(sprite.rect.height * scale);
            if (click != null) b.AddIconButton(sprite, box, click);
            else b.AddImage(sprite, box, box);
            var art = b.Last;
            if (art.image != null) art.image.preserveAspect = true;
            art.root.AddComponent<ItemHoverTooltip>().Item = LiveReader.MachineItem(m.UniqueId);
            if (!badged) return;

            bool unlocked = MachineLocks.IsUnlocked(m.UniqueId);
            float badge = (LockedArt.Length + 2) * PanelStyle.LockScale;
            b.AddIconButton(Badge(unlocked), badge, () => ToggleLock(m));
            var rect = b.Last.Rect;
            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            rect.SetParent(art.Rect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(badge, badge);
            rect.anchoredPosition = new Vector2(Mathf.Floor((box - w) / 2) - PanelStyle.LockOutset, -Mathf.Floor((box - h) / 2) + PanelStyle.LockOutset);
            b.Last.SetTooltip(Loc.T(unlocked ? "machine_unlocked_hint" : "machine_locked_hint"));
        }

        /// <summary>Modules installed in other, unlocked machines that this machine can take.</summary>
        static List<ModuleInfo> Borrowable(Snapshot snap, MachineInfo machine) => snap.Machines
            .Where(o => o.UniqueId != machine.UniqueId && MachineLocks.IsUnlocked(o.UniqueId))
            .SelectMany(o => o.Installed.Select(p => p.Module))
            .Where(m => !Pinned(m) && machine.CanHold(m)).ToList();

        /// <summary>Locking needs no confirmation (as in the web tool); unlocking asks unless 不再提示 was ticked.</summary>
        static void ToggleLock(MachineInfo machine)
        {
            if (Busy) return;
            if (MachineLocks.IsUnlocked(machine.UniqueId)) { SetLock(machine, false); return; }
            if (MachineLocks.SkipConfirmation) { SetLock(machine, true); return; }
            ConfirmUnlock(machine);
        }

        static void SetLock(MachineInfo machine, bool unlocked)
        {
            MachineLocks.Set(machine.UniqueId, unlocked);
            Invalidate();
            if (IsOpen) Refresh();
            if (PickerOpen) OpenPicker();
        }

        /// <summary>
        /// The unlock warning as a small mod window: the game's own confirmation has no room for a checkbox and is
        /// drawn below the mod window layer.
        /// </summary>
        static void ConfirmUnlock(MachineInfo machine)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            if (manager.IsOpen(ConfirmId)) manager.CloseWindow(ConfirmId);
            bool skip = false;
            float width = PanelStyle.ConfirmWidth, inner = width - PanelStyle.ChromeFallback;
            var b = manager.CreateWindow(ConfirmId, Loc.T("machine_unlock_title", machine.Name), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(width, PanelStyle.ConfirmHeight).SetDraggable(true).SetCloseOnEscape(true);
            TooltipShield.Register(b.Handle.Rect);
            b.BeginColumn(OptimizerTuning.RowGap, TextAnchor.UpperLeft);
            var text = Label(b, Loc.T("machine_unlock_warning", machine.Name, machine.Installed.Count), inner);
            text.text.overflowMode = TextOverflowModes.Overflow;
            b.AddToggle(Loc.T("machine_unlock_skip"), false, v => skip = v);
            b.Last.SetFixedWidth(inner);
            if (b.Last.text != null) { b.Last.text.fontSize = PanelStyle.BodyFont; b.Last.text.enableAutoSizing = false; }
            void Close() { if (manager.IsOpen(ConfirmId)) manager.CloseWindow(ConfirmId); }
            Row(b, inner);
            b.AddButton(Loc.T("machine_unlock_confirm"), () => { if (skip) MachineLocks.SkipConfirmation = true; Close(); SetLock(machine, true); });
            b.AddButton(Loc.T("machine_unlock_cancel"), Close);
            EndRow(b);
            b.End();
            b.Center();
            b.Show();
            manager.GetWindow(ConfirmId)?.BringToFront();
        }

        /// <summary>An ordinary button showing the lock / unlock icon instead of a word.</summary>
        static void LockButton(CustomUIBuilder b, MachineInfo machine, float width)
        {
            bool unlocked = MachineLocks.IsUnlocked(machine.UniqueId);
            Button(b, " ", width, () => ToggleLock(machine), !Busy);
            b.Last.SetTooltip(Loc.T(unlocked ? "machine_unlocked_hint" : "machine_locked_hint"));
            var icon = new GameObject("lock", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)icon.transform;
            rect.SetParent(b.Last.Rect, false);
            var layout = icon.AddComponent<LayoutElement>(); layout.ignoreLayout = true;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            float side = (LockedArt.Length + 2) * PanelStyle.LockScale;
            rect.sizeDelta = new Vector2(side, side);
            var image = icon.GetComponent<Image>();
            image.sprite = Badge(unlocked); image.raycastTarget = false; image.preserveAspect = true;
            if (Busy) image.color = new Color(1, 1, 1, .5f);
        }

        /// <summary>Focus a machine (the item used on a machine, or a pick in the machine window).</summary>
        public static void OpenFor(int machineUniqueId)
        {
            ClosePicker();
            if (machineUniqueId != selectedMachineId) { Invalidate(); selectedMachineId = machineUniqueId; }
            if (IsOpen) { Refresh(); CustomUIManager.Instance?.GetWindow(WindowId)?.BringToFront(); }
            else Open();
        }

        static void ClosePicker()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            if (manager.IsOpen(PickerId)) manager.CloseWindow(PickerId);
            if (manager.IsOpen(ConfirmId)) manager.CloseWindow(ConfirmId);
        }

        static bool PickerOpen
        {
            get { var w = CustomUIManager.Instance?.GetWindow(PickerId); return w != null && w.IsAlive; }
        }

        static void OpenPicker()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var old = manager.GetWindow(PickerId);
            Vector2? position = old != null && old.IsAlive ? old.Rect.anchoredPosition : (Vector2?)null;
            if (manager.IsOpen(PickerId)) manager.CloseWindow(PickerId);
            var snap = LiveReader.Read();
            if (snap.Machines.Count == 0) return;
            int columns = Mathf.Min(snap.Machines.Count, PanelStyle.PickerColumns);
            int rows = (snap.Machines.Count + columns - 1) / columns;
            float cellW = PanelStyle.PickerCell, cellH = PanelStyle.PickerIcon + PanelStyle.PickerName;
            float width = Mathf.Max(PanelStyle.PickerMinWidth, columns * cellW + (columns - 1) * PanelStyle.PickerGap + PanelStyle.PickerChrome);
            float height = Mathf.Min(rows * cellH + (rows - 1) * PanelStyle.PickerGap + PanelStyle.PickerChromeHeight, PanelStyle.PreferredHeight);
            var b = manager.CreateWindow(PickerId, Loc.T("picker_title"), CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery)
                .SetSize(width, height).SetDraggable(true).SetCloseOnEscape(true);
            TooltipShield.Register(b.Handle.Rect);
            b.BeginScroll(height - PanelStyle.FrameMargin);
            b.AddLabel(Loc.T("picker_hint"));
            if (b.Last.text != null) { b.Last.text.fontSize = PanelStyle.CaptionFont; b.Last.text.enableAutoSizing = false; b.Last.SetTextColor(PanelStyle.Muted); }
            b.BeginGrid(columns, cellW, cellH, PanelStyle.PickerGap);
            foreach (var m in snap.Machines)
            {
                var machine = m;
                b.BeginColumn(2, TextAnchor.UpperCenter);
                MachineArt(b, m, PanelStyle.PickerIcon, () => OpenFor(machine.UniqueId));
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
            if (position.HasValue) b.SetPosition(position.Value); else b.Center();
            b.Show();
        }
    }
}
