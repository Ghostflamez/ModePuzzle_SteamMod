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
    /// Priority is lexicographic (primary, then secondary on ties, then fewer overvolted / degrading modules).
    /// </summary>
    public sealed class Search
    {
        public static class Phases
        {
            public const string Heuristic = "heuristic", Exact = "exact", Done = "done", Timeout = "timeout", Stopped = "stopped", Infeasible = "infeasible";
        }

        // Discovery budget only: the reference uses an 1800 ms cap. A shorter seed pass leaves
        // more time for exact proof; none of these settings changes scores or admissible bounds.
        const double HeuristicShare = 0.18, HeuristicMinMs = 30, ProgressIntervalMs = 125;
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
            public Accumulator Acc;
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
        readonly Func<bool> cancelled;
        bool stopped;
        string phase = Phases.Heuristic;
        double lastImprovement;
        readonly List<Placed> fixedLayout = new List<Placed>();
        Bits fixedMask;
        int fixedArea;
        readonly bool custom;
        readonly int primary, secondary;
        readonly AxisRange[] ranges;
        readonly int capacity;

        List<TypeEntry> types;
        Accumulator[,] termTable;
        List<Piece> pieces;
        Limits[,] pm;
        double[,] potentialHi, potentialLo;
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

        public Search(SearchConfig config, double limitMs, Func<double> now, Func<double> random, Action<SearchResult> onProgress = null, Func<bool> cancelled = null)
        {
            this.config = config;
            this.cancelled = cancelled ?? (() => false);
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

        public static int SearchQuantity(SearchConfig config) => config.Items.Where(i => !i.Locked).Sum(i => i.Qty) + config.Fixed.Count;

        public SearchResult Solve()
        {
            if (SearchQuantity(config) > MaxSearchQuantity)
                throw new ArgumentException($"At most {MaxSearchQuantity} pieces can take part in one search ({SearchQuantity(config)} given).");
            start = now();
            lastReport = start;
            Prepare();

            var all = Enumerable.Range(0, n).Where(i => pieces[i].Type.Placements.Count > 0).ToList();
            var initial = Totals(fixedLayout);
            var root = Bounds(all, capacity - fixedArea, initial);
            rootBounds = new BoundPair { Hi = (double[])root.Hi.Clone(), Lo = (double[])root.Lo.Clone() };
            if (config.Optional) Save(fixedLayout, initial);
            if (pieces.Where(p => p.Required).Sum(p => p.Area) + fixedArea > capacity || pieces.Any(p => p.Required && p.Type.Placements.Count == 0))
                return Result(true, Phases.Infeasible);
            if (!Viable(rootBounds.Value)) return Result(true, Phases.Done);

            Heuristic();
            phase = Phases.Exact;
            onProgress(Result(false, Phases.Exact));
            selected.AddRange(fixedLayout.Select(a => a.Clone()));
            selectedRisk = RiskOf(selected);
            if (!expired) Dfs(0, fixedMask, fixedArea, initial);
            return Result(true, stopped ? Phases.Stopped : expired ? Phases.Timeout : bestLayout == null ? Phases.Infeasible : Phases.Done);
        }

        void Prepare()
        {
            if (config.Fixed.Any(f => f.Item == null || f.Placement == null || f.Item.Qty != 1) ||
                config.Items.Concat(config.Fixed.Select(f => f.Item)).GroupBy(i => i.Id).Any(g => g.Count() != 1))
                throw new ArgumentException("Fixed pieces must be separate, unique single instances.");
            types = config.Items.Where(i => !i.Locked).Concat(config.Fixed.Select(f => f.Item)).Select(item =>
            {
                var placements = Geometry.GeneratePlacements(config.Board, item);
                foreach (var p in placements) p.U = Scoring.Unary(item, p, rules);
                return new TypeEntry { Item = item, Placements = placements };
            }).ToList();
            termTable = new Accumulator[types.Count, types.Count];
            for (int x = 0; x < types.Count; x++)
                for (int y = 0; y < types.Count; y++)
                    termTable[x, y] = new Accumulator(Scoring.Terms(types[x].Item, types[y].Item, rules));

            pieces = new List<Piece>();
            for (int t = 0; t < types.Count; t++)
                for (int i = 0; i < types[t].Item.Qty; i++)
                    pieces.Add(new Piece
                    {
                        Type = types[t], TypeIndex = t, Instance = types[t].Item.Id + ":" + (i + 1), Copy = i,
                        Risk = types[t].Item.Risk, Required = !config.Optional || types[t].Item.Required,
                        Area = types[t].Item.Shape.Length,
                    });

            foreach (var f in config.Fixed)
            {
                var a = pieces.Single(p => p.Item == f.Item);
                var p = a.Type.Placements.FirstOrDefault(q => q.Cells.OrderBy(c => c).SequenceEqual(f.Placement.Cells.OrderBy(c => c)));
                if (p == null || (fixedMask & p.Mask).Any) throw new ArgumentException("Illegal or overlapping fixed placement.");
                fixedMask |= p.Mask;
                fixedArea += a.Area;
                a.Required = false;
                // Geometry is canonical and recomputed; retain the supplied pose for symmetric pieces.
                var pose = new Placement { Cells = (int[])p.Cells.Clone(), Mask = p.Mask, Halo = p.Halo,
                    Left = p.Left, Top = p.Top, U = p.U, Index = p.Index,
                    X = f.Placement.X, Y = f.Placement.Y, Rotation = f.Placement.Rotation, Flipped = f.Placement.Flipped };
                fixedLayout.Add(new Placed { Piece = a, P = pose });
                pieces.Remove(a);
            }
            InitializeAccumulators(fixedLayout);
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
            frames = new Frame[n];
            for (int i = 0; i < n; i++) frames[i] = new Frame(pieces[i].Type.Placements.Count, n + fixedLayout.Count);
            boundValues.Capacity = n;
            selected.Capacity = n + fixedLayout.Count;
            lastByType = new int[types.Count];
            for (int t = 0; t < types.Count; t++) lastByType[t] = -1;
            requiredCount = pieces.Count(p => p.Required);
            pm = new Limits[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) pm[i, j] = Pair(pieces[i], pieces[j]);
            // Each future piece owns half of every future edge, and all edges to selected pieces.
            // Numerators here are integers or halves, so reversible updates are exact in double.
            potentialHi = new double[n, Axis.Count];
            potentialLo = new double[n, Axis.Count];
            for (int i = 0; i < n; i++)
                for (int k = 0; k < Axis.Count; k++)
                {
                    potentialHi[i, k] = pieces[i].MaxUnary[k];
                    potentialLo[i, k] = pieces[i].MinUnary[k];
                    foreach (var f in fixedLayout)
                    {
                        potentialHi[i, k] += Pair(pieces[i], f.Piece).Hi[k];
                        potentialLo[i, k] += Pair(pieces[i], f.Piece).Lo[k];
                    }
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j || pieces[j].Type.Placements.Count == 0) continue;
                        potentialHi[i, k] += pm[i, j].Hi[k] / 2.0;
                        potentialLo[i, k] += pm[i, j].Lo[k] / 2.0;
                    }
                }
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
                for (int c = 0; c < Scoring.Channels; c++)
                    for (int k = 0; k < Axis.Count; k++)
                    {
                        long num = termTable[x, y].Get(c, k);
                        int ceil = (int)((Math.Abs(num) + Scoring.Denominator - 1) / Scoring.Denominator);
                        if (num < 0) l.Lo[k] -= ceil; else l.Hi[k] += ceil;
                    }
            return l;
        }

        Accumulator Terms(Piece x, Piece y) => termTable[x.TypeIndex, y.TypeIndex];

        bool Complete(List<Placed> layout)
        {
            int count = 0;
            for (int i = 0; i < layout.Count; i++) if (layout[i].Piece.Required) count++;
            return count == requiredCount;
        }

        static int RiskOf(List<Placed> layout)
        {
            int count = 0;
            for (int i = 0; i < layout.Count; i++) if (layout[i].Piece.Risk) count++;
            return count;
        }

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
            int order = ComparePriority(totals, bestTotals);
            if (order < 0 || !Complete(layout) || !WithinBounds(totals)) return;
            int risk = RiskOf(layout);
            if (order > 0 || (order == 0 && risk < bestRisk))
            {
                lastImprovement = now() - start;
                bestTotals = (int[])totals.Clone();
                bestLayout = layout.Select(a => a.Clone()).ToList();
                bestRisk = risk;
            }
        }

        double ScalarBound(List<int> remaining, int free, int current, int k, int sign)
        {
            var vals = boundValues;
            vals.Clear();
            foreach (int i in remaining)
            {
                double v = sign * (sign == 1 ? potentialHi[i, k] : potentialLo[i, k]);
                vals.Add((pieces[i].Area, v));
            }
            if (!config.Optional)
            {
                double sum = current;
                for (int i = 0; i < vals.Count; i++) sum += sign * vals[i].value;
                return sum;
            }
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

        BoundPair Bounds(List<int> remaining, int free, int[] totals)
        {
            var b = boundScratch;
            for (int k = 0; k < Axis.Count; k++) { b.Hi[k] = double.PositiveInfinity; b.Lo[k] = double.NegativeInfinity; }
            foreach (int k in boundAxes)
            {
                b.Hi[k] = ScalarBound(remaining, free, totals[k], k, 1);
                if (custom) b.Lo[k] = ScalarBound(remaining, free, totals[k], k, -1);
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

        Accumulator Delta(Piece a, Placement p, List<Placed> layout, int[] d)
        {
            for (int k = 0; k < Axis.Count; k++) d[k] = p.U[k];
            var acc = default(Accumulator);
            foreach (var b in layout)
            {
                if (!Geometry.Adjacent(p, b.P)) continue;
                acc.Add(Terms(a, b.Piece));
                b.Acc.AddDifference(Terms(b.Piece, a), d);
            }
            for (int k = 0; k < Axis.Count; k++) d[k] += acc.Total(k);
            return acc;
        }

        void Append(List<Placed> layout, Placed a)
        {
            foreach (var b in layout)
            {
                if (!Geometry.Adjacent(a.P, b.P)) continue;
                b.Acc.Add(Terms(b.Piece, a.Piece));
            }
            layout.Add(a);
        }

        void InitializeAccumulators(List<Placed> layout)
        {
            foreach (var a in layout)
            {
                a.Acc = default(Accumulator);
                foreach (var b in layout)
                    if (a != b && Geometry.Adjacent(a.P, b.P)) a.Acc.Add(Terms(a.Piece, b.Piece));
            }
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
            double heuristicEnd = start + Math.Min(config.HeuristicBudgetMs, Math.Max(HeuristicMinMs, limitMs * HeuristicShare));
            int iteration = 0;
            List<Placed> repairLayout = null;
            double repairFitness = double.NegativeInfinity;
            var delta = new int[Axis.Count];
            var trial = new int[Axis.Count];
            var candidateD = new int[Axis.Count];
            while (n > 0 && now() < heuristicEnd && !ShouldStop())
            {
                var layout = fixedLayout.Select(a => a.Clone()).ToList();
                Bits mask = fixedMask;
                var totals = Totals(layout);
                var seed = bestLayout ?? repairLayout;
                if (seed != null && iteration % RestartEvery != 0)
                {
                    layout = seed.Where(a => fixedLayout.Any(f => f.Piece == a.Piece) || random() > RepairDropRate).Select(a => a.Clone()).ToList();
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
                    if (ShouldStop()) break;
                    Placed candidate = null;
                    double quality = double.NegativeInfinity;
                    double currentFitness = Fitness(totals);
                    foreach (var p in a.Type.Placements)
                    {
                        if (ShouldStop()) break;
                        if ((mask & p.Mask).Any) continue;
                        var acc = Delta(a, p, layout, delta);
                        for (int k = 0; k < Axis.Count; k++) trial[k] = totals[k] + delta[k];
                        double q = Fitness(trial) + random() * PlacementJitter;
                        if (q > quality)
                        {
                            quality = q;
                            if (candidate == null) candidate = new Placed { Piece = a };
                            candidate.P = p;
                            candidate.Acc = acc;
                            Array.Copy(delta, candidateD, Axis.Count);
                        }
                    }
                    if (candidate == null)
                    {
                        if (a.Required) { failed = true; break; }
                        continue;
                    }
                    for (int k = 0; k < Axis.Count; k++) trial[k] = totals[k] + candidateD[k];
                    if (!a.Required && Fitness(trial) < currentFitness && random() < SkipWorseRate) continue;
                    Append(layout, candidate);
                    mask |= candidate.P.Mask;
                    Array.Copy(trial, totals, Axis.Count);
                }
                if (!failed && Complete(layout))
                {
                    Save(layout, totals);
                    if (!Viable(rootBounds.Value)) break;
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
        int[] lastByType;
        int selectedRisk, selectedRequired;
        readonly List<(int area, double value)> boundValues = new List<(int, double)>();
        readonly BoundPair boundScratch = new BoundPair { Hi = new double[Axis.Count], Lo = new double[Axis.Count] };
        Frame[] frames;
        long ticks;

        struct Candidate
        {
            public Placement P;
            public Accumulator Acc;
            public int D0, D1, D2;
            public double Fitness;
        }

        sealed class CandidateComparer : IComparer<Candidate>
        {
            public static readonly CandidateComparer Instance = new CandidateComparer();
            public int Compare(Candidate a, Candidate b)
            {
                int c = b.Fitness.CompareTo(a.Fitness);
                return c != 0 ? c : a.P.Index.CompareTo(b.P.Index);
            }
        }

        sealed class Frame
        {
            public readonly List<int> Remaining;
            public readonly Candidate[] Candidates;
            public readonly Accumulator[] Previous;
            public readonly int[] Delta = new int[Axis.Count], Totals = new int[Axis.Count];
            public readonly Placed Placed = new Placed();
            public Frame(int placements, int pieces)
            {
                Remaining = new List<int>(pieces);
                Candidates = new Candidate[placements];
                Previous = new Accumulator[pieces];
            }
        }

        void Dfs(int index, Bits mask, int used, int[] totals)
        {
            CheckClock();
            if (expired) return;
            if (config.Optional && selectedRequired == requiredCount) Save(selected, totals);
            if (index == n) { Save(selected, totals); return; }

            var frame = frames[index];
            var remaining = frame.Remaining;
            remaining.Clear();
            for (int j = index; j < n; j++)
                if (lastByType[pieces[j].TypeIndex] != Skipped && pieces[j].Type.Placements.Count > 0)
                    remaining.Add(j);
            int requiredArea = 0;
            for (int j = 0; j < remaining.Count; j++)
                if (pieces[remaining[j]].Required) requiredArea += pieces[remaining[j]].Area;
            if (requiredArea > capacity - used) return;
            if (!Viable(Bounds(remaining, capacity - used, totals), selectedRisk)) return;
            // Keep the cheap bound before terminal enumeration: most terminal branches cannot improve.
            if (index == n - 1) { FinishLast(index, mask, used, totals, frame); return; }

            var a = pieces[index];
            int prev = lastByType[a.TypeIndex];
            bool hadPrev = prev != -1;
            if (!(hadPrev && prev == Skipped) && used + a.Area <= capacity)
            {
                var candidates = frame.Candidates;
                int count = 0;
                foreach (var p in a.Type.Placements)
                {
                    if ((mask & p.Mask).Any || (hadPrev && p.Index <= prev)) continue;
                    var acc = Delta(a, p, selected, frame.Delta);
                    for (int k = 0; k < Axis.Count; k++) frame.Totals[k] = totals[k] + frame.Delta[k];
                    candidates[count++] = new Candidate { P = p, Acc = acc, D0 = frame.Delta[0],
                        D1 = frame.Delta[1], D2 = frame.Delta[2], Fitness = Fitness(frame.Totals) };
                }
                // Placement indices preserve the original stable fitness ordering on ties.
                Array.Sort(candidates, 0, count, CandidateComparer.Instance);
                for (int c = 0; c < count; c++)
                {
                    var candidate = candidates[c];
                    var p = candidate.P;
                    for (int j = 0; j < selected.Count; j++)
                    {
                        var other = selected[j];
                        frame.Previous[j] = other.Acc;
                        if (Geometry.Adjacent(p, other.P)) other.Acc.Add(Terms(other.Piece, a));
                    }
                    frame.Placed.Piece = a;
                    frame.Placed.P = p;
                    frame.Placed.Acc = candidate.Acc;
                    selected.Add(frame.Placed);
                    if (a.Risk) selectedRisk++;
                    if (a.Required) selectedRequired++;
                    lastByType[a.TypeIndex] = p.Index;
                    frame.Totals[0] = totals[0] + candidate.D0;
                    frame.Totals[1] = totals[1] + candidate.D1;
                    frame.Totals[2] = totals[2] + candidate.D2;
                    UpdatePotential(index, false, 1);
                    Dfs(index + 1, mask | p.Mask, used + a.Area, frame.Totals);
                    UpdatePotential(index, false, -1);
                    selected.RemoveAt(selected.Count - 1);
                    if (a.Risk) selectedRisk--;
                    if (a.Required) selectedRequired--;
                    for (int j = 0; j < selected.Count; j++) selected[j].Acc = frame.Previous[j];
                    if (expired) break;
                }
            }
            RestoreLast(a.TypeIndex, hadPrev, prev);
            if (!a.Required && !expired)
            {
                lastByType[a.TypeIndex] = Skipped;
                bool remove = !(hadPrev && prev == Skipped);
                if (remove) UpdatePotential(index, true, -1);
                Dfs(index + 1, mask, used, totals);
                if (remove) UpdatePotential(index, true, 1);
                RestoreLast(a.TypeIndex, hadPrev, prev);
            }
        }

        bool ShouldStop()
        {
            if (cancelled()) { stopped = true; expired = true; }
            double time = now();
            if (time - start >= limitMs) expired = true;
            if (!expired && time - lastReport >= ProgressIntervalMs)
            {
                onProgress(Result(false, phase));
                lastReport = time;
            }
            return expired;
        }

        void CheckClock()
        {
            visits++;
            if ((ticks++ & ClockCheckMask) != 0) return;
            double time = now();
            if (ShouldStop()) return;
            if (time - lastReport > ProgressIntervalMs) { onProgress(Result(false, Phases.Exact)); lastReport = time; }
        }

        void FinishLast(int index, Bits mask, int used, int[] totals, Frame frame)
        {
            var a = pieces[index];
            int prev = lastByType[a.TypeIndex];
            bool hadPrev = prev != -1;
            if ((hadPrev && prev == Skipped) || used + a.Area > capacity) return;
            foreach (var p in a.Type.Placements)
            {
                if ((mask & p.Mask).Any || (hadPrev && p.Index <= prev)) continue;
                CheckClock();
                if (expired) return;
                var acc = Delta(a, p, selected, frame.Delta);
                for (int k = 0; k < Axis.Count; k++) frame.Totals[k] = totals[k] + frame.Delta[k];
                if (ComparePriority(frame.Totals, bestTotals) < 0) continue;
                frame.Placed.Piece = a;
                frame.Placed.P = p;
                frame.Placed.Acc = acc;
                selected.Add(frame.Placed);
                // Saved layouts are output geometry; scoring uses the exact delta above. No later
                // DFS branch reads their accumulators (heuristic repair has already finished).
                Save(selected, frame.Totals);
                selected.RemoveAt(selected.Count - 1);
            }
        }

        void UpdatePotential(int index, bool skipType, int sign)
        {
            for (int removed = index; removed < n; removed++)
            {
                if (removed != index && (!skipType || pieces[removed].TypeIndex != pieces[index].TypeIndex)) continue;
                if (pieces[removed].Type.Placements.Count == 0) continue;
                for (int i = index + 1; i < n; i++)
                {
                    if (i == removed) continue;
                    foreach (int k in boundAxes)
                    {
                        potentialHi[i, k] += sign * pm[i, removed].Hi[k] / 2.0;
                        if (custom) potentialLo[i, k] += sign * pm[i, removed].Lo[k] / 2.0;
                    }
                }
                if (!skipType) break;
            }
        }

        void RestoreLast(int type, bool had, int value)
        {
            lastByType[type] = had ? value : -1;
        }

        SearchResult Result(bool done, string phase)
        {
            var r = new SearchResult
            {
                Done = done, Phase = phase, Feasible = bestLayout != null, Proven = done && !expired,
                Totals = bestTotals == null ? null : (int[])bestTotals.Clone(),
                LastImprovementMs = lastImprovement,
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
