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
        /// <summary>Leave every 退化 / 过载 (易损) module out of the search, installed ones included.</summary>
        static bool excludeDegrading, excludeOvervolted;
        const string DegradingEffect = "MODULE_EFFECT_DEGRADING", OvervoltedEffect = "MODULE_EFFECT_OVERVOLTED";

        /// <summary>
        /// Excluded by the two buttons. 保留 / 固定 modules and stuck ones stay: they must be used, as in the web tool,
        /// where required modules are never excluded automatically.
        /// </summary>
        /// <summary>损坏模组 (repairable with electronics) and 报废模组 (only for scrap): no stats, they only take room.</summary>
        static readonly HashSet<string> BrokenModules = new HashSet<string> { "system_module_corrupt", "system_module_ruined" };
        static bool IsBroken(ModuleInfo m) => m?.Identifier != null && BrokenModules.Contains(m.Identifier);

        /// <summary>Stuck or broken modules in the bay stay where they are; broken ones elsewhere are no candidates.</summary>
        static bool Pinned(ModuleInfo m) => m.Stuck || IsBroken(m);

        /// <summary>The user's locks plus 固定 for every stuck or broken module in the machine.</summary>
        static Dictionary<int, EngineBridge.LockKind> EffectiveLocks(MachineInfo machine)
        {
            var result = new Dictionary<int, EngineBridge.LockKind>(locks);
            foreach (var p in machine.Installed) if (IsBroken(p.Module)) result[p.Module.UniqueId] = EngineBridge.LockKind.Fixed;
            return result;
        }

        /// <summary>Which modules are free and in what state: a repair or an extraction in storage changes it.</summary>
        internal static string FreeFingerprint()
        {
            var snap = LiveReader.Read();
            return string.Join(";", snap.Free.Select(m => m.UniqueId + ":" + m.Identifier + ":" + m.Stuck).OrderBy(s => s, StringComparer.Ordinal));
        }

        static bool Excluded(ModuleInfo m)
        {
            if (Pinned(m) || m.Effects == null) return false;
            if (locks.TryGetValue(m.UniqueId, out var kind) && kind != EngineBridge.LockKind.None) return false;
            return excludeDegrading && m.Effects.Contains(DegradingEffect) || excludeOvervolted && m.Effects.Contains(OvervoltedEffect);
        }
        static int resultAxis = Axis.Performance;
        static int target, timeIndex = OptimizerTuning.DefaultTimeIndex, primary = Axis.Performance, secondary = -1;
        static readonly string[] minimum = { "", "", "" }, maximum = { "", "", "" };
        static string message;
        static double displayLimitSeconds = OptimizerTuning.TimeLimitsSeconds[OptimizerTuning.DefaultTimeIndex];
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
            bayShows = BayShows.Current;
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
            bool firstFeasible = result?.Feasible != true && next?.Feasible == true;
            if (next != null) result = next;
            if (firstFeasible) bayShows = BayShows.Result;
            if (failure != null && !completionShown)
            {
                message = Loc.T("opt_search_failed"); ModePuzzleMod.Log?.Warning("Search failed: " + failure);
            }
            UpdateMetrics();
            if (!running && !completionShown)
            {
                completionShown = true;
                Refresh();
            }
            else if (firstFeasible) Refresh();
        }

        static string Status()
        {
            if (message == Loc.T("opt_search_failed")) return Loc.T("opt_failed_tag");
            if (invalid && message == Loc.T("opt_stale")) return Loc.T("opt_invalid_tag");
            if (result == null) return Loc.T(Busy ? "opt_searching" : "opt_ready");
            var r = result;
            return Loc.T(!r.Done ? "opt_searching" : r.Phase == Search.Phases.Stopped ? "opt_stopped" :
                r.Phase == Search.Phases.Timeout ? "opt_timeout" : !r.Feasible ? "opt_infeasible" : "opt_optimal");
        }

        static void UpdateMetrics()
        {
            if (statusElement != null) { statusElement.SetText(StatusTag()); statusElement.SetFixedWidth(TextWidth(Status()) + 4); }
            if (spinnerElement?.image != null)
            {
                spinnerElement.image.color = new Color(1, 1, 1, Busy ? 1 : 0);
                if (Busy) spinnerElement.image.sprite = SpinnerFrame(Mathf.FloorToInt(Time.unscaledTime / OptimizerTuning.PollSeconds));
            }
            var r = result;
            // In plain words: how many layouts were tried, and at which second the best one turned up (found early in
            // a long search means a longer search will rarely help).
            string tried = r == null ? "" : Loc.T("metric_tried", r.Visits.ToString("N0"));
            string best = r == null ? "" : r.Feasible ? Loc.T("metric_best_at", (r.LastImprovementMs / 1000).ToString("0.0")) : Loc.T("metric_none");
            if (metricElements.Count > 0) metricElements[0].SetText(tried);
            if (metricElements.Count > 1) metricElements[1].SetText(best);
            elapsedElement?.SetText(Loc.T("opt_elapsed", ((r?.ElapsedMs ?? 0) / 1000).ToString("0.0"), displayLimitSeconds));
            progressElement?.SetProgress(Mathf.Clamp01((float)((r?.ElapsedMs ?? 0) / (displayLimitSeconds * 1000.0))));
        }

        static void StartSearch()
        {
            if (Busy) return;
            try
            {
                // Capture at click time, not the potentially stale model used to draw the controls.
                var snap=LiveReader.Read(); var machine=snap.Machines.Single(m => m.UniqueId == selectedMachineId);
                var free=onlyInstalled ? new List<ModuleInfo>() : snap.Free.Where(machine.CanHold).Concat(Borrowable(snap,machine)).ToList();
                free=free.Where(m => !Excluded(m) && !IsBroken(m)).ToList();
                var config=EngineBridge.Config(machine,free,EffectiveLocks(machine));
                var dropped=new HashSet<string>(machine.Installed.Select(p => p.Module).Where(Excluded).Select(m => "u"+m.UniqueId));
                config.Items.RemoveAll(i => dropped.Contains(i.Id));
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
                detailAxis=resultAxis; bayShows=BayShows.Current;
                original=machine; result=null; invalid=false; completionShown=false; message=null;
                displayLimitSeconds = OptimizerTuning.TimeLimitsSeconds[timeIndex];
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
    }
}
