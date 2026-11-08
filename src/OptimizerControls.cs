using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;

namespace ModePuzzle
{
    static partial class OptimizerPanel
    {
        static readonly Dictionary<int, EngineBridge.LockKind> locks = new Dictionary<int, EngineBridge.LockKind>();
        static SearchSession session;
        static LayoutTransaction transaction, previous;
        static SearchResult result;
        static MachineInfo original;
        static bool rebuilding, invalid, onlyInstalled, completionShown;
        static int resultAxis = Axis.Performance;
        static int target, timeIndex = OptimizerTuning.DefaultTimeIndex, primary = Axis.Performance, secondary = -1;
        static readonly string[] minimum = { "", "", "" }, maximum = { "", "", "" };
        static string message;
        static CustomUIElement statusElement, messageElement, applyElement;
        static readonly int[] Targets = { Axis.Performance, Axis.Efficiency, Axis.Quality };
        static List<string> Axes() => new List<string> { GameText.Efficiency, GameText.Quality, GameText.Performance };
        static bool Busy => session?.Running == true;

        static void OnClosed() { PanelWatcher.Stop(); if (!rebuilding) WindowGone(); }
        public static void WindowGone()
        {
            session?.Stop(); statusElement=null; messageElement=null; applyElement=null;
            Pixels.Destroy(baySprite); baySprite=null;
        }
        public static void ResetSession()
        {
            Close(); transaction = null; previous = null; result = null; original = null; locks.Clear(); message = null; invalid=true; completionShown=true;
        }
        /// <summary>
        /// Something the search depended on changed (or the user changed a setting): stop the search and discard
        /// its result, so no stale preview stays on screen; the panel goes back to the bay view.
        /// </summary>
        static void Invalidate()
        {
            session?.Stop(); invalid = true;
            if (transaction != null || result != null)
            {
                message = Loc.T("opt_stale");
                messageElement?.SetText(message);
            }
            DiscardResult();
            applyElement?.SetInteractable(false);
        }

        static void DiscardResult()
        {
            result = null; transaction = null; original = null;
            if (view == View.Result) view = View.Bay;
        }

        /// <summary>The panel's refresh button: re-read the game and drop a result that can no longer be applied.</summary>
        static void UserRefresh()
        {
            if (!Busy && (invalid || result == null)) { DiscardResult(); message = null; }
            Refresh();
        }

        /// <summary>Restore is offered only for the machine it belongs to, and only while nothing changed since.</summary>
        static bool RestoreAvailable => !Busy && previous != null && previous.MachineId == selectedMachineId && previous.RestoreReady();
        public static void MachineChanged() { Invalidate(); Refresh(); }
        public static bool SnapshotChanged()
        {
            if (invalid || transaction == null) return false;
            try
            {
                var reason = transaction.ChangeReason();
                if (reason != null) ModePuzzleMod.Log?.Log("Snapshot changed: " + reason);
                return reason != null;
            }
            catch (Exception e) { ModePuzzleMod.Log?.Warning("Snapshot check failed: " + e); return true; }
        }

        public static void PollSearch()
        {
            if (session == null) return;
            if (transaction == null)
            {
                if (!session.Running) { session=null; Refresh(); }
                return;
            }
            bool running = session.Running;
            var next = session.Poll(out var failure);
            if (next != null) result = next;
            if (failure != null && !completionShown)
            {
                message = Loc.T("opt_search_failed"); ModePuzzleMod.Log?.Warning("Search failed: " + failure);
            }
            if (statusElement != null) statusElement.SetText(Status());
            if (!running && !completionShown)
            {
                completionShown = true;
                if (!invalid && result?.Feasible == true) view = View.Result;
                Refresh();
            }
        }

        static string Status()
        {
            if (result == null) return Loc.T(Busy ? "opt_searching" : "opt_ready");
            var r = result;
            string state = !r.Done ? "opt_searching" : r.Phase == Search.Phases.Stopped ? "opt_stopped" :
                r.Phase == Search.Phases.Timeout ? "opt_timeout" : !r.Feasible ? "opt_infeasible" : "opt_optimal";
            int axis = resultAxis;
            return Loc.T("opt_status", Loc.T(state), (r.ElapsedMs / 1000).ToString("0.0"), r.Visits,
                r.Feasible ? r.Totals[axis].ToString() : "—", r.UpperBound?.ToString("0.##") ?? "—",
                ((r.ElapsedMs-r.LastImprovementMs)/1000).ToString("0.0"));
        }

        static void Controls(CustomUIBuilder b, float width)
        {
            b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Loc.T("opt_target"));
            Choice.Buttons(b, new List<string> { GameText.Performance, GameText.Efficiency, GameText.Quality, Loc.T("opt_custom") }, target,
                i => { target=i; Invalidate(); Refresh(); }, !Busy);
            b.End();
            b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Loc.T("opt_time_limit"));
            Choice.Buttons(b, OptimizerTuning.TimeLimitsSeconds.Select(s => Loc.T("opt_seconds",s)).ToList(), timeIndex,
                i => { timeIndex=i; Refresh(); }, !Busy);
            b.End();
            b.AddToggle(Loc.T("opt_only_installed"),onlyInstalled,v => { onlyInstalled=v; Invalidate(); Refresh(); }); b.Last.SetInteractable(!Busy);
            if (target == 3)
            {
                b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Loc.T("opt_primary"));
                Choice.Buttons(b, Axes(), primary, i => { primary=i; if (secondary==i) secondary=-1; Invalidate(); Refresh(); }, !Busy);
                b.End();
                b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Loc.T("opt_secondary"));
                var options = Axes(); options.Insert(0,Loc.T("opt_none"));
                Choice.Buttons(b, options, secondary+1, i => { if (i-1 != primary) { secondary=i-1; Invalidate(); Refresh(); } }, !Busy);
                b.End();
                for (int k=0;k<Axis.Count;k++)
                {
                    int axis=k;
                    b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Axes()[k] + " " + Loc.T("opt_min"));
                    b.Last.SetFixedWidth(OptimizerTuning.ControlWidth);
                    Choice.Stepper(b, minimum[k], OptimizerTuning.RangeStep, 0, v => { minimum[axis]=v; Invalidate(); Refresh(); },
                        Loc.T("opt_unbounded"), Loc.T("opt_clear"), !Busy);
                    b.End();
                    b.BeginRow(OptimizerTuning.RowGap,TextAnchor.MiddleLeft).AddLabel(Axes()[k] + " " + Loc.T("opt_max"));
                    b.Last.SetFixedWidth(OptimizerTuning.ControlWidth);
                    Choice.Stepper(b, maximum[k], OptimizerTuning.RangeStep, 0, v => { maximum[axis]=v; Invalidate(); Refresh(); },
                        Loc.T("opt_unbounded"), Loc.T("opt_clear"), !Busy);
                    b.End();
                }
            }
            b.BeginRow(OptimizerTuning.RowGap);
            b.AddButton(Loc.T("opt_start"),() => StartSearch()); b.Last.SetInteractable(!Busy);
            b.AddButton(Loc.T("opt_stop"),() => session?.Stop()); b.Last.SetInteractable(Busy);
            b.AddButton(Loc.T("opt_preview"),() => { view=View.Result; Refresh(); }); b.Last.SetInteractable(result?.Feasible == true);
            b.End().BeginRow(OptimizerTuning.RowGap);
            b.AddButton(Loc.T("opt_apply"),ApplyResult); applyElement=b.Last; b.Last.SetInteractable(!Busy && !invalid && result?.Feasible == true && transaction != null);
            b.AddButton(Loc.T("opt_restore"),RestorePrevious); b.Last.SetInteractable(RestoreAvailable);
            b.End();
            b.AddLabel(Status()); statusElement=b.Last; b.Last.SetFixedWidth(width);
            b.AddLabel(message ?? ""); messageElement=b.Last; b.Last.SetFixedWidth(width);
        }

        static void StartSearch()
        {
            if (Busy) return;
            try
            {
                // Capture at click time, not the potentially stale model used to draw the controls.
                var snap=LiveReader.Read(); var machine=snap.Machines.Single(m => m.UniqueId == selectedMachineId);
                var free=onlyInstalled ? new List<ModuleInfo>() : snap.Free.Where(machine.CanHold).ToList();
                var config=EngineBridge.Config(machine,free,locks);
                if (target == 3)
                {
                    config.Custom=new CustomTarget { Primary=primary, Secondary=secondary };
                    for (int k=0;k<Axis.Count;k++)
                    {
                        double Parse(string s,double fallback)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return fallback;
                            if (!double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var n) || double.IsNaN(n) || double.IsInfinity(n))
                                throw new ArgumentException(Loc.T("opt_bad_range"));
                            return n;
                        }
                        var range=new AxisRange(Parse(minimum[k],double.NegativeInfinity),Parse(maximum[k],double.PositiveInfinity));
                        if (range.Min > range.Max) throw new ArgumentException(Loc.T("opt_bad_range"));
                        config.Custom.Bounds[k]=range;
                    }
                }
                else config.Target=Targets[target];
                if (Search.SearchQuantity(config)>Search.MaxSearchQuantity) throw new ArgumentException(Loc.T("opt_too_many",Search.MaxSearchQuantity));
                var ids=new HashSet<string>(config.Items.Select(i => i.Id).Concat(config.Fixed.Select(f => f.Item.Id)));
                transaction=new LayoutTransaction(machine,config,machine.Installed.Select(p => p.Module).Concat(free).Where(m => ids.Contains("u"+m.UniqueId)));
                resultAxis=config.Custom?.Primary ?? config.Target;
                original=machine; result=null; invalid=false; completionShown=false; message=null;
                session=new SearchSession(config,OptimizerTuning.TimeLimitsSeconds[timeIndex]*1000.0); session.Start();
            }
            catch (Exception e) { message=e.Message; ModePuzzleMod.Log?.Warning("Start search: "+e); }
            Refresh();
        }

        static void ApplyResult()
        {
            if (Busy || invalid || result?.Feasible != true || transaction == null) return;
            message=transaction.Apply(result);
            if (transaction.CanRestore) previous=transaction;
            ModePuzzleMod.Log?.Log($"Apply on machine {selectedMachineId}: {message}");
            invalid=true; DiscardResult(); Refresh();
        }
        static void RestorePrevious()
        {
            if (!RestoreAvailable) { message = Loc.T("opt_restore_unavailable"); Refresh(); return; }
            message=previous.Restore(); invalid=true; DiscardResult();
            ModePuzzleMod.Log?.Log($"Restore on machine {selectedMachineId}: {message}");
            Refresh();
        }
        static string Values(int[] v) => string.Join(" / ",v.Select(GameText.ColoredValue));
        static void ResultView(CustomUIBuilder b,float width,float height)
        {
            if (result?.Feasible != true || transaction == null) { b.AddLabel(Loc.T("opt_no_result")); return; }
            try
            {
                var preview=transaction.Preview(result);
                b.AddLabel(Loc.T("opt_preview_machine",original.Name));
                Pixels.Destroy(baySprite); baySprite=ModuleArt.ComposeBay(preview);
                float scale=Mathf.Max(1,Mathf.Floor(Mathf.Min(width/(preview.Width*ModuleArt.CellPixels),height/(preview.Height*ModuleArt.CellPixels))));
                b.AddImage(baySprite,preview.Width*ModuleArt.CellPixels*scale,preview.Height*ModuleArt.CellPixels*scale);
                var before=EngineBridge.EvaluateCurrent(original,new Rules()).Total;
                b.AddLabel(Loc.T("opt_comparison",GameText.Efficiency,GameText.Quality,GameText.Performance,Values(before),Values(result.Totals))); b.Last.SetFixedWidth(width);
                var oldIds=new HashSet<int>(original.Installed.Select(p => p.Module.UniqueId));
                var newIds=new HashSet<int>(preview.Installed.Select(p => p.Module.UniqueId));
                int moved=preview.Installed.Count(p => oldIds.Contains(p.Module.UniqueId) && !original.Installed.Any(o => o.Module.UniqueId==p.Module.UniqueId &&
                    o.OriginX==p.OriginX && o.OriginY==p.OriginY && o.Orientation==p.Orientation && o.Flipped==p.Flipped));
                b.AddLabel(Loc.T("opt_counts",moved,oldIds.Except(newIds).Count(),newIds.Except(oldIds).Count()));
                b.AddHeader(Loc.T("opt_details",GameText.Efficiency,GameText.Quality,GameText.Performance));
                foreach (var row in Scoring.Evaluate(result.Layout,new Rules()).Rows)
                {
                    b.AddLabel(row.Item.Name+"\n"+Loc.T("opt_detail_row",Values(row.Item.Base),Values(row.Self),Values(row.Absorbed),Values(row.Receiver),Values(row.Total)));
                    b.Last.SetFixedWidth(width);
                }
            }
            catch (Exception e) { b.AddWarning(Loc.T("opt_search_failed")); ModePuzzleMod.Log?.Warning("Preview: "+e); }
        }
    }
}
