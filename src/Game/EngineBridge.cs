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
