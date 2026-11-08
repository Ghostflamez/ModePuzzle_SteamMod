using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Model;

namespace ModePuzzle.UI
{
    /// <summary>Original web-tool pixel art. Top-left RGBA, no Unity dependencies.</summary>
    public static class TraitIcons
    {
        public const int Side = 9, CellPixels = 16;
        public enum Icon { SideMount, TopMount, NegativeFeedback, Receiver, Premium, Inferior, Overvolted, Degrading, Learning, SupportNode }
        public static readonly Px Ink = Px.Hex("ffe6b0"), Accent = Px.Hex("ffb347");
        static readonly Px Plate = new Px(20, 16, 24, 209);
        // Verbatim PIXEL_ICONS from ModePuzzle/src/config/ui.js; # ink, + accent.
        static readonly string[][] Rows = {
            new[] { "##.......", "##.......", "##...+...", "##..++...", "##.+++++.", "##..++...", "##...+...", "##.......", "##......." },
            new[] { "#########", "#########", ".........", "....+....", "...+++...", "..+++++..", "....+....", "....+....", "....+...." },
            new[] { ".........", "..#...#..", ".###.###.", "..#...#..", ".........", ".+++++++.", ".+++++++.", ".........", "........." },
            new[] { "....+....", "..+...+..", ".+..+..+.", "...+++...", "....#....", "....#....", "...###...", "..#####..", "........." },
            new[] { "....+....", "....+....", "...+++...", "+++++++++", ".+++++++.", "..+++++..", "..++.++..", ".++...++.", ".+.....+." },
            new[] { "....#....", "....#....", "....#....", "....#....", ".#..#..#.", "..#.#.#..", "...###...", "....#....", "........." },
            new[] { ".....++..", "....++...", "...++....", "..+++++..", "....++...", "...++....", "..++.....", ".++......", ".+......." },
            new[] { "#........", "#++......", "#..+.....", "#...++...", "#.....+..", "#......+.", "#.......+", "#........", "#########" },
            new[] { "..#####..", ".#.....#.", "#..+.+..#", "#.+++++.#", "#..+.+..#", ".#.....#.", "..#####..", "...#.#...", "...###..." },
            new[] { "....#....", "...###...", "..##+##..", ".##+++##.", "###+++###", ".##+++##.", "..##+##..", "...###...", "....#...." },
        };
        static readonly string[] EffectIds = {
            "MODULE_EFFECT_SIDE_MOUNT", "MODULE_EFFECT_TOP_MOUNT", "MODULE_EFFECT_NEGATIVE_FEEDBACK",
            "MODULE_EFFECT_RECEIVER", "MODULE_EFFECT_PREMIUM", "MODULE_EFFECT_INFERIOR",
            "MODULE_EFFECT_OVERVOLTED", "MODULE_EFFECT_DEGRADING", "MODULE_EFFECT_LEARNING_ALGORITHM"
        };
        // Hand-reduced variants for the exceptional single-cell/multiple-effect case.
        // Sampling 9 -> 6 would erase central strokes (notably the inferior arrow).
        static readonly string[][] Compact = {
            new[] { "#.....", "#..+..", "#.++..", "#+++++", "#.++..", "#..+.." },
            new[] { "######", "......", "..+...", ".+++..", "+++++.", "..+..." },
            new[] { ".#..#.", "######", ".#..#.", "......", "++++++", "......" },
            new[] { "+.++.+", ".+..+.", "..++..", "..#...", ".###..", "#####." },
            new[] { "..+...", ".+++..", "++++++", ".++++.", ".+..+.", "+....+" },
            new[] { "..#...", "..#...", "..#...", "#.#.#.", ".###..", "..#..." },
            new[] { "...++.", "..++..", ".+++++", "..++..", ".++...", "++...." },
            new[] { "#+....", "#.++..", "#...+.", "#....+", "#.....", "######" },
            new[] { ".####.", "#.+..#", "#+++.#", "#.+..#", ".####.", "..##.." },
            new[] { "..##..", ".#++#.", "##++##", "##++##", ".#++#.", "..##.." },
        };
        // Lock state of a machine, from the web tool's PIXEL_ICONS (locked / unlocked).
        static readonly string[] LockedRows = { "..#####..", ".#.....#.", ".#.....#.", "#########", "#+++++++#", "#+++#+++#", "#+++#+++#", "#+++++++#", "#########" };
        static readonly string[] UnlockedRows = { "..#####..", ".#.....#.", ".#.......", "#########", "#+++++++#", "#+++#+++#", "#+++#+++#", "#+++++++#", "#########" };

        /// <summary>A lock badge (plate, 1px border and the 9x9 icon) stamped at x, y of a top-left RGBA image.</summary>
        public static void StampLock(Px[] dst, int width, int height, int x, int y, bool unlocked)
        {
            var rows = unlocked ? UnlockedRows : LockedRows;
            for (int dy = -1; dy <= Side; dy++)
                for (int dx = -1; dx <= Side; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= width || py >= height) continue;
                    dst[py * width + px] = Over(Plate, dst[py * width + px]);
                }
            for (int dy = 0; dy < Side; dy++)
                for (int dx = 0; dx < Side; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= width || py >= height) continue;
                    char c = rows[dy][dx];
                    if (c == '#') dst[py * width + px] = Ink;
                    else if (c == '+') dst[py * width + px] = Accent;
                }
        }

        public static bool TryEffect(string id, out Icon icon)
        {
            int index = Array.IndexOf(EffectIds, id);
            icon = index < 0 ? default : (Icon)index;
            return index >= 0;
        }
        /// <summary>First two distinct known effects in tag order, then intrinsic node effect.</summary>
        public static Icon[] ForModule(ModuleInfo module)
        {
            var result = new List<Icon>();
            if (module == null) return result.ToArray();
            if (module.Effects != null)
                foreach (var id in module.Effects)
                    if (TryEffect(id, out var icon) && !result.Contains(icon))
                    {
                        result.Add(icon);
                        if (result.Count == 2) break;
                    }
            if (module.IsNode) result.Add(Icon.SupportNode);
            return result.ToArray();
        }
        /// <summary>Original transparent 9x9 bitmap; chip=true uses the web legend colours.</summary>
        public static Px[] Bitmap(Icon icon, bool chip = false)
        {
            var result = new Px[Side * Side];
            var ink = chip ? Px.Hex("241e28") : Ink;
            var accent = chip ? Px.Hex("fff3d6") : Accent;
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    char c = Rows[(int)icon][y][x];
                    if (c == '#') result[y * Side + x] = ink;
                    if (c == '+') result[y * Side + x] = accent;
                }
            return result;
        }
        /// <summary>
        /// After module blit: upright badges in transformed Cells sorted by Y then X.
        /// One full 9x9 icon per cell, inset 2, with a 1px backing border.
        /// If cells are insufficient, use 6x6 icons on 8x8 plates in the first cell
        /// (top-left, top-right, bottom-left). Never paint outside owned cells.
        /// Returns badge count; wholly off-image cells and empty placements are skipped.
        /// </summary>
        public static int Stamp(Px[] dst, int width, int height, PlacedModule placed)
        {
            if (dst == null || width <= 0 || height <= 0 || (long)width * height != dst.Length)
                throw new ArgumentException("Expected a complete top-left RGBA image.");
            var icons = ForModule(placed?.Module);
            if (icons.Length == 0 || placed.Cells == null) return 0;
            var cells = placed.Cells.Where(c => c.X >= 0 && c.Y >= 0 &&
                ((long)c.X + 1) * CellPixels <= width && ((long)c.Y + 1) * CellPixels <= height)
                .GroupBy(c => new { c.X, c.Y }).Select(g => g.First())
                .OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
            if (cells.Length == 0) return 0;
            bool compact = cells.Length < icons.Length;
            for (int i = 0; i < icons.Length; i++)
            {
                var cell = cells[compact ? 0 : i];
                int size = compact ? 6 : Side;
                int x = cell.X * CellPixels + (compact ? i % 2 * 8 + 1 : 2);
                int y = cell.Y * CellPixels + (compact ? i / 2 * 8 + 1 : 2);
                for (int dy = -1; dy <= size; dy++)
                    for (int dx = -1; dx <= size; dx++)
                    {
                        int at = (y + dy) * width + x + dx;
                        dst[at] = Over(Plate, dst[at]);
                    }
                var bitmap = Bitmap(icons[i]);
                for (int dy = 0; dy < size; dy++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        char mark = compact ? Compact[(int)icons[i]][dy][dx] : '.';
                        var pixel = compact ? (mark == '#' ? Ink : mark == '+' ? Accent : default)
                            : bitmap[dy * Side + dx];
                        if (pixel.A != 0) dst[(y + dy) * width + x + dx] = pixel;
                    }
            }
            return icons.Length;
        }
        static Px Over(Px a, Px b)
        {
            int alpha = a.A * 255 + b.A * (255 - a.A);
            if (alpha == 0) return default;
            byte Mix(byte t, byte u) => (byte)((t * a.A * 255 + u * b.A * (255 - a.A) + alpha / 2) / alpha);
            return new Px(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), (byte)((alpha + 127) / 255));
        }
    }
}
