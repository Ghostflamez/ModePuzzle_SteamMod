using System;
using System.Collections.Generic;
using System.Linq;

namespace ModePuzzle.Engine
{
    /// <summary>128-bit cell set (boards up to 10 × 10; the game's bays are at most 7 × 5).</summary>
    public struct Bits
    {
        public ulong Lo, Hi;

        public static Bits Cell(int i) => i < 64 ? new Bits { Lo = 1UL << i } : new Bits { Hi = 1UL << (i - 64) };
        public static Bits operator |(Bits a, Bits b) => new Bits { Lo = a.Lo | b.Lo, Hi = a.Hi | b.Hi };
        public static Bits operator &(Bits a, Bits b) => new Bits { Lo = a.Lo & b.Lo, Hi = a.Hi & b.Hi };
        public static Bits operator ~(Bits a) => new Bits { Lo = ~a.Lo, Hi = ~a.Hi };
        public bool Any => (Lo | Hi) != 0;
        public bool Equals(Bits o) => Lo == o.Lo && Hi == o.Hi;
    }

    public sealed class Placement
    {
        public int Rotation;
        public bool Flipped;
        public int Index;
        /// <summary>Board cell indexes (y * w + x).</summary>
        public int[] Cells;
        public Bits Mask, Halo;
        public int X, Y;
        /// <summary>Touches the bay's leftmost usable column / topmost usable row.</summary>
        public bool Left, Top;
        /// <summary>Own value per axis (base plus mounts), filled by the search.</summary>
        public int[] U;
    }

    /// <summary>Shapes and placements, as the external tool's engine.js.</summary>
    public static class Geometry
    {
        static readonly int[][] Neighbours = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        public static Point[] Normalize(IEnumerable<Point> cells)
        {
            var list = cells.ToList();
            int mx = list.Min(c => c.X), my = list.Min(c => c.Y);
            return list.Select(c => new Point(c.X - mx, c.Y - my)).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        }

        static string Key(Point[] cells) => string.Join(";", cells.Select(c => c.X + "," + c.Y));

        public struct Orientation
        {
            public Point[] Cells;
            public int Rotation;
            public bool Flipped;
        }

        /// <summary>Distinct orientations: flip, then quarter turns, in the engine's order.</summary>
        public static List<Orientation> Orientations(Point[] shape)
        {
            var seen = new HashSet<string>();
            var result = new List<Orientation>();
            for (int flip = 0; flip < 2; flip++)
                for (int r = 0; r < 4; r++)
                {
                    var oriented = Normalize(shape.Select(c =>
                    {
                        int x = flip == 1 ? -c.X : c.X, y = c.Y;
                        for (int i = 0; i < r; i++) { int t = x; x = -y; y = t; }
                        return new Point(x, y);
                    }));
                    if (seen.Add(Key(oriented))) result.Add(new Orientation { Cells = oriented, Rotation = r * 90, Flipped = flip == 1 });
                }
            return result;
        }

        public static bool Connected(Point[] cells)
        {
            if (cells.Length == 0) return false;
            var keys = new HashSet<(int, int)>(cells.Select(c => (c.X, c.Y)));
            if (keys.Count != cells.Length) return false;
            var seen = new HashSet<(int, int)>();
            var todo = new Stack<(int, int)>();
            todo.Push((cells[0].X, cells[0].Y));
            while (todo.Count > 0)
            {
                var (x, y) = todo.Pop();
                if (!seen.Add((x, y))) continue;
                foreach (var n in Neighbours)
                {
                    var next = (x + n[0], y + n[1]);
                    if (keys.Contains(next) && !seen.Contains(next)) todo.Push(next);
                }
            }
            return seen.Count == cells.Length;
        }

        public static List<Placement> GeneratePlacements(Board board, Item item)
        {
            var result = new List<Placement>();
            int minX = int.MaxValue, minY = int.MaxValue;
            for (int i = 0; i < board.Cells.Length; i++)
                if (board.Cells[i]) { minX = Math.Min(minX, i % board.W); minY = Math.Min(minY, i / board.W); }

            foreach (var o in Orientations(item.Shape))
            {
                int mw = o.Cells.Max(c => c.X) + 1, mh = o.Cells.Max(c => c.Y) + 1;
                for (int y = 0; y <= board.H - mh; y++)
                    for (int x = 0; x <= board.W - mw; x++)
                    {
                        bool ok = true;
                        foreach (var c in o.Cells) if (!board.Available(x + c.X, y + c.Y)) { ok = false; break; }
                        if (!ok) continue;
                        var p = new Placement { Rotation = o.Rotation, Flipped = o.Flipped, Index = result.Count, X = x, Y = y,
                            Cells = new int[o.Cells.Length] };
                        Bits mask = default, halo = default;
                        for (int i = 0; i < o.Cells.Length; i++)
                        {
                            int cx = x + o.Cells[i].X, cy = y + o.Cells[i].Y;
                            p.Cells[i] = cy * board.W + cx;
                            mask |= Bits.Cell(p.Cells[i]);
                            if (cx == minX) p.Left = true;
                            if (cy == minY) p.Top = true;
                            foreach (var n in Neighbours)
                                if (board.Available(cx + n[0], cy + n[1])) halo |= Bits.Cell((cy + n[1]) * board.W + cx + n[0]);
                        }
                        p.Mask = mask;
                        p.Halo = halo & ~mask;
                        result.Add(p);
                    }
            }
            return result;
        }

        public static bool Adjacent(Placement p, Placement q) => (p.Halo & q.Mask).Any;
    }
}
