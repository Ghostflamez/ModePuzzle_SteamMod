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
                case "tooltip":
                {
                    // The same call ItemHoverTooltip makes each frame, once, for a screenshot.
                    var snap = LiveReader.Read();
                    if (!int.TryParse(arg, out int n) || n < 1 || n > snap.Machines.Count) return "usage: tooltip <1-based index>";
                    var element = LiveReader.MachineItem(snap.Machines[n - 1].UniqueId) as GameItemElement;
                    if (element == null || OverlayHandler.current == null) return "no element";
                    OverlayHandler.current.ShowTooltip(new UnityEngine.Vector2(UnityEngine.Screen.width * .3f, UnityEngine.Screen.height * .8f),
                        new UnityEngine.Vector2(32, 0), element.GetTooltip(), 72);
                    return "ok";
                }
                case "dialogcancel":
                    if (CustomUIManager.Instance?.IsOpen(ConfirmId) != true) return "no dialog";
                    CustomUIManager.Instance.CloseWindow(ConfirmId); return "ok";
                case "hover":
                {
                    // Pretends the pointer rests on the machine's picture (ItemHoverTooltip.hovered), or stops.
                    var field = typeof(ModePuzzle.UI.ItemHoverTooltip).GetField("hovered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var snap = LiveReader.Read();
                    int id = int.TryParse(arg, out int n) && n >= 1 && n <= snap.Machines.Count ? snap.Machines[n - 1].UniqueId : -1;
                    int count = 0;
                    foreach (var h in UnityEngine.Object.FindObjectsOfType<ModePuzzle.UI.ItemHoverTooltip>())
                    {
                        bool on = h.Item != null && h.Item.uniqueId == id && count == 0;
                        if (on) count++;
                        if (on) field.SetValue(h, true); else if ((bool)field.GetValue(h)) h.OnPointerExit(null);
                    }
                    return "ok " + count;
                }
                case "inputtest":
                {
                    var manager = CustomUIManager.Instance;
                    if (manager.IsOpen("modepuzzle.dev.input")) { manager.CloseWindow("modepuzzle.dev.input"); return "closed"; }
                    var w = manager.CreateWindow("modepuzzle.dev.input", "dev", CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery).SetSize(400, 220);
                    w.AddInput("placeholder", "123", null);
                    var el = w.Last;
                    w.Center(); w.Show();
                    return $"input={(el.input != null)} root={el.root?.name} children={el.root?.transform.childCount}";
                }
                case "timemenu":
                    if (arg == "close") CloseTimeMenu(); else OpenTimeMenu();
                    return "ok";
                case "closewin":
                    CustomUIManager.Instance?.CloseWindow(arg);
                    return "ok";
                case "dumptoggle":
                {
                    // Hierarchy of a fresh game toggle: names, sizes, anchors and components.
                    var manager = CustomUIManager.Instance;
                    var w = manager.CreateWindow("modepuzzle.dev.toggle", "dev", CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Machinery).SetSize(400, 200);
                    w.AddToggle("dev", false, null);
                    var root = w.Last.root.transform;
                    w.Show(); UnityEngine.Canvas.ForceUpdateCanvases();
                    var sb = new System.Text.StringBuilder();
                    void Walk(UnityEngine.Transform t, int depth)
                    {
                        var r = t as UnityEngine.RectTransform;
                        sb.Append(new string(' ', depth * 2)).Append(t.name);
                        if (r != null) sb.Append($" size={r.rect.width:0}x{r.rect.height:0} aMin={r.anchorMin} aMax={r.anchorMax} sd={r.sizeDelta}");
                        sb.Append(" [").Append(string.Join(",", t.GetComponents<UnityEngine.Component>().Select(c => c.GetType().Name))).Append("]");
                        var le = t.GetComponent<UnityEngine.UI.LayoutElement>();
                        if (le != null) sb.Append($" LE min={le.minWidth}x{le.minHeight} pref={le.preferredWidth}x{le.preferredHeight}");
                        sb.Append(" | ");
                        foreach (UnityEngine.Transform c in t) Walk(c, depth + 1);
                    }
                    Walk(root, 0);
                    var tg = w.Last.toggle;
                    sb.Append($" target={tg?.targetGraphic?.name} graphic={tg?.graphic?.name}");
                    manager.CloseWindow("modepuzzle.dev.toggle");
                    return sb.ToString();
                }
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
