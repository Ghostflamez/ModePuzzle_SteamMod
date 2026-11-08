using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ModePuzzle.Engine;

static class FixedChecks
{
    static int Main()
    {
        for (int seed = 0; seed < 180; seed++)
        {
            var rng = new Random(seed);
            var board = new Board(3, 2, Enumerable.Range(0, 6).Select(i => i != 5 || seed % 3 != 0).ToArray());
            var cfg = new SearchConfig { Board = board, Optional = seed % 4 != 0, Target = seed % 3 };
            if (seed % 2 == 0) cfg.Custom = new CustomTarget { Primary = seed % 3, Secondary = (seed + 1) % 3,
                Bounds = new[] { new AxisRange(-35, 35), new AxisRange(-30, 40), new AxisRange(-40, 45) } };
            for (int i = 0; i < 4; i++)
            {
                var a = new Item { Id = "i" + i, Shape = i == 3 ? new[] { new Point(0,0), new Point(1,0) } : new[] { new Point(0,0) },
                    Type = (seed + i) % 4 == 0 ? ItemType.Node : ItemType.Module,
                    Traits = (Traits)rng.Next(16), Base = new[] { rng.Next(-29,31), rng.Next(-29,31), rng.Next(-29,31) },
                    Required = i == 2 && seed % 3 == 0, Risk = i % 2 == 0 };
                if (i < 1 + seed % 2) cfg.Fixed.Add(new PlacedPiece { Item = a, Instance = a.Id + ":1", Placement = Geometry.GeneratePlacements(board,a)[i] });
                else cfg.Items.Add(a);
            }
            int[] best = null; int risk = int.MaxValue;
            var layout = cfg.Fixed.ToList();
            var primary = cfg.Custom?.Primary ?? cfg.Target; var secondary = cfg.Custom?.Secondary ?? -1;
            void Walk(int at)
            {
                if (at == cfg.Items.Count)
                {
                    var t = Scoring.Evaluate(layout,cfg.Rules).Total;
                    if (cfg.Custom != null && Enumerable.Range(0,3).Any(k => t[k] < cfg.Custom.Bounds[k].Min || t[k] > cfg.Custom.Bounds[k].Max)) return;
                    int r = layout.Count(p => p.Item.Risk);
                    if (best == null || t[primary] > best[primary] || t[primary] == best[primary] &&
                        (secondary >= 0 && t[secondary] > best[secondary] || (secondary < 0 || t[secondary] == best[secondary]) && r < risk)) { best = t; risk = r; }
                    return;
                }
                var a = cfg.Items[at];
                if (cfg.Optional && !a.Required) Walk(at+1);
                foreach (var p in Geometry.GeneratePlacements(board,a))
                {
                    if (layout.Any(b => (b.Placement.Mask & p.Mask).Any)) continue;
                    layout.Add(new PlacedPiece { Item=a, Placement=p, Instance=a.Id+":1" }); Walk(at+1); layout.RemoveAt(layout.Count-1);
                }
            }
            Walk(0);
            foreach (bool exact in new[] { false, true })
            {
                var clock = Stopwatch.StartNew(); int reads = 0;
                var r = new Search(cfg,10000,() => clock.Elapsed.TotalMilliseconds + (exact && reads++ > 0 ? 1000 : 0),rng.NextDouble).Solve();
                if (!r.Proven || r.Feasible != (best != null) || best != null &&
                    (r.Totals[primary] != best[primary] || secondary >= 0 && r.Totals[secondary] != best[secondary] || r.RiskPieces != risk ||
                     !Scoring.Evaluate(r.Layout,cfg.Rules).Total.SequenceEqual(r.Totals) || cfg.Fixed.Any(f => !r.Layout.Any(p => p.Item.Id == f.Item.Id && p.Placement.Mask.Equals(f.Placement.Mask) && p.Placement.Rotation == f.Placement.Rotation && p.Placement.Flipped == f.Placement.Flipped && p.Placement.X == f.Placement.X && p.Placement.Y == f.Placement.Y))))
                    throw new Exception("Fixed brute force mismatch seed="+seed+" exact="+exact);
            }
        }
        var only = new SearchConfig { Board = new Board(1,1,new[] {true}) };
        var item = new Item { Id="fixed", Shape=new[] {new Point(0,0)}, Base=new[]{-7,9,11} };
        only.Fixed.Add(new PlacedPiece {Item=item,Placement=Geometry.GeneratePlacements(only.Board,item)[0]});
        only.Fixed[0].Placement.Rotation = 90; only.Fixed[0].Placement.Flipped = true;
        var sw = Stopwatch.StartNew();
        var result = new Search(only,1000,() => sw.Elapsed.TotalMilliseconds,() => .5).Solve();
        if (!result.Proven || !result.Totals.SequenceEqual(item.Base) || result.Layout[0].Placement.Rotation != 90 || !result.Layout[0].Placement.Flipped) throw new Exception("All fixed");
        var stop = new Search(only,1000,() => sw.Elapsed.TotalMilliseconds,() => .5,null,() => true).Solve();
        // Cancellation of a nontrivial search must never claim proof.
        only.Items.Add(new Item { Id="free", Shape=new[] {new Point(0,0)}, Base=new[]{10,20,30} });
        stop = new Search(only,1000,() => sw.Elapsed.TotalMilliseconds,() => .5,null,() => true).Solve();
        if (stop.Proven && stop.Phase == Search.Phases.Stopped) throw new Exception("Cancelled proof");
        var hard = new SearchConfig { Board=new Board(7,5,Enumerable.Repeat(true,35).ToArray()) };
        for (int i=0;i<12;i++) hard.Items.Add(new Item { Id="h"+i,
            Shape=new[]{new Point(0,0),new Point(1,0),new Point(0,1)},
            Base=new[]{i-5,10+i,20+i},Traits=Traits.Receiver | Traits.Top,
            Type=i%3==0 ? ItemType.Node : ItemType.Module });
        var session=new SearchSession(hard,5000);
        session.Start(); System.Threading.Thread.Sleep(20); session.Stop();
        var deadline=Stopwatch.StartNew();
        while(session.Running && deadline.ElapsedMilliseconds<2000) System.Threading.Thread.Sleep(1);
        var stopped=session.Poll(out var failure);
        if(session.Running || failure!=null || stopped==null || stopped.Proven || stopped.Phase!=Search.Phases.Stopped)
            throw new Exception("Worker cancellation failed: "+failure);
        session=new SearchSession(hard,550); session.Start();
        deadline.Restart(); SearchResult previous=null; int reports=0; long visits=0;
        var publications=new List<(SearchResult result,int[] total)>();
        while(session.Running && deadline.ElapsedMilliseconds<2000)
        {
            var r=session.Poll(out failure);
            if(failure!=null) throw new Exception(failure);
            if(r!=null && r!=previous)
            {
                if(r.Visits<visits) throw new Exception("Progress went backwards");
                visits=r.Visits; previous=r; reports++;
                if(r.Totals!=null) publications.Add((r,(int[])r.Totals.Clone()));
            }
            System.Threading.Thread.Sleep(5);
        }
        var timed=session.Poll(out failure);
        if(session.Running || failure!=null || timed==null || timed.Proven || timed.Phase!=Search.Phases.Timeout || reports<2)
            throw new Exception("Worker heartbeat/timeout failed");
        if(publications.Any(p=>!p.result.Totals.SequenceEqual(p.total))) throw new Exception("Published result mutated");
        Console.WriteLine("WORKER OK: cancellation, heartbeat, timeout, immutable publications");
        Console.WriteLine("FIXED BRUTE FORCE OK: 180 cases x heuristic/exact, all-fixed and cancellation checks");
        return 0;
    }
}
