using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ModePuzzle.Engine;

static class RealBenchmark
{
    static Dictionary<string,object> D(object o)=>(Dictionary<string,object>)o;
    static List<object> L(object o)=>(List<object>)o;
    static PlacedPiece Placed(object o,Board board)
    {
        var d=D(o); var item=Parity.ReadItem(d["item"]);
        var cells=L(d["cells"]).Select(Convert.ToInt32).OrderBy(c=>c).ToArray();
        return new PlacedPiece {Item=item,Instance=item.Id+":1",Placement=Geometry.GeneratePlacements(board,item).Single(p=>p.Cells.OrderBy(c=>c).SequenceEqual(cells))};
    }
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var cases=L(MiniJson.Parse(File.ReadAllText(args[0])));
        Console.WriteLine("machine,candidates,cells,heuristic_ms,current,best,bound,elapsed_ms,proven,visits,last_improvement_ms,observed_95pct_ms");
        foreach(int ci in new[]{0,4,8,10})
        {
            var data=D(cases[ci]); var runs=new List<(double budget,SearchResult r,List<SearchResult> samples)>();
            int current=0,count=0,cells=0;
            foreach(double budget in new[]{50.0,200.0,800.0})
            {
                var cfg=new SearchConfig {Board=Parity.ReadBoard(data["board"]),Items=L(data["items"]).Select(Parity.ReadItem).ToList(),HeuristicBudgetMs=budget};
                cfg.Fixed=L(data["fixed"]).Select(f=>Placed(f,cfg.Board)).ToList();
                current=Scoring.Evaluate(L(data["current"]).Select(f=>Placed(f,cfg.Board)).ToList(),cfg.Rules).Total[Axis.Performance];
                count=cfg.Items.Count; cells=cfg.Board.Capacity;
                var clock=Stopwatch.StartNew(); var rng=new Random(0); var samples=new List<SearchResult>();
                var result=new Search(cfg,15000,()=>clock.Elapsed.TotalMilliseconds,rng.NextDouble,r=>samples.Add(r)).Solve();
                samples.Add(result); runs.Add((budget,result,samples));
            }
            int observed=runs.Max(x=>x.r.Totals[Axis.Performance]);
            foreach(var run in runs)
            {
                var r=run.r;
                var good=run.samples.FirstOrDefault(s=>s.Feasible && s.Totals[Axis.Performance]>=observed*.95);
                Console.WriteLine($"{data["key"]},{count},{cells},{run.budget},{current},{r.Totals[Axis.Performance]},{r.UpperBound:0.##},{r.ElapsedMs:0},{r.Proven},{r.Visits},{r.LastImprovementMs:0},{(good==null?"not reached":good.ElapsedMs.ToString("0"))}");
            }
        }
        return 0;
    }
}
