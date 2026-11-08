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

        public static Px Hex(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return new Px((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }

    /// <summary>
    /// Builds the 超科技模组优化器 icon from the 基因扫描仪 sprite the game has loaded: the body is recoloured in
    /// the game's hypertech palette (cream white, gold, pink) and a two-cell module is drawn on its screen.
    /// Pure pixel logic (no Unity types) so it can be previewed outside the game.
    /// </summary>
    public static class IconForge
    {
        /// <summary>Colours of the miniature module on the screen.</summary>
        public class ChipPalette
        {
            public Px[] Board;    // dark to light
            public Px[] Cap;      // dark, mid, light
            public Px[] Pin;      // two alternating pin colours
            public Px[] Copper;   // chip legs
            public Px Chip;
        }

        /// <summary>Scanner body roles, in the order of the scanner's own colours from dark to light.</summary>
        public class Scheme
        {
            public Px Button, Shadow, Main, Light, Bezel;
            public ChipPalette Chip;
            /// <summary>Role of each body colour of the source sprite, darkest first (0 button, 1 shadow, 2 main,
            /// 3 light, 4 bezel). Null: map the darkest colours onto the roles in order.</summary>
            public int[] RankRoles;
        }

        // Colours from the game's hypertech items (超科技能源单元, 超科技涡轮增压器, 超科技储藏区).
        // Frame: cream body, pink screen bezel, gold button. Chip: white board, gold cap and pins.
        public static readonly Scheme Hypertech = new Scheme
        {
            Button = Px.Hex("#d59f75"),
            Shadow = Px.Hex("#88948a"),
            Main = Px.Hex("#c4c4b0"),
            Light = Px.Hex("#d5d2b8"),
            Bezel = Px.Hex("#a272ac"),
            // Deal Maker (谈判专家) body, darkest first: #2f2b3a #373541 #44464c #53585b #5f6868 #88948a.
            RankRoles = new[] { 1, 1, 2, 1, 4, 0 },
            Chip = new ChipPalette
            {
                Board = new[] { Px.Hex("#88948a"), Px.Hex("#a8aea5"), Px.Hex("#c4c4b0"), Px.Hex("#d5d2b8") },
                Cap = new[] { Px.Hex("#ba8953"), Px.Hex("#d59f75"), Px.Hex("#e0d09b") },
                Pin = new[] { Px.Hex("#d59f75"), Px.Hex("#e0d09b") },
                Copper = new[] { Px.Hex("#b26b4e"), Px.Hex("#ba8953") },
                Chip = Px.Hex("#44464c"),
            },
        };

        public static Px[] Build(Px[] scanner, int w, int h) => Build(scanner, w, h, Hypertech);

        public static Px[] Build(Px[] scanner, int w, int h, Scheme scheme)
        {
            var result = Recolor(scanner, scheme);
            var screen = FindScreen(scanner, w, h);
            if (screen != null) DrawMiniModule(result, w, screen.Value, scheme.Chip);
            return result;
        }

        /// <summary>Maps the scanner's body colours, darkest first, onto button, shadow, main, light and
        /// bezel; the outline (darkest colour, also the screen) is kept.</summary>
        static Px[] Recolor(Px[] src, Scheme scheme)
        {
            var colors = Distinct(src);
            if (colors.Count == 0) return (Px[])src.Clone();
            var outline = colors.OrderBy(c => c.Lum).First();
            var body = colors.Where(c => !c.Same(outline)).OrderBy(c => c.Lum).ToList();
            var roles = new[] { scheme.Button, scheme.Shadow, scheme.Main, scheme.Light, scheme.Bezel };
            if (scheme.RankRoles != null && scheme.RankRoles.Length == body.Count)
            {
                var ranked = new Dictionary<int, Px>();
                for (int i = 0; i < body.Count; i++) ranked[Key(body[i])] = roles[scheme.RankRoles[i]];
                return Apply(src, ranked);
            }
            // Fewer colours than roles: skip the button role first, then take roles from the light end.
            int skip = Math.Max(0, roles.Length - body.Count);
            var map = new Dictionary<int, Px>();
            for (int i = 0; i < body.Count; i++) map[Key(body[i])] = roles[Math.Min(roles.Length - 1, i + skip)];

            return Apply(src, map);
        }

        static Px[] Apply(Px[] src, Dictionary<int, Px> map)
        {
            var result = new Px[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (!c.Opaque) continue;
                result[i] = map.TryGetValue(Key(c), out var m) ? new Px(m.R, m.G, m.B, c.A) : c;
            }
            return result;
        }

        public struct Box { public int X, Y, W, H; }

        /// <summary>The screen: the largest connected region of the outline colour.</summary>
        public static Box? FindScreen(Px[] src, int w, int h)
        {
            var colors = Distinct(src);
            if (colors.Count == 0) return null;
            var outline = colors.OrderBy(c => c.Lum).First();
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

        // A two-cell module in miniature, drawn upright (pins left, board, cap right) in local coordinates and
        // turned 90° clockwise onto the screen: pins on top, cap at the bottom, lying horizontally.
        const int MiniW = 11, MiniH = 17;   // MiniW includes the two-pixel fingers; odd MiniH keeps it symmetric

        static void DrawMiniModule(Px[] dst, int w, Box screen, ChipPalette pal)
        {
            // On screen the module is MiniH wide and MiniW tall.
            int ox = screen.X + (screen.W - MiniH) / 2;
            int oy = screen.Y + (screen.H - MiniW) / 2;
            void Put(int x, int y, Px c)
            {
                if (x < 0 || y < 0 || x >= MiniW || y >= MiniH) return;
                int i = (oy + x) * w + ox + (MiniH - 1 - y);
                if (i >= 0 && i < dst.Length) dst[i] = c;
            }
            // Everything but the gold fingers sits one pixel further from the pin edge.
            void Body(int x, int y, Px c) => Put(x + 1, y, c);

            var top = pal.Board.Length - 1;
            for (int y = 0; y < MiniH; y++)
            {
                // Gold fingers, two pixels long, every other row; the gaps between them in the board's dark tone.
                if (y >= 2 && y <= MiniH - 3)
                {
                    var c = y % 2 == 0 ? pal.Pin[(y / 2) % pal.Pin.Length] : pal.Board[0];
                    Put(0, y, c); Put(1, y, c);
                }
                // Board, lit along the top and left and shaded along the right once turned.
                for (int x = 1; x <= 6; x++)
                {
                    var c = pal.Board[1];
                    if (y == MiniH - 1 || x == 1) c = pal.Board[2];
                    if (y == 0) c = pal.Board[0];
                    Body(x, y, c);
                }
                // Cap: mid, light, dark; rounded outer corners.
                Body(7, y, pal.Cap[1]);
                Body(8, y, pal.Cap[2]);
                if (y != 0 && y != MiniH - 1) Body(9, y, pal.Cap[0]);
            }
            // Two chips with copper legs, one per cell.
            foreach (int cy in new[] { 2, MiniH - 5 })
            {
                for (int y = cy; y < cy + 3; y++)
                    for (int x = 3; x <= 5; x++) Body(x, y, pal.Chip);
                Body(2, cy, pal.Copper[0]); Body(2, cy + 2, pal.Copper[0]);
                Body(6, cy + 1, pal.Copper[pal.Copper.Length - 1]);
            }
            // Traces in the lightest board colour.
            // Mirror-symmetric about the middle row.
            for (int y = 6; y <= MiniH - 7; y++) Body(4, y, pal.Board[top]);
            Body(5, MiniH / 2, pal.Board[top]);
            foreach (int y in new[] { 1, MiniH - 2 })
                for (int x = 2; x <= 4; x++) Body(x, y, pal.Board[top]);
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
