using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    static partial class OptimizerPanel
    {
        static List<string> LockLabels() => new List<string> { Loc.T("opt_lock_none"), Loc.T("opt_lock_required"), Loc.T("opt_lock_fixed") };
        static float ChoiceWidth(IList<string> labels) => labels.Sum(s => TextWidth("[ " + s + " ]", true)) + OptimizerTuning.RowGap * (labels.Count - 1);
        static void LockButtons(CustomUIBuilder b, ModuleInfo module, float width)
        {
            Row(b, width);
            locks.TryGetValue(module.UniqueId, out var kind);
            Choice.Buttons(b, LockLabels(), module.Stuck ? 2 : (int)kind,
                value => { locks[module.UniqueId] = (EngineBridge.LockKind)value; Invalidate(); Refresh(); }, !module.Stuck && !Busy);
            EndRow(b);
        }
        static void Header(CustomUIBuilder b, float width, int[] columns, int numericEnd, params string[] labels)
        {
            Row(b, width);
            var image = b.CurrentContainer.gameObject.AddComponent<Image>(); image.color = PanelStyle.PanelColor; image.raycastTarget = false;
            for (int i = 0; i < labels.Length; i++)
            {
                bool numeric = i >= (labels.Length == 6 ? 2 : 3) && i < numericEnd;
                var e = Caption(b, labels[i], columns[i], numeric);
                if (numeric) NumberMargin(e);
            }
            EndRow(b, false);
        }
        static void NumberMargin(CustomUIElement e)
        {
            if (e.text != null) e.text.margin = new Vector4(0, 0, PanelStyle.NumberMargin, 0);
        }
        static void CompactRow(CustomUIBuilder b)
        {
            foreach (var text in b.CurrentContainer.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                text.enableWordWrapping = false; text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            }
            for (int i = 0; i < b.CurrentContainer.childCount; i++)
            {
                var child = b.CurrentContainer.GetChild(i);
                var layout = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = PanelStyle.RowHeight; layout.flexibleHeight = 0;
            }
        }
        static void ModuleTable(CustomUIBuilder b, MachineInfo machine, List<ModuleInfo> free, Scoring.Evaluation evaluation, float width)
        {
            bool isFree = free != null, editable = !isFree && bayShows == BayShows.Current;
            float gap = OptimizerTuning.RowGap, lockWidth = ChoiceWidth(LockLabels());
            float nameMin = width * PanelStyle.NameShare, traitsMin = width * PanelStyle.TraitShare;
            float fixedWidth = PanelStyle.NumberWidth + PanelStyle.IconSlot + Axis.Count * PanelStyle.StatWidth;
            bool reserveLocks = bayShows == BayShows.Current && fixedWidth + nameMin + traitsMin + lockWidth + 7 * gap <= width;
            // Free rows reuse the shown layout's name/stat/trait widths, omitting number and lock slots.
            float remaining = width - fixedWidth - (reserveLocks ? lockWidth + 7 * gap : 6 * gap);
            float name = Mathf.Clamp(remaining * PanelStyle.NameFlexShare, nameMin, Mathf.Max(nameMin, remaining - traitsMin)), traits = Mathf.Max(traitsMin, remaining - name);
            var shares = new List<float> { PanelStyle.NumberWidth, PanelStyle.IconSlot, name, PanelStyle.StatWidth, PanelStyle.StatWidth, PanelStyle.StatWidth, traits };
            if (reserveLocks) shares.Add(lockWidth);
            bool inlineLocks = editable && reserveLocks;
            var c = Columns(width, gap, shares.ToArray());
            var headers = new List<string> { isFree ? "" : Loc.T("table_number"), Loc.T("table_icon"), Loc.T("panel_module"), GameText.Efficiency, GameText.Quality, GameText.Performance, Loc.T("panel_traits") };
            if (inlineLocks) headers.Add(Loc.T("table_lock"));
            if (isFree) Header(b, width, c.Skip(1).Take(6).ToArray(), 5, headers.Skip(1).Take(6).ToArray());
            else Header(b, width, c, 6, headers.ToArray());
            var modules = isFree ? free.Take(PanelStyle.FreeListMax).ToList() : machine.Installed.Select(p => p.Module).ToList();
            var rows = evaluation?.Rows.ToDictionary(r => r.Item.Id);
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                int[] values = isFree ? new[] { m.Efficiency, m.Quality, m.Performance } : rows["u" + m.UniqueId].Total;
                Row(b, width);
                b.CurrentContainer.GetComponent<LayoutElement>().minHeight = PanelStyle.RowHeight;
                if (!isFree) Label(b, (i + 1).ToString(), c[0], true);
                ModuleIcon(b, m, c[1]); Name(b, m, c[2]);
                for (int k = 0; k < Axis.Count; k++) NumberMargin(Label(b, GameText.ColoredValue(values[k]), c[k + 3], true));
                Chips(b, m, c[6]);
                if (inlineLocks) LockButtons(b, m, c[7]);
                CompactRow(b); EndRow(b, false);
                if (editable && !inlineLocks)
                {
                    float indent = c[0] + c[1] + 2 * gap;
                    var sub = Columns(width, gap, indent - gap, width - indent);
                    Row(b, width); Label(b, "", sub[0]); LockButtons(b, m, sub[1]); EndRow(b, false);
                }
            }
            if (modules.Count == 0) Caption(b, Loc.T("panel_none"), width);
            if (isFree && free.Count > PanelStyle.FreeListMax) Caption(b, Loc.T("panel_more", free.Count - PanelStyle.FreeListMax), width);
        }
        static void Details(CustomUIBuilder b, MachineInfo machine, Scoring.Evaluation evaluation, float width)
        {
            Row(b, width);
            Choice.Buttons(b, Axes(), detailAxis, i => { detailAxis = i; Refresh(); });
            EndRow(b);
            float available = width - PanelStyle.NumberWidth - PanelStyle.IconSlot - 7 * OptimizerTuning.RowGap;
            float name = width * PanelStyle.NameShare, value = Mathf.Max(0, (available - name) / 5);
            var c = Columns(width, OptimizerTuning.RowGap, PanelStyle.NumberWidth, PanelStyle.IconSlot, name, value, value, value, value, value);
            Header(b, width, c, 8, Loc.T("table_number"), Loc.T("table_icon"), Loc.T("panel_module"), Loc.T("detail_base"), Loc.T("detail_self"), Loc.T("detail_absorbed"), Loc.T("detail_receiver"), Loc.T("detail_total"));
            var rows = evaluation.Rows.ToDictionary(r => r.Item.Id);
            var totals = new int[5];
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var m = machine.Installed[i].Module;
                var r = rows["u" + m.UniqueId];
                int[] values = { r.Item.Base[detailAxis], r.Self[detailAxis], r.Absorbed[detailAxis], r.Receiver[detailAxis], r.Total[detailAxis] };
                Row(b, width); b.CurrentContainer.GetComponent<LayoutElement>().minHeight = PanelStyle.RowHeight;
                Label(b, (i + 1).ToString(), c[0], true); ModuleIcon(b, m, c[1]); Name(b, m, c[2]);
                for (int k = 0; k < values.Length; k++) { totals[k] += values[k]; Label(b, GameText.ColoredValue(values[k]), c[k + 3], true); }
                CompactRow(b); EndRow(b, false);
            }
            Row(b, width); Label(b, "", c[0]); Label(b, "", c[1]); Label(b, Loc.T("detail_total"), c[2]);
            foreach (var pair in totals.Select((v, i) => new { v, i })) Label(b, GameText.ColoredValue(pair.v), c[pair.i + 3], true);
            EndRow(b, false);
            int expected = bayShows == BayShows.Result && result?.Feasible == true ? result.Totals[detailAxis] : evaluation.Total[detailAxis];
            if (totals[4] != expected) ModePuzzleMod.Log?.Warning($"Detail total assertion failed: axis={detailAxis}, rows={totals[4]}, dashboard={expected}");
        }
        static void ChoiceSetting(CustomUIBuilder b, string label, IList<string> labels, int selected, Action<int> set, float width)
        {
            Row(b, width); Label(b, label, TextWidth(label)); Choice.Buttons(b, labels, selected, set, !Busy); EndRow(b);
        }
        static void Controls(CustomUIBuilder b, float width)
        {
            var targets = new List<string> { GameText.Performance, GameText.Efficiency, GameText.Quality, Loc.T("opt_custom") };
            var times = OptimizerTuning.TimeLimitsSeconds.Select(s => Loc.T("opt_seconds", s)).ToList();
            float targetWidth = TextWidth(Loc.T("opt_target")) + OptimizerTuning.RowGap + ChoiceWidth(targets);
            float timeWidth = TextWidth(Loc.T("opt_time_limit")) + OptimizerTuning.RowGap + ChoiceWidth(times);
            Action<int> setTarget = i => { target = i; detailAxis = i == 3 ? primary : Targets[i]; Invalidate(); Refresh(); };
            Action<int> setTime = i => { timeIndex = i; Refresh(); };
            if (targetWidth + PanelStyle.SettingsGap + timeWidth <= width)
            {
                var c = Columns(targetWidth + PanelStyle.SettingsGap + timeWidth, PanelStyle.SettingsGap, targetWidth, timeWidth);
                Row(b, width, PanelStyle.SettingsGap);
                ChoiceSetting(b, Loc.T("opt_target"), targets, target, setTarget, c[0]);
                ChoiceSetting(b, Loc.T("opt_time_limit"), times, timeIndex, setTime, c[1]);
                EndRow(b, false);
            }
            else
            {
                ChoiceSetting(b, Loc.T("opt_target"), targets, target, setTarget, width);
                ChoiceSetting(b, Loc.T("opt_time_limit"), times, timeIndex, setTime, width);
            }
            b.AddToggle(Loc.T("opt_only_installed"), onlyInstalled, v => { onlyInstalled = v; Invalidate(); Refresh(); });
            b.Last.SetFixedWidth(width); b.Last.SetInteractable(!Busy);
            if (b.Last.text != null) { b.Last.text.fontSize = PanelStyle.BodyFont; b.Last.text.enableAutoSizing = false; }
            if (target != 3) return;
            ChoiceSetting(b, Loc.T("opt_primary"), Axes(), primary, i => { primary = i; detailAxis = i; if (secondary == i) secondary = -1; Invalidate(); Refresh(); }, width);
            var secondaryOptions = Axes(); secondaryOptions.Insert(0, Loc.T("opt_none"));
            ChoiceSetting(b, Loc.T("opt_secondary"), secondaryOptions, secondary + 1, i => { if (i - 1 != primary) { secondary = i - 1; Invalidate(); Refresh(); } }, width);
            for (int k = 0; k < Axis.Count; k++)
            {
                int axis = k;
                string minLabel = Axes()[k] + " " + Loc.T("opt_min"), maxLabel = Loc.T("opt_max");
                float minWidth = BoundWidth(minLabel), maxWidth = BoundWidth(maxLabel);
                if (minWidth + OptimizerTuning.RowGap + maxWidth <= width)
                {
                    var c = Columns(minWidth + OptimizerTuning.RowGap + maxWidth, OptimizerTuning.RowGap, minWidth, maxWidth);
                    Row(b, width);
                    Bound(b, minLabel, minimum[k], v => { minimum[axis] = v; Invalidate(); Refresh(); }, c[0]);
                    Bound(b, maxLabel, maximum[k], v => { maximum[axis] = v; Invalidate(); Refresh(); }, c[1]);
                    EndRow(b, false);
                }
                else
                {
                    Bound(b, minLabel, minimum[k], v => { minimum[axis] = v; Invalidate(); Refresh(); }, width);
                    Bound(b, Axes()[k] + " " + maxLabel, maximum[k], v => { maximum[axis] = v; Invalidate(); Refresh(); }, width);
                }
            }
        }
        static float BoundWidth(string label) => TextWidth(label) + OptimizerTuning.RangeWidth +
            new[] { "-" + OptimizerTuning.RangeStep, "-1", "+1", "+" + OptimizerTuning.RangeStep, Loc.T("opt_clear") }.Sum(s => TextWidth(s, true)) + 6 * OptimizerTuning.RowGap;
        static void Bound(CustomUIBuilder b, string label, string value, Action<string> set, float width)
        {
            Row(b, width); Label(b, label, TextWidth(label));
            Choice.Stepper(b, value, OptimizerTuning.RangeStep, 0, set, Loc.T("opt_unbounded"), Loc.T("opt_clear"), !Busy);
            // Stepper's numeric label uses the same typography as the rest of the panel.
            foreach (var text in b.CurrentContainer.GetComponentsInChildren<TMPro.TMP_Text>()) { text.fontSize = PanelStyle.BodyFont; text.enableAutoSizing = false; }
            EndRow(b);
        }
    }
}
