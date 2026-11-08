using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ModePuzzle.Engine;

/// <summary>Checks the C# engine against cases produced by the JavaScript reference (gen_cases.mjs).</summary>
static class Parity
{
    static int failures;
    static bool exactOnly;
    static readonly List<(int ci, double ms, long visits, int gcs, int pieces, int cells)> Stats = new List<(int, double, long, int, int, int)>();

    static void Fail(string msg)
    {
        failures++;
        if (failures <= 20) Console.WriteLine("FAIL " + msg);
    }

    static int Main(string[] args)
    {
        exactOnly = args.Contains("--exact-only");
        var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(args[0]));
        var evals = (List<object>)root["evalCases"];
        var solves = (List<object>)root["solveCases"];
        int ci = 0;
        foreach (Dictionary<string, object> c in evals) CheckEval(c, ci++);
        Console.WriteLine($"eval: {evals.Count} cases, failures so far {failures}");
        ci = 0;
        var watch = Stopwatch.StartNew();
        foreach (Dictionary<string, object> c in solves) CheckSolve(c, ci++);
        Console.WriteLine($"solve: {solves.Count} cases in {watch.ElapsedMilliseconds} ms, failures total {failures}");
        foreach (var x in Stats.OrderByDescending(x => x.ms).Take(6))
            Console.WriteLine($"  case {x.ci}: {x.ms:0} ms, {x.visits} visits ({x.ms * 1000 / System.Math.Max(1, x.visits):0.0} µs/visit), gen0 GCs {x.gcs}, pieces {x.pieces}, cells {x.cells}");
        Console.WriteLine($"  total visits {Stats.Sum(x => x.visits)}, total gen0 GCs {Stats.Sum(x => x.gcs)}");
        Console.WriteLine(failures == 0 ? "PARITY OK" : "PARITY FAILED");
        return failures == 0 ? 0 : 1;
    }

    static int I(object o) => Convert.ToInt32(o, CultureInfo.InvariantCulture);
    static List<object> L(object o) => (List<object>)o;
    static Dictionary<string, object> D(object o) => (Dictionary<string, object>)o;

    static Board ReadBoard(object o)
    {
        var b = D(o);
        return new Board(I(b["w"]), I(b["h"]), L(b["cells"]).Select(x => (bool)x).ToArray());
    }

    static Item ReadItem(object o)
    {
        var d = D(o);
        var traits = Traits.None;
        bool risk = false;
        foreach (string t in L(d["traits"]))
        {
            switch (t)
            {
                case "left": traits |= Traits.Left; break;
                case "top": traits |= Traits.Top; break;
                case "negative": traits |= Traits.Negative; break;
                case "receiver": traits |= Traits.Receiver; break;
                case "overvolted": case "degrading": risk = true; break;
            }
        }
        return new Item
        {
            Id = (string)d["id"], Name = (string)d["name"],
            Type = (string)d["type"] == "node" ? ItemType.Node : ItemType.Module,
            Shape = L(d["shape"]).Select(c => new Point(I(L(c)[0]), I(L(c)[1]))).ToArray(),
            Base = L(d["base"]).Select(I).ToArray(), Traits = traits, Risk = risk,
            Qty = d.ContainsKey("qty") ? I(d["qty"]) : 1,
            Required = d.ContainsKey("required") && (bool)d["required"],
            Locked = d.ContainsKey("locked") && (bool)d["locked"],
        };
    }

    static string Arr(IEnumerable<int> a) => "[" + string.Join(",", a) + "]";

    static void CheckEval(Dictionary<string, object> c, int ci)
    {
        var board = ReadBoard(c["board"]);
        var items = L(c["items"]).Select(ReadItem).ToDictionary(i => i.Id);
        var rules = new Rules();
        var placements = items.Values.ToDictionary(i => i.Id, i => Geometry.GeneratePlacements(board, i));

        foreach (Dictionary<string, object> pc in L(c["placements"]))
        {
            var id = (string)pc["id"];
            var mine = placements[id].Select(p => string.Join(",", p.Cells.OrderBy(x => x))).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var theirs = L(pc["cells"]).Cast<string>().OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (placements[id].Count != I(pc["count"]) || !mine.SequenceEqual(theirs))
                Fail($"eval#{ci} placements of {id}: {placements[id].Count} vs {I(pc["count"])}");
        }

        var layout = new List<PlacedPiece>();
        foreach (Dictionary<string, object> lc in L(c["layout"]))
        {
            var item = items[(string)lc["id"]];
            var cells = L(lc["cells"]).Select(I).ToArray();
            int rot = I(lc["rotation"]);
            bool flipped = (bool)lc["flipped"];
            var p = placements[item.Id].FirstOrDefault(q => q.Rotation == rot && q.Flipped == flipped && q.Cells.SequenceEqual(cells));
            if (p == null) { Fail($"eval#{ci} placement of {item.Id} not found"); return; }
            p.U = Scoring.Unary(item, p, rules);
            layout.Add(new PlacedPiece { Instance = item.Id + ":1", Item = item, Placement = p });
        }
        var ev = Scoring.Evaluate(layout, rules);
        var rows = L(c["rows"]);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = D(rows[i]);
            var mine = ev.Rows[i];
            foreach (var (key, val) in new[] { ("self", mine.Self), ("absorbed", mine.Absorbed), ("receiver", mine.Receiver), ("total", mine.Total) })
            {
                var expected = L(r[key]).Select(I).ToArray();
                if (!expected.SequenceEqual(val)) Fail($"eval#{ci} row {r["id"]} {key}: {Arr(val)} vs {Arr(expected)}");
            }
        }
        var total = L(c["total"]).Select(I).ToArray();
        if (!total.SequenceEqual(ev.Total)) Fail($"eval#{ci} total {Arr(ev.Total)} vs {Arr(total)}");
    }

    static double? Num(object o) => o == null ? (double?)null : Convert.ToDouble(o, CultureInfo.InvariantCulture);

    static void CheckSolve(Dictionary<string, object> c, int ci)
    {
        var cfg = D(c["config"]);
        var config = new SearchConfig
        {
            Board = ReadBoard(cfg["board"]),
            Items = L(cfg["items"]).Select(ReadItem).ToList(),
            Optional = (bool)cfg["optional"],
        };
        if (cfg["target"] is string)
        {
            var cu = D(cfg["custom"]);
            config.Custom = new CustomTarget
            {
                Primary = I(cu["primary"]),
                Secondary = cu["secondary"] == null ? -1 : I(cu["secondary"]),
                Bounds = L(cu["bounds"]).Select(b => new AxisRange(Num(D(b)["min"]) ?? double.NegativeInfinity, Num(D(b)["max"]) ?? double.PositiveInfinity)).ToArray(),
            };
        }
        else config.Target = I(cfg["target"]);

        var clock = Stopwatch.StartNew();
        var rng = new Random(ci);
        int gc0 = GC.CollectionCount(0);
        int clockReads = 0;
        // Advance past the heuristic budget after Search records its start, to exercise DFS independently.
        var r = new Search(config, 60000,
            () => clock.Elapsed.TotalMilliseconds + (exactOnly && clockReads++ > 0 ? 2000 : 0),
            () => rng.NextDouble()).Solve();
        Stats.Add((ci, clock.Elapsed.TotalMilliseconds, r.Visits, GC.CollectionCount(0) - gc0,
            config.Items.Where(i => !i.Locked).Sum(i => i.Qty), config.Board.Capacity));
        Console.WriteLine($"case {ci}: {clock.Elapsed.TotalMilliseconds:0} ms, {r.Visits} visits, gen0 GCs {GC.CollectionCount(0) - gc0}");
        bool feasible = (bool)c["feasible"];
        if (!r.Proven) { Fail($"solve#{ci} not proven ({r.Phase})"); return; }
        if (r.Feasible != feasible) { Fail($"solve#{ci} feasible {r.Feasible} vs {feasible}"); return; }
        if (!feasible) return;
        if (c.ContainsKey("riskPieces") && r.RiskPieces != I(c["riskPieces"]))
            Fail($"solve#{ci} risk {r.RiskPieces} vs {I(c["riskPieces"])}");
        var objective = L(c["objective"]).Select(I).ToArray();
        int primary = config.Custom?.Primary ?? config.Target;
        var mine = new List<int> { r.Totals[primary] };
        if (config.Custom != null && config.Custom.Secondary >= 0) mine.Add(r.Totals[config.Custom.Secondary]);
        if (!objective.SequenceEqual(mine)) Fail($"solve#{ci} objective {Arr(mine)} vs {Arr(objective)}");
        // The reported totals must match an independent evaluation of the reported layout.
        var check = Scoring.Evaluate(r.Layout, config.Rules).Total;
        if (!check.SequenceEqual(r.Totals)) Fail($"solve#{ci} totals {Arr(r.Totals)} but layout evaluates to {Arr(check)}");
    }
}

/// <summary>Minimal JSON reader for the parity file (objects, arrays, strings, numbers, booleans, null).</summary>
static class MiniJson
{
    public static object Parse(string s) { int i = 0; return Value(s, ref i); }

    static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object Value(string s, ref int i)
    {
        Ws(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object>();
            i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                var key = (string)Value(s, ref i);
                Ws(s, ref i); i++; // ':'
                d[key] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i++] == '}') return d;
            }
        }
        if (c == '[')
        {
            var l = new List<object>();
            i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i++] == ']') return l;
            }
        }
        if (c == '"')
        {
            var sb = new StringBuilder();
            i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; char e = s[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)Convert.ToInt32(s.Substring(i + 1, 4), 16) : e); if (e == 'u') i += 4; }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
        if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
        if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
        if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
        int st = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }
}
