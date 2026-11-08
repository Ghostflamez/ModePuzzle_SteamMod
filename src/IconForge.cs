using System;
using System.Collections.Generic;
using System.Linq;

namespace ModePuzzle
{
    /// <summary>RGBA pixel; images are row-major with the origin at the top-left.</summary>
    public struct Px
    {
        public byte R, G, B, A;
        public Px(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        public bool Opaque => A >= 128;
        public int Lum => R * 299 + G * 587 + B * 114;
        public bool Same(Px o) => R == o.R && G == o.G && B == o.B;
        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }

    /// <summary>
    /// Builds the 模组优化器 icon from sprites the game has already loaded: the 基因扫描仪 body is recoloured
    /// with the 神经核心模组 palette, and a two-cell neural-core module is drawn on its screen.
    /// Pure pixel logic (no Unity types) so it can be previewed outside the game.
    /// </summary>
    public static class IconForge
    {
        /// <summary>Colour roles sampled from a neural-core module sprite.</summary>
        public class Palette
        {
            public Px Outline = new Px(0x29, 0x22, 0x34);
            public Px[] Board = { new Px(0x27, 0x56, 0x54), new Px(0x2f, 0x75, 0x6f), new Px(0x45, 0x91, 0x8b), new Px(0x67, 0xaa, 0xa5) };
            public Px[] Cap = { new Px(0x5e, 0x2b, 0x45), new Px(0x74, 0x3d, 0x70), new Px(0xa2, 0x72, 0xac) };
            public Px[] Pin = { new Px(0xbb, 0x91, 0x65), new Px(0xcc, 0xb9, 0x87), new Px(0xe0, 0xd0, 0x9b) };
            public Px[] Copper = { new Px(0x95, 0x5c, 0x4b), new Px(0xb2, 0x6b, 0x4e) };
            public Px Chip = new Px(0x37, 0x35, 0x41);
        }

        /// <summary>Sorts the module's colours into roles by hue; roles not found keep their defaults.</summary>
        public static Palette SamplePalette(Px[] module)
        {
            var p = new Palette();
            var colors = Distinct(module);
            if (colors.Count == 0) return p;
            p.Outline = colors.OrderBy(c => c.Lum).First();
            Take(colors.Where(c => c.G > c.R + 15 && c.B > c.R), 2, ref p.Board);
            Take(colors.Where(c => c.R > c.G + 15 && c.B > c.G + 15), 2, ref p.Cap);
            Take(colors.Where(c => c.R > c.G && c.G > c.B && c.G >= 140), 2, ref p.Pin);
            Take(colors.Where(c => c.R > c.G && c.G > c.B && c.G < 140), 1, ref p.Copper);
            var chip = colors.Where(c => !c.Same(p.Outline) && Math.Abs(c.R - c.G) < 20 && Math.Abs(c.B - c.G) < 20 && c.Lum < 80000).ToList();
            if (chip.Count > 0) p.Chip = chip.OrderBy(c => c.Lum).First();
            return p;
        }

        public static Px[] Build(Px[] scanner, int w, int h, Px[] module)
        {
            var pal = SamplePalette(module);
            var outPx = Recolor(scanner, pal);
            var screen = FindScreen(scanner, w, h);
            if (screen != null) DrawMiniModule(outPx, w, screen.Value, pal);
            return outPx;
        }

        /// <summary>Maps the scanner's body colours, darkest to lightest, onto the board ramp; the darkest
        /// (the button) becomes the cap accent when the body has one colour more than the ramp.</summary>
        static Px[] Recolor(Px[] src, Palette pal)
        {
            var colors = Distinct(src);
            var outline = colors.OrderBy(c => c.Lum).First();
            var body = colors.Where(c => !c.Same(outline)).OrderBy(c => c.Lum).ToList();
            var map = new Dictionary<int, Px>();
            int offset = body.Count > pal.Board.Length ? 1 : 0;
            if (offset == 1) map[Key(body[0])] = pal.Cap[0];
            for (int i = offset; i < body.Count; i++)
            {
                int n = body.Count - offset;
                int k = n <= 1 ? pal.Board.Length - 1 : (int)Math.Round((i - offset) * (pal.Board.Length - 1) / (double)(n - 1));
                map[Key(body[i])] = pal.Board[k];
            }
            var result = new Px[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (!c.Opaque) { result[i] = default; continue; }
                result[i] = map.TryGetValue(Key(c), out var m) ? new Px(m.R, m.G, m.B, c.A) : c;
            }
            return result;
        }

        public struct Box { public int X, Y, W, H; }

        /// <summary>The screen: the largest enclosed region of the outline colour, found by flood fill.</summary>
        public static Box? FindScreen(Px[] src, int w, int h)
        {
            var outline = Distinct(src).OrderBy(c => c.Lum).First();
            var seen = new bool[src.Length];
            Box? best = null;
            int bestArea = 0;
            for (int start = 0; start < src.Length; start++)
            {
                if (seen[start] || !src[start].Opaque || !src[start].Same(outline)) continue;
                int minX = w, minY = h, maxX = -1, maxY = -1, area = 0;
                var stack = new Stack<int>();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    area++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    foreach (var j in new[] { i - 1, i + 1, i - w, i + w })
                    {
                        if (j < 0 || j >= src.Length || seen[j]) continue;
                        if ((j == i - 1 || j == i + 1) && j / w != y) continue;
                        if (!src[j].Opaque || !src[j].Same(outline)) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
                if (area > bestArea) { bestArea = area; best = new Box { X = minX, Y = minY, W = maxX - minX + 1, H = maxY - minY + 1 }; }
            }
            return best;
        }

        // A vertical two-cell neural-core module in miniature: pins on the left, board, cap on the right.
        const int MiniW = 10, MiniH = 18;

        static void DrawMiniModule(Px[] dst, int w, Box screen, Palette pal)
        {
            int ox = screen.X + (screen.W - MiniW) / 2;
            int oy = screen.Y + (screen.H - MiniH) / 2;
            void Put(int x, int y, Px c)
            {
                if (x < 0 || y < 0 || x >= MiniW || y >= MiniH) return;
                int i = (oy + y) * w + ox + x;
                if (i >= 0 && i < dst.Length) dst[i] = c;
            }

            for (int y = 0; y < MiniH; y++)
            {
                // Pins: every other row, alternating two golds.
                if (y >= 2 && y <= MiniH - 3 && y % 2 == 0) Put(0, y, pal.Pin[(y / 2) % 2 == 0 ? 1 : 0]);
                // Board, lit along the top-left and shaded along the bottom.
                for (int x = 1; x <= 6; x++)
                {
                    var c = pal.Board[1];
                    if (y == 0 || x == 1) c = pal.Board[2];
                    if (y == MiniH - 1) c = pal.Board[0];
                    Put(x, y, c);
                }
                // Cap: mid, light, dark; rounded outer corners.
                Put(7, y, pal.Cap[1]);
                Put(8, y, pal.Cap[2]);
                if (y != 0 && y != MiniH - 1) Put(9, y, pal.Cap[0]);
            }
            // Two chips with copper legs, one per cell.
            foreach (int cy in new[] { 2, 11 })
            {
                for (int y = cy; y < cy + 3; y++)
                    for (int x = 3; x <= 5; x++) Put(x, y, pal.Chip);
                Put(2, cy, pal.Copper[0]); Put(2, cy + 2, pal.Copper[0]);
                Put(6, cy + 1, pal.Copper[pal.Copper.Length - 1]);
            }
            // Traces in the lightest board colour.
            for (int y = 6; y <= 9; y++) Put(4, y, pal.Board[3]);
            Put(5, 9, pal.Board[3]); Put(2, 15, pal.Board[3]); Put(3, 15, pal.Board[3]); Put(4, 15, pal.Board[3]);
        }

        static void Take(IEnumerable<Px> found, int minCount, ref Px[] slot)
        {
            var list = found.OrderBy(c => c.Lum).ToArray();
            if (list.Length >= minCount) slot = Resample(list, slot.Length);
        }

        /// <summary>Picks n colours spread evenly over a sorted ramp.</summary>
        static Px[] Resample(Px[] ramp, int n)
        {
            if (ramp.Length == n) return ramp;
            var r = new Px[n];
            for (int i = 0; i < n; i++) r[i] = ramp[n == 1 ? 0 : (int)Math.Round(i * (ramp.Length - 1) / (double)(n - 1))];
            return r;
        }

        static List<Px> Distinct(Px[] px)
        {
            var seen = new HashSet<int>();
            var list = new List<Px>();
            foreach (var c in px)
                if (c.Opaque && seen.Add(Key(c))) list.Add(c);
            return list;
        }

        static int Key(Px c) => (c.R << 16) | (c.G << 8) | c.B;
    }
}
