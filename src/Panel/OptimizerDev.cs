using System.Globalization;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Game;

namespace ModePuzzle
{
    static partial class OptimizerPanel
    {
        /// <summary>
        /// Drives the window the way a player would, for development screenshots. Read-only towards the game:
        /// nothing here applies, restores or moves items.
        /// </summary>
        internal static string DevCommand(string line)
        {
            var parts = (line ?? "").Trim().Split(new[] { ' ' }, 2);
            string verb = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
            switch (verb)
            {
                case "open": Open(); return "ok";
                case "close": Close(); return "ok";
                case "refresh": UserRefresh(); return "ok";
                case "machine":
                {
                    var snap = LiveReader.Read();
                    int index = snap.Machines.FindIndex(m => m.UniqueId == selectedMachineId);
                    if (arg == "next") index++;
                    else if (arg == "prev") index--;
                    else if (int.TryParse(arg, out int n)) index = n - 1;
                    else return "usage: machine next|prev|<1-based index>";
                    if (!IsOpen) Open();
                    SelectMachine(snap, index);
                    return "ok " + selectedMachineId;
                }
                case "picker":
                    if (arg == "close") ClosePicker(); else OpenPicker();
                    return "ok open=" + PickerOpen;
                case "pick":
                {
                    var snap = LiveReader.Read();
                    if (!int.TryParse(arg, out int n) || n < 1 || n > snap.Machines.Count) return "usage: pick <1-based index>";
                    OpenFor(snap.Machines[n - 1].UniqueId);
                    return "ok " + selectedMachineId;
                }
                case "unlock":
                    // Session state only (no save change); skips the confirmation dialog.
                    MachineLocks.Set(selectedMachineId, arg != "off");
                    Invalidate(); Refresh(); return "ok unlocked=" + MachineLocks.IsUnlocked(selectedMachineId);
                case "unlockdialog":
                {
                    var m = LiveReader.Read().Machines.FirstOrDefault(x => x.UniqueId == selectedMachineId);
                    if (m == null) return "no machine";
                    ToggleLock(m); return "ok";
                }
                case "exclude":
                    if (arg.StartsWith("d")) excludeDegrading = !excludeDegrading;
                    else if (arg.StartsWith("o")) excludeOvervolted = !excludeOvervolted;
                    Invalidate(); Refresh(); return $"ok degrading={excludeDegrading} overvolted={excludeOvervolted}";
                case "tab":
                    tab = arg == "details" ? Tab.Details : arg == "free" ? Tab.Free : Tab.Modules;
                    Refresh(); return "ok";
                case "bay":
                    bayShows = arg == "result" ? BayShows.Result : BayShows.Current;
                    Refresh(); return "ok";
                case "axis":
                    detailAxis = arg.StartsWith("e") ? Axis.Efficiency : arg.StartsWith("q") ? Axis.Quality : Axis.Performance;
                    Refresh(); return "ok";
                case "target":
                    if (!int.TryParse(arg, out target) || target < 0 || target > 3) target = 0;
                    Invalidate(); Refresh(); return "ok";
                case "time":
                    if (!int.TryParse(arg, out timeIndex) || timeIndex < 0 || timeIndex >= OptimizerTuning.TimeLimitsSeconds.Length)
                        timeIndex = OptimizerTuning.DefaultTimeIndex;
                    Refresh(); return "ok";
                case "search": StartSearch(); return "ok";
                case "stop": session?.Stop(); return "ok";
                case "scroll":
                    // 1 = top, 0 = bottom (ScrollRect.verticalNormalizedPosition).
                    if (scroll == null) return "no scroll";
                    if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return "error: scroll 0-1";
                    {
                        // Moves the content directly (the window keeps its own offset in anchoredPosition.y).
                        float view = scroll.viewport != null ? scroll.viewport.rect.height : ((UnityEngine.RectTransform)scroll.transform).rect.height;
                        float range = UnityEngine.Mathf.Max(0, scroll.content.rect.height - view);
                        var offset = scroll.content.anchoredPosition;
                        offset.y = (1 - UnityEngine.Mathf.Clamp01(v)) * range;
                        scroll.content.anchoredPosition = offset; scroll.StopMovement();
                        return $"ok range={range:0}";
                    }
                case "state":
                    return $"open={IsOpen} machine={selectedMachineId} tab={tab} bay={bayShows} axis={detailAxis} target={target} " +
                           $"time={timeIndex} busy={Busy} result={(result == null ? "none" : result.Phase.ToString())} message={message}";
                default:
                    return "unknown command: " + verb;
            }
        }
    }
}
