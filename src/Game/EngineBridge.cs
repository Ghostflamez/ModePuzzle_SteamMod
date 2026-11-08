using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Model;

namespace ModePuzzle.Game
{
    /// <summary>Converts live machines and modules into the engine's board, items and placements.</summary>
    static class EngineBridge
    {
        static readonly int[][] Neighbours = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        public static Board BoardOf(MachineInfo m) => new Board(m.Width, m.Height, (bool[])m.Usable.Clone());

        public static Item ItemOf(ModuleInfo m)
        {
            var traits = Engine.Traits.None;
            bool risk = false;
            foreach (var e in m.Effects)
            {
                switch (e)
                {
                    case "MODULE_EFFECT_SIDE_MOUNT": traits |= Engine.Traits.Left; break;
                    case "MODULE_EFFECT_TOP_MOUNT": traits |= Engine.Traits.Top; break;
                    case "MODULE_EFFECT_NEGATIVE_FEEDBACK": traits |= Engine.Traits.Negative; break;
                    case "MODULE_EFFECT_RECEIVER": traits |= Engine.Traits.Receiver; break;
                    case "MODULE_EFFECT_OVERVOLTED": case "MODULE_EFFECT_DEGRADING": risk = true; break;
                }
            }
            return new Item
            {
                Id = "u" + m.UniqueId, Name = m.Name,
                Type = m.IsNode ? ItemType.Node : ItemType.Module,
                Shape = m.Shape.Select(c => new Engine.Point(c.X, c.Y)).ToArray(),
                Base = new[] { m.Efficiency, m.Quality, m.Performance },
                Traits = traits, Risk = risk,
            };
        }

        public enum LockKind { None, Required, Fixed }

        public static SearchConfig Config(MachineInfo machine, IEnumerable<ModuleInfo> free, IDictionary<int, LockKind> locks)
        {
            var config = new SearchConfig { Board = BoardOf(machine) };
            foreach (var placed in machine.Installed)
            {
                var item = ItemOf(placed.Module);
                locks.TryGetValue(placed.Module.UniqueId, out var kind);
                if (placed.Module.Stuck || kind == LockKind.Fixed)
                {
                    var placement = PlacementOf(config.Board, placed.Cells);
                    placement.X = placed.Cells.Min(c => c.X); placement.Y = placed.Cells.Min(c => c.Y);
                    placement.Rotation = ((4 - placed.Orientation) % 4) * 90; placement.Flipped = placed.Flipped;
                    config.Fixed.Add(new PlacedPiece { Item = item, Instance = item.Id + ":1", Placement = placement });
                }
                else { item.Required = kind == LockKind.Required; config.Items.Add(item); }
            }
            foreach (var module in free)
                if (!module.Stuck && machine.CanHold(module) && module.Shape.Count > 0)
                {
                    var item = ItemOf(module);
                    if (Geometry.GeneratePlacements(config.Board, item).Count > 0) config.Items.Add(item);
                }
            return config;
        }

        /// <summary>Match occupied cells to the game's clockwise transform, including shape padding.</summary>
        public static PlacedModule Proposed(ModuleInfo module, Placement placement, int width)
        {
            var wanted = placement.Cells.OrderBy(c => c).ToArray();
            for (int flip = 0; flip < 2; flip++)
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    var cells = module.Shape.Select(c =>
                    {
                        int x = flip == 1 ? module.ShapeWidth - 1 - c.X : c.X, y = c.Y;
                        switch (rotation)
                        {
                            case 1: { int t = x; x = y; y = module.ShapeWidth - 1 - t; break; }
                            case 2: x = module.ShapeWidth - 1 - x; y = module.ShapeHeight - 1 - y; break;
                            case 3: { int t = x; x = module.ShapeHeight - 1 - y; y = t; break; }
                        }
                        return new Cell(x, y);
                    }).ToList();
                    int dx = wanted.Min(c => c % width) - cells.Min(c => c.X);
                    int dy = wanted.Min(c => c / width) - cells.Min(c => c.Y);
                    cells = cells.Select(c => new Cell(c.X + dx, c.Y + dy)).ToList();
                    if (cells.Select(c => c.Y * width + c.X).OrderBy(c => c).SequenceEqual(wanted))
                        return new PlacedModule { Module = module, Cells = cells, OriginX = dx, OriginY = dy, Orientation = rotation, Flipped = flip == 1 };
                }
            throw new System.ArgumentException("Result shape has no game transform: " + module.UniqueId);
        }

        /// <summary>A placement for cells a module already covers in the bay (mask, halo and mount edges as
        /// Geometry.GeneratePlacements computes them).</summary>
        public static Placement PlacementOf(Board board, IList<Cell> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            for (int i = 0; i < board.Cells.Length; i++)
                if (board.Cells[i]) { minX = System.Math.Min(minX, i % board.W); minY = System.Math.Min(minY, i / board.W); }
            var p = new Placement { Cells = new int[cells.Count] };
            Bits mask = default, halo = default;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                p.Cells[i] = c.Y * board.W + c.X;
                mask |= Bits.Cell(p.Cells[i]);
                if (c.X == minX) p.Left = true;
                if (c.Y == minY) p.Top = true;
                foreach (var n in Neighbours)
                    if (board.Available(c.X + n[0], c.Y + n[1])) halo |= Bits.Cell((c.Y + n[1]) * board.W + c.X + n[0]);
            }
            p.Mask = mask;
            p.Halo = halo & ~mask;
            return p;
        }

        /// <summary>Scores the machine's current layout with the engine. Cells outside the bay are ignored.</summary>
        public static Scoring.Evaluation EvaluateCurrent(MachineInfo m, Rules rules)
        {
            var board = BoardOf(m);
            var layout = m.Installed
                .Where(p => p.Cells.Count > 0 && p.Cells.All(c => m.IsUsable(c.X, c.Y)))
                .Select(p => new PlacedPiece { Instance = "u" + p.Module.UniqueId, Item = ItemOf(p.Module), Placement = PlacementOf(board, p.Cells) })
                .ToList();
            return Scoring.Evaluate(layout, rules);
        }
    }
}
