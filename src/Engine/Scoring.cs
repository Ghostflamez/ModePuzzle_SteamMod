using System.Collections.Generic;

namespace ModePuzzle.Engine
{
    /// <summary>
    /// The game's module scoring (ModuleEffectHelper), as the external tool's engine.js. Each module's value
    /// starts at its integer base; effects add integers:
    ///   side / top mount:  base * mount / 100, truncated separately per mount
    ///   receiver:          base * receiver * nodes / 100, truncated once
    ///   negative feedback: sum(adjacent negative bases) * negativeAbsorb / 100, truncated once
    ///   support node:      sum(adjacent module bases) * nodeAbsorb / 100 (the node's own base is unused)
    /// Bases and rates are integers, so every exact term is a numerator over 100 and C#'s integer division
    /// truncates toward zero exactly like the engine's BigInt arithmetic.
    /// </summary>
    public static class Scoring
    {
        public const int Channels = 2;   // 0: absorption (node / negative feedback), 1: receiver
        public const long Denominator = 100;

        public static int[] Unary(Item item, Placement p, Rules rules)
        {
            var u = new int[Axis.Count];
            if (item.Type == ItemType.Node) return u;
            int mounts = (item.Has(Traits.Left) && p.Left ? 1 : 0) + (item.Has(Traits.Top) && p.Top ? 1 : 0);
            for (int k = 0; k < Axis.Count; k++)
                u[k] = item.Base[k] + mounts * (int)((long)item.Base[k] * rules.Mount / Denominator);
            return u;
        }

        static int AbsorptionBase(Item item, Item other, int k)
        {
            if (other.Type != ItemType.Module) return 0;
            if (item.Type == ItemType.Node) return other.Base[k];
            return item.Has(Traits.Negative) ? System.Math.Min(0, other.Base[k]) : 0;
        }

        /// <summary>Exact contributions of <paramref name="other"/> to <paramref name="item"/>: numerators over 100,
        /// [channel][axis].</summary>
        public static long[][] Terms(Item item, Item other, Rules rules)
        {
            long rate = item.Type == ItemType.Node ? rules.NodeAbsorb : rules.NegativeAbsorb;
            bool receives = other.Type == ItemType.Node && item.Has(Traits.Receiver);
            var t = NewAcc();
            for (int k = 0; k < Axis.Count; k++)
            {
                t[0][k] = AbsorptionBase(item, other, k) * rate;
                t[1][k] = receives ? (long)item.Base[k] * rules.Receiver : 0;
            }
            return t;
        }

        public static long[][] NewAcc() => new[] { new long[Axis.Count], new long[Axis.Count] };

        public static long[][] Add(long[][] acc, long[][] extra)
        {
            var r = NewAcc();
            for (int c = 0; c < Channels; c++)
                for (int k = 0; k < Axis.Count; k++) r[c][k] = acc[c][k] + extra[c][k];
            return r;
        }

        public static int Trunc(long numerator) => (int)(numerator / Denominator);

        /// <summary>Sum over channels of each channel's truncated total.</summary>
        public static int[] TruncChannels(long[][] acc)
        {
            var r = new int[Axis.Count];
            for (int k = 0; k < Axis.Count; k++)
                for (int c = 0; c < Channels; c++) r[k] += Trunc(acc[c][k]);
            return r;
        }

        public sealed class Row
        {
            public string Instance;
            public Item Item;
            public Placement Placement;
            public int[] Self, Absorbed, Receiver, Total;
            public int ReceiverNodes;
        }

        public sealed class Evaluation
        {
            public List<Row> Rows = new List<Row>();
            public int[] Total = new int[Axis.Count];
        }

        public static Evaluation Evaluate(IList<PlacedPiece> layout, Rules rules)
        {
            var e = new Evaluation();
            for (int i = 0; i < layout.Count; i++)
            {
                var a = layout[i];
                var acc = NewAcc();
                int nodes = 0;
                for (int j = 0; j < layout.Count; j++)
                {
                    var b = layout[j];
                    if (i == j || !Geometry.Adjacent(a.Placement, b.Placement)) continue;
                    acc = Add(acc, Terms(a.Item, b.Item, rules));
                    if (a.Item.Has(Traits.Receiver) && b.Item.Type == ItemType.Node) nodes++;
                }
                var row = new Row
                {
                    Instance = a.Instance, Item = a.Item, Placement = a.Placement, ReceiverNodes = nodes,
                    Self = Unary(a.Item, a.Placement, rules), Absorbed = new int[Axis.Count],
                    Receiver = new int[Axis.Count], Total = new int[Axis.Count],
                };
                for (int k = 0; k < Axis.Count; k++)
                {
                    row.Absorbed[k] = Trunc(acc[0][k]);
                    row.Receiver[k] = Trunc(acc[1][k]);
                    row.Total[k] = row.Self[k] + row.Absorbed[k] + row.Receiver[k];
                    e.Total[k] += row.Total[k];
                }
                e.Rows.Add(row);
            }
            return e;
        }
    }
}
