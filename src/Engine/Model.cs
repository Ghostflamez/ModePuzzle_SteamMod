using System;
using System.Collections.Generic;

namespace ModePuzzle.Engine
{
    /// <summary>Attribute axes, in the external tool's order: [效率, 质量, 性能].</summary>
    public static class Axis
    {
        public const int Efficiency = 0, Quality = 1, Performance = 2, Count = 3;
    }

    public enum ItemType { Module, Node }

    [Flags]
    public enum Traits
    {
        None = 0,
        /// <summary>侧边挂载: +mount% on the bay's left column.</summary>
        Left = 1,
        /// <summary>顶部挂载: +mount% on the bay's top row.</summary>
        Top = 2,
        /// <summary>负反馈: absorbs the negative bases of adjacent modules.</summary>
        Negative = 4,
        /// <summary>接收器: +receiver% per adjacent node.</summary>
        Receiver = 8,
    }

    /// <summary>Scoring percentages; the game's values are the defaults.</summary>
    public sealed class Rules
    {
        public int Mount = 20, NegativeAbsorb = 25, Receiver = 10, NodeAbsorb = 20;
    }

    public struct Point
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
    }

    /// <summary>A stack of identical modules (or nodes) offered to the search.</summary>
    public sealed class Item
    {
        public string Id;
        public string Name;
        public ItemType Type;
        public Point[] Shape;
        /// <summary>Integer base attributes per axis (nodes: unused).</summary>
        public int[] Base = new int[Axis.Count];
        public Traits Traits;
        /// <summary>过载 / 退化: preferred out when scores tie.</summary>
        public bool Risk;
        public int Qty = 1;
        /// <summary>Must be in the layout even when the search may leave pieces out.</summary>
        public bool Required;
        /// <summary>Excluded from the search.</summary>
        public bool Locked;

        public bool Has(Traits t) => Type == ItemType.Module && (Traits & t) != 0;
    }

    /// <summary>A bay: row-major usable cells.</summary>
    public sealed class Board
    {
        public int W, H;
        public bool[] Cells;
        public Board(int w, int h, bool[] cells) { W = w; H = h; Cells = cells; }
        public bool Available(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && Cells[y * W + x];
        public int Capacity { get { int n = 0; foreach (var c in Cells) if (c) n++; return n; } }
    }

    /// <summary>Range on one axis for a custom target (infinite = unbounded).</summary>
    public struct AxisRange
    {
        public double Min, Max;
        public AxisRange(double min, double max) { Min = min; Max = max; }
        public static AxisRange Any => new AxisRange(double.NegativeInfinity, double.PositiveInfinity);
    }

    public sealed class SearchConfig
    {
        public Board Board;
        public List<Item> Items = new List<Item>();
        /// <summary>Pre-placed instances, separate from Items; quantity must be one and IDs unique.</summary>
        public List<PlacedPiece> Fixed = new List<PlacedPiece>();
        public Rules Rules = new Rules();
        /// <summary>Pieces may be left out (unless Required).</summary>
        public bool Optional = true;
        /// <summary>Maximise this axis; ignored when Custom is set.</summary>
        public int Target = Axis.Performance;
        public CustomTarget Custom;
        /// <summary>Discovery cap only; never changes feasibility, scoring or proof.</summary>
        public double HeuristicBudgetMs = 200;
    }

    /// <summary>Ranges on every axis, then maximise Primary, then Secondary on ties.</summary>
    public sealed class CustomTarget
    {
        public AxisRange[] Bounds = { AxisRange.Any, AxisRange.Any, AxisRange.Any };
        public int Primary = Axis.Performance;
        /// <summary>-1: none.</summary>
        public int Secondary = -1;
    }

    /// <summary>One placed piece of a result.</summary>
    public sealed class PlacedPiece
    {
        public string Instance;
        public Item Item;
        public Placement Placement;
    }

    public sealed class SearchResult
    {
        public bool Done, Feasible, Proven;
        public string Phase;
        public int[] Totals;
        public List<PlacedPiece> Layout = new List<PlacedPiece>();
        public int RiskPieces;
        public long Visits;
        public double ElapsedMs, LastImprovementMs;
        /// <summary>Upper bound on the primary axis from the root relaxation (null: unbounded).</summary>
        public double? UpperBound;
    }
}
