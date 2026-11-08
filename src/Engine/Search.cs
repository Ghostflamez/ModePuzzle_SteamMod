using System;
using System.Collections.Generic;
using System.Linq;

namespace ModePuzzle.Engine
{
    /// <summary>
    /// Layout search, ported from the external tool's engine.js `solve`: a randomized heuristic finds good
    /// layouts quickly, then a depth-first branch and bound proves the optimum. Absorption is not pairwise
    /// additive, so each placed piece keeps its exact per-channel sums and a new neighbour changes its score by
    /// the difference of truncated sums. Bounds use signed per-edge envelopes valid for every partial sum.
    /// Priority is lexicographic (primary, then secondary on ties, then fewer 过载 / 退化 modules).
    /// </summary>
    public sealed class Search
    {
        public static class Phases
        {
            public const string Heuristic = "heuristic", Exact = "exact", Done = "done", Timeout = "timeout", Infeasible = "infeasible";
        }

        // Tuning (src/config/search.js of the external tool): none of it changes scores or the proof.
        const double HeuristicShare = 0.18, HeuristicMinMs = 30, HeuristicMaxMs = 1800, ProgressIntervalMs = 250;
        const int ClockCheckMask = 255, RestartEvery = 3;
        const double RepairDropRate = 0.35, SkipWorseRate = 0.8, PlacementJitter = 0.5, RankWeight = 0.3;
        const double ViolationWeight = 1000, SecondaryWeight = 0.001;
        public const double Epsilon = 1e-9;
        public const int MaxSearchQuantity = 100;
        const int Skipped = -2;

        sealed class TypeEntry
        {
            public Item Item;
            public List<Placement> Placements;
        }

        sealed class Piece
        {
            public TypeEntry Type;
            public int TypeIndex;
            public string Instance;
            public int Copy, Area;
            public bool Risk, Required;
            public double[] MaxUnary, MinUnary;
            public double Rank;
            public Item Item => Type.Item;
        }

        sealed class Placed
        {
            public Piece Piece;
            public Placement P;
            public long[][] Acc;
            public Placed Clone() => new Placed { Piece = Piece, P = P, Acc = Acc };
        }

        struct Limits
        {
            public int[] Hi, Lo;
        }

        struct BoundPair
        {
            public double[] Hi, Lo;
        }

        readonly SearchConfig config;
        readonly Rules rules;
        readonly Func<double> now;
        readonly Func<double> random;
        readonly Action<SearchResult> onProgress;
        readonly double limitMs;
        readonly bool custom;
        readonly int primary, secondary;
        readonly AxisRange[] ranges;
        readonly int capacity;

        List<TypeEntry> types;
        long[][][][] termTable;
        List<Piece> pieces;
        Limits[,] pm;
        int n, requiredCount;
        int[] boundAxes;
        double[] scales;
        double start, lastReport;
        long visits;
        bool expired;
        int[] bestTotals;
        List<Placed> bestLayout;
        int bestRisk = int.MaxValue;
        BoundPair? rootBounds;

        public Search(SearchConfig config, double limitMs, Func<double> now, Func<double> random, Action<SearchResult> onProgress = null)
        {
            this.config = config;
            rules = config.Rules;
            this.limitMs = limitMs;
            this.now = now;
            this.random = random;
            this.onProgress = onProgress ?? (_ => { });
            custom = config.Custom != null;
            primary = custom ? config.Custom.Primary : config.Target;
            secondary = custom ? config.Custom.Secondary : -1;
            ranges = custom ? config.Custom.Bounds.ToArray() : new[] { AxisRange.Any, AxisRange.Any, AxisRange.Any };
            capacity = config.Board.Capacity;
        }

        public static int SearchQuantity(SearchConfig config) => config.Items.Where(i => !i.Locked).Sum(i => i.Qty);

        public SearchResult Solve()
        {
            if (SearchQuantity(config) > MaxSearchQuantity)
                throw new ArgumentException($"At most {MaxSearchQuantity} pieces can take part in one search ({SearchQuantity(config)} given).");
            start = now();
            lastReport = start;
            Prepare();

            var all = Enumerable.Range(0, n).Where(i => pieces[i].Type.Placements.Count > 0).ToList();
            rootBounds = Bounds(all, new List<int>(), capacity, new int[Axis.Count]);
            if (config.Optional) Save(new List<Placed>(), new int[Axis.Count]);
            if (pieces.Where(p => p.Required).Sum(p => p.Area) > capacity || pieces.Any(p => p.Required && p.Type.Placements.Count == 0))
                return Result(true, Phases.Infeasible);
            if (!Viable(rootBounds.Value)) return Result(true, Phases.Done);

            Heuristic();
            onProgress(Result(false, Phases.Exact));
            if (!expired) Dfs(0, default, 0, new int[Axis.Count]);
            return Result(true, expired ? Phases.Timeout : Phases.Done);
        }

        void Prepare()
        {
            types = config.Items.Where(i => !i.Locked).Select(item =>
            {
                var placements = Geometry.GeneratePlacements(config.Board, item);
                foreach (var p in placements) p.U = Scoring.Unary(item, p, rules);
                return new TypeEntry { Item = item, Placements = placements };
            }).ToList();
            termTable = new long[types.Count][][][];
            for (int x = 0; x < types.Count; x++)
            {
                termTable[x] = new long[types.Count][][];
                for (int y = 0; y < types.Count; y++) termTable[x][y] = Scoring.Terms(types[x].Item, types[y].Item, rules);
            }

            pieces = new List<Piece>();
            for (int t = 0; t < types.Count; t++)
                for (int i = 0; i < types[t].Item.Qty; i++)
                    pieces.Add(new Piece
                    {
                        Type = types[t], TypeIndex = t, Instance = types[t].Item.Id + ":" + (i + 1), Copy = i,
                        Risk = types[t].Item.Risk, Required = !config.Optional || types[t].Item.Required,
                        Area = types[t].Item.Shape.Length,
                    });

            var typeLimits = new Limits[types.Count, types.Count];
            for (int x = 0; x < types.Count; x++)
                for (int y = 0; y < types.Count; y++) typeLimits[x, y] = PairLimits(x, y);
            Limits Pair(Piece a, Piece b) => typeLimits[a.TypeIndex, b.TypeIndex];

            foreach (var a in pieces)
            {
                var ps = a.Type.Placements;
                a.MaxUnary = new double[Axis.Count];
                a.MinUnary = new double[Axis.Count];
                for (int k = 0; k < Axis.Count; k++)
                {
                    a.MaxUnary[k] = ps.Count > 0 ? ps.Max(p => p.U[k]) : double.NegativeInfinity;
                    a.MinUnary[k] = ps.Count > 0 ? ps.Min(p => p.U[k]) : double.PositiveInfinity;
                }
                a.Rank = a.MaxUnary[primary] + pieces.Sum(b => a == b ? 0 : Pair(a, b).Hi[primary] / 2.0);
            }
            pieces.Sort((a, b) =>
            {
                int c = (b.Required ? 1 : 0).CompareTo(a.Required ? 1 : 0);
                if (c != 0) return c;
                c = (b.Rank / b.Area).CompareTo(a.Rank / a.Area);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Item.Id, b.Item.Id);
                return c != 0 ? c : a.Copy.CompareTo(b.Copy);
            });
            n = pieces.Count;
            requiredCount = pieces.Count(p => p.Required);
            pm = new Limits[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) pm[i, j] = Pair(pieces[i], pieces[j]);
            boundAxes = custom ? new[] { 0, 1, 2 } : new[] { primary };
            scales = new double[Axis.Count];
            for (int k = 0; k < Axis.Count; k++)
                scales[k] = Math.Max(1, config.Items.Select(i => Math.Abs((double)i.Base[k]) * i.Qty).DefaultIfEmpty(0).Max());
        }

        /// <summary>For any current sum S and term t, trunc(S + t) − trunc(S) has t's sign and magnitude at most
        /// ceil(|t|), even across zero.</summary>
        Limits PairLimits(int a, int b)
        {
            var l = new Limits { Hi = new int[Axis.Count], Lo = new int[Axis.Count] };
            foreach (var (x, y) in new[] { (a, b), (b, a) })
                foreach (var channel in termTable[x][y])
                    for (int k = 0; k < Axis.Count; k++)
                    {
                        long num = channel[k];
                        int ceil = (int)((Math.Abs(num) + Scoring.Denominator - 1) / Scoring.Denominator);
                        if (num < 0) l.Lo[k] -= ceil; else l.Hi[k] += ceil;
                    }
            return l;
        }

        long[][] Terms(Piece x, Piece y) => termTable[x.TypeIndex][y.TypeIndex];

        bool Complete(List<Placed> layout) => layout.Count(a => a.Piece.Required) == requiredCount;
        static int RiskOf(List<Placed> layout) => layout.Count(a => a.Piece.Risk);

        bool WithinBounds(int[] totals)
        {
            for (int k = 0; k < Axis.Count; k++)
                if (totals[k] < ranges[k].Min - Epsilon || totals[k] > ranges[k].Max + Epsilon) return false;
            return true;
        }

        int ComparePriority(int[] a, int[] b)
        {
            if (b == null) return 1;
            double d = a[primary] - b[primary];
            if (Math.Abs(d) > Epsilon) return Math.Sign(d);
            if (secondary >= 0)
            {
                double s = a[secondary] - b[secondary];
                if (Math.Abs(s) > Epsilon) return Math.Sign(s);
            }
            return 0;
        }

        void Save(List<Placed> layout, int[] totals)
        {
            if (!Complete(layout) || !WithinBounds(totals)) return;
            int order = ComparePriority(totals, bestTotals);
            int risk = RiskOf(layout);
            if (order > 0 || (order == 0 && risk < bestRisk))
            {
                bestTotals = (int[])totals.Clone();
                bestLayout = layout.Select(a => a.Clone()).ToList();
                bestRisk = risk;
            }
        }

        double ScalarBound(List<int> remaining, List<int> selected, int free, int current, int k, int sign)
        {
            int Limit(int i, int j) => sign == 1 ? pm[i, j].Hi[k] : pm[i, j].Lo[k];
            var vals = new List<(int area, double value)>(remaining.Count);
            foreach (int i in remaining)
            {
                double v = sign * (sign == 1 ? pieces[i].MaxUnary[k] : pieces[i].MinUnary[k]);
                foreach (int j in selected) v += sign * Limit(i, j);
                foreach (int j in remaining) if (i != j) v += sign * Limit(i, j) / 2.0;
                vals.Add((pieces[i].Area, v));
            }
            if (!config.Optional) return sign * (sign * current + vals.Sum(a => a.value));
            // Relaxing mandatory inclusion gives a safe (looser) bound; inclusion is enforced elsewhere.
            vals.Sort((a, b) => (b.value / b.area).CompareTo(a.value / a.area));
            double upper = sign * current;
            foreach (var a in vals)
            {
                if (a.value <= 0) continue;
                int used = Math.Min(free, a.area);
                upper += a.value * used / a.area;
                free -= used;
                if (free <= 0) break;
            }
            return sign * upper;
        }

        BoundPair Bounds(List<int> remaining, List<int> selected, int free, int[] totals)
        {
            var b = new BoundPair { Hi = new double[Axis.Count], Lo = new double[Axis.Count] };
            for (int k = 0; k < Axis.Count; k++) { b.Hi[k] = double.PositiveInfinity; b.Lo[k] = double.NegativeInfinity; }
            foreach (int k in boundAxes)
            {
                b.Hi[k] = ScalarBound(remaining, selected, free, totals[k], k, 1);
                if (custom) b.Lo[k] = ScalarBound(remaining, selected, free, totals[k], k, -1);
            }
            return b;
        }

        /// <summary>Can a completion of this branch still beat the best layout? Risk only grows.</summary>
        bool Viable(BoundPair b, int risk = 0)
        {
            for (int k = 0; k < Axis.Count; k++)
                if (!(b.Hi[k] >= ranges[k].Min - Epsilon && b.Lo[k] <= ranges[k].Max + Epsilon)) return false;
            if (bestTotals == null) return true;
            double high = Math.Min(b.Hi[primary], ranges[primary].Max);
            if (high < bestTotals[primary] - Epsilon) return false;
            if (high > bestTotals[primary] + Epsilon) return true;
            bool fewerRisk = risk < bestRisk;
            if (secondary < 0) return fewerRisk;
            double second = Math.Min(b.Hi[secondary], ranges[secondary].Max);
            if (second > bestTotals[secondary] + Epsilon) return true;
            return second >= bestTotals[secondary] - Epsilon && fewerRisk;
        }

        (int[] d, long[][] acc) Delta(Piece a, Placement p, List<Placed> layout)
        {
            var d = (int[])p.U.Clone();
            var acc = Scoring.NewAcc();
            foreach (var b in layout)
            {
                if (!Geometry.Adjacent(p, b.P)) continue;
                acc = Scoring.Add(acc, Terms(a, b.Piece));
                var next = Scoring.Add(b.Acc, Terms(b.Piece, a));
                for (int k = 0; k < Axis.Count; k++)
                    for (int c = 0; c < Scoring.Channels; c++)
                        d[k] += Scoring.Trunc(next[c][k]) - Scoring.Trunc(b.Acc[c][k]);
            }
            var own = Scoring.TruncChannels(acc);
            for (int k = 0; k < Axis.Count; k++) d[k] += own[k];
            return (d, acc);
        }

        List<(Placed b, long[][] acc)> Append(List<Placed> layout, Placed a)
        {
            var previous = new List<(Placed, long[][])>();
            foreach (var b in layout)
            {
                if (!Geometry.Adjacent(a.P, b.P)) continue;
                previous.Add((b, b.Acc));
                b.Acc = Scoring.Add(b.Acc, Terms(b.Piece, a.Piece));
            }
            layout.Add(a);
            return previous;
        }

        static void Restore(List<Placed> layout, List<(Placed b, long[][] acc)> previous)
        {
            layout.RemoveAt(layout.Count - 1);
            foreach (var (b, acc) in previous) b.Acc = acc;
        }

        void InitializeAccumulators(List<Placed> layout)
        {
            foreach (var a in layout)
            {
                a.Acc = Scoring.NewAcc();
                foreach (var b in layout)
                    if (a != b && Geometry.Adjacent(a.P, b.P)) a.Acc = Scoring.Add(a.Acc, Terms(a.Piece, b.Piece));
            }
        }

        static int[] Plus(int[] a, int[] b)
        {
            var r = new int[Axis.Count];
            for (int k = 0; k < Axis.Count; k++) r[k] = a[k] + b[k];
            return r;
        }

        double Fitness(int[] total)
        {
            double penalty = 0;
            for (int k = 0; k < Axis.Count; k++)
                penalty += Math.Max(0, Math.Max(ranges[k].Min - total[k], total[k] - ranges[k].Max)) / scales[k];
            return -penalty * ViolationWeight + total[primary] / scales[primary]
                   + (secondary < 0 ? 0 : SecondaryWeight * total[secondary] / scales[secondary]);
        }

        int[] Totals(List<Placed> layout) =>
            Scoring.Evaluate(layout.Select(a => new PlacedPiece { Instance = a.Piece.Instance, Item = a.Piece.Item, Placement = a.P }).ToList(), rules).Total;

        /// <summary>Randomized greedy passes with repair; guides discovery only, never decides the result alone.</summary>
        void Heuristic()
        {
            double heuristicEnd = start + Math.Min(HeuristicMaxMs, Math.Max(HeuristicMinMs, limitMs * HeuristicShare));
            int iteration = 0;
            List<Placed> repairLayout = null;
            double repairFitness = double.NegativeInfinity;
            while (n > 0 && now() < heuristicEnd)
            {
                var layout = new List<Placed>();
                Bits mask = default;
                var totals = new int[Axis.Count];
                var seed = bestLayout ?? repairLayout;
                if (seed != null && iteration % RestartEvery != 0)
                {
                    layout = seed.Where(_ => random() > RepairDropRate).Select(a => a.Clone()).ToList();
                    InitializeAccumulators(layout);
                    foreach (var a in layout) mask |= a.P.Mask;
                    totals = Totals(layout);
                }
                var chosen = new HashSet<string>(layout.Select(a => a.Piece.Instance));
                double rankScale = Math.Max(1, Math.Abs(rootBounds.Value.Hi[primary]));
                var order = pieces.Where(a => !chosen.Contains(a.Instance))
                    .Select(a => (a, key: random() + RankWeight * (double.IsInfinity(a.Rank) || double.IsNaN(a.Rank) ? 0 : a.Rank) / rankScale))
                    .OrderByDescending(x => x.a.Required ? 1 : 0).ThenByDescending(x => x.key)
                    .Select(x => x.a).ToList();
                bool failed = false;
                foreach (var a in order)
                {
                    if (now() - start >= limitMs) { expired = true; break; }
                    Placed candidate = null;
                    int[] candidateD = null;
                    double quality = double.NegativeInfinity;
                    double currentFitness = Fitness(totals);
                    foreach (var p in a.Type.Placements)
                    {
                        if ((mask & p.Mask).Any) continue;
                        var (d, acc) = Delta(a, p, layout);
                        double q = Fitness(Plus(totals, d)) + random() * PlacementJitter;
                        if (q > quality) { quality = q; candidate = new Placed { Piece = a, P = p, Acc = acc }; candidateD = d; }
                    }
                    if (candidate == null)
                    {
                        if (a.Required) { failed = true; break; }
                        continue;
                    }
                    if (!a.Required && Fitness(Plus(totals, candidateD)) < currentFitness && random() < SkipWorseRate) continue;
                    Append(layout, candidate);
                    mask |= candidate.P.Mask;
                    totals = Plus(totals, candidateD);
                }
                if (!failed && Complete(layout))
                {
                    Save(layout, totals);
                    double f = Fitness(totals);
                    if (f > repairFitness) { repairFitness = f; repairLayout = layout.Select(x => x.Clone()).ToList(); }
                }
                iteration++;
                visits++;
                if (now() - lastReport > ProgressIntervalMs)
                {
                    onProgress(Result(false, Phases.Heuristic));
                    lastReport = now();
                }
                if (expired) break;
            }
        }

        readonly List<Placed> selected = new List<Placed>();
        readonly List<int> selectedIndices = new List<int>();
        readonly Dictionary<int, int> lastByType = new Dictionary<int, int>();
        long ticks;

        void Dfs(int index, Bits mask, int used, int[] totals)
        {
            visits++;
            if ((ticks++ & ClockCheckMask) == 0)
            {
                double time = now();
                if (time - start >= limitMs) { expired = true; return; }
                if (time - lastReport > ProgressIntervalMs) { onProgress(Result(false, Phases.Exact)); lastReport = time; }
            }
            if (expired) return;
            if (config.Optional) Save(selected, totals);
            if (index == n) { Save(selected, totals); return; }

            var remaining = new List<int>();
            for (int j = index; j < n; j++)
                if ((!lastByType.TryGetValue(pieces[j].TypeIndex, out int last) || last != Skipped) && pieces[j].Type.Placements.Count > 0)
                    remaining.Add(j);
            if (remaining.Sum(i => pieces[i].Required ? pieces[i].Area : 0) > capacity - used) return;
            if (!Viable(Bounds(remaining, selectedIndices, capacity - used, totals), RiskOf(selected))) return;

            var a = pieces[index];
            bool hadPrev = lastByType.TryGetValue(a.TypeIndex, out int prev);
            if (!(hadPrev && prev == Skipped) && used + a.Area <= capacity)
            {
                var candidates = new List<(Placement p, int[] d, long[][] acc, double f)>();
                foreach (var p in a.Type.Placements)
                {
                    if ((mask & p.Mask).Any || (hadPrev && p.Index <= prev)) continue;
                    var (d, acc) = Delta(a, p, selected);
                    candidates.Add((p, d, acc, Fitness(Plus(totals, d))));
                }
                // Stable sort by fitness, best first (JS Array.prototype.sort is stable).
                candidates = candidates.Select((c, i) => (c, i)).OrderByDescending(x => x.c.f).ThenBy(x => x.i).Select(x => x.c).ToList();
                foreach (var (p, d, acc, _) in candidates)
                {
                    var previous = Append(selected, new Placed { Piece = a, P = p, Acc = acc });
                    selectedIndices.Add(index);
                    lastByType[a.TypeIndex] = p.Index;
                    Dfs(index + 1, mask | p.Mask, used + a.Area, Plus(totals, d));
                    Restore(selected, previous);
                    selectedIndices.RemoveAt(selectedIndices.Count - 1);
                    if (expired) break;
                }
            }
            RestoreLast(a.TypeIndex, hadPrev, prev);
            if (!a.Required && !expired)
            {
                lastByType[a.TypeIndex] = Skipped;
                Dfs(index + 1, mask, used, totals);
                RestoreLast(a.TypeIndex, hadPrev, prev);
            }
        }

        void RestoreLast(int type, bool had, int value)
        {
            if (had) lastByType[type] = value; else lastByType.Remove(type);
        }

        SearchResult Result(bool done, string phase)
        {
            var r = new SearchResult
            {
                Done = done, Phase = phase, Feasible = bestLayout != null, Proven = done && !expired,
                Totals = bestTotals == null ? null : (int[])bestTotals.Clone(),
                RiskPieces = bestLayout == null ? 0 : RiskOf(bestLayout), Visits = visits, ElapsedMs = now() - start,
            };
            if (bestLayout != null)
                r.Layout = bestLayout.Select(a => new PlacedPiece { Instance = a.Piece.Instance, Item = a.Piece.Item, Placement = a.P }).ToList();
            if (rootBounds != null && !double.IsInfinity(rootBounds.Value.Hi[primary]))
                r.UpperBound = Math.Max(rootBounds.Value.Hi[primary], bestTotals?[primary] ?? double.NegativeInfinity);
            return r;
        }
    }
}
