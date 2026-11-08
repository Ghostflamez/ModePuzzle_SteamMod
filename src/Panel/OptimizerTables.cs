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
        static float ToggleWidth(string label) => TextWidth(label) + PanelStyle.ToggleBox * PanelStyle.ToggleScale + PanelStyle.ToggleTextGap;

        /// <summary>
        /// The game's toggle with its box shrunk to <see cref="PanelStyle.ToggleScale"/>; changing it invalidates the
        /// result and rebuilds the window.
        /// </summary>
        static void SmallToggle(CustomUIBuilder b, string label, string hint, bool on, Action<bool> set, float width)
        {
            b.AddToggle(label, on, v => { set(v); Invalidate(); Refresh(); });
            var e = b.Last;
            e.SetFixedWidth(width); e.SetInteractable(!Busy);
            if (e.text != null) { e.text.fontSize = PanelStyle.BodyFont; e.text.enableAutoSizing = false; e.text.enableWordWrapping = false; }
            if (hint != null) e.SetTooltip(hint);
            // The game's toggle: a root with a 32x32 "Background" box (fixed LayoutElement) whose "Checkmark" stretches
            // over it, then the label. Shrink the box and the root's height together.
            var box = e.toggle?.targetGraphic?.rectTransform;
            if (box == null) return;
            float small = Mathf.Round(PanelStyle.ToggleBox * PanelStyle.ToggleScale);
            var layout = box.GetComponent<LayoutElement>() ?? box.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = layout.minHeight = layout.preferredHeight = small;
            layout.flexibleWidth = layout.flexibleHeight = 0;
            box.sizeDelta = new Vector2(small, small);
            var root = e.root.GetComponent<LayoutElement>();
            if (root != null) root.minHeight = root.preferredHeight = small;
        }
        static void ChoiceSetting(CustomUIBuilder b, string label, IList<string> labels, int selected, Action<int> set, float width)
        {
            Row(b, width); Label(b, label, TextWidth(label)); Choice.Buttons(b, labels, selected, set, !Busy); EndRow(b);
        }
        /// <summary>The three search options; two rows when they do not fit in one.</summary>
        static void SearchOptions(CustomUIBuilder b, float width)
        {
            string degrading = Loc.T("opt_exclude_degrading"), overvolted = Loc.T("opt_exclude_overvolted"), only = Loc.T("opt_only_installed");
            float wd = ToggleWidth(degrading), wo = ToggleWidth(overvolted), wi = ToggleWidth(only);
            bool oneRow = wd + wo + wi + 2 * PanelStyle.SettingsGap <= width;
            Row(b, width, PanelStyle.SettingsGap);
            SmallToggle(b, degrading, Loc.T("opt_exclude_degrading_hint"), excludeDegrading, v => excludeDegrading = v, wd);
            SmallToggle(b, overvolted, Loc.T("opt_exclude_overvolted_hint"), excludeOvervolted, v => excludeOvervolted = v, wo);
            if (!oneRow) { EndRow(b, false); Row(b, width, PanelStyle.SettingsGap); }
            SmallToggle(b, only, null, onlyInstalled, v => onlyInstalled = v, Mathf.Min(wi, width));
            EndRow(b, false);
        }

        /// <summary>
        /// 自定义目标: primary and secondary axis, then one row per axis with its lower and upper bound. A bound is a
        /// number box (empty = no bound) with small up / down arrows beside it (Shift: ±10).
        /// </summary>
        static void CustomTarget(CustomUIBuilder b, float width)
        {
            Caption(b, Loc.T("opt_custom_hint"), width);
            ChoiceSetting(b, Loc.T("opt_primary"), Axes(), primary, i => { primary = i; detailAxis = i; if (secondary == i) secondary = -1; Invalidate(); Refresh(); }, width);
            var secondaryOptions = Axes(); secondaryOptions.Insert(0, Loc.T("opt_none"));
            ChoiceSetting(b, Loc.T("opt_secondary"), secondaryOptions, secondary + 1, i => { if (i - 1 != primary) { secondary = i - 1; Invalidate(); Refresh(); } }, width);

            float label = Mathf.Max(TextWidth(Loc.T("opt_primary")), Axes().Max(a => TextWidth(a)));
            float bound = PanelStyle.BoundInput + PanelStyle.BoundArrow + OptimizerTuning.RowGap;
            float table = Mathf.Min(width, label + 2 * bound + 2 * OptimizerTuning.RowGap);
            Header(b, table, new[] { label, bound, bound }.Select(Mathf.RoundToInt).ToArray(), 0, "", Loc.T("opt_min"), Loc.T("opt_max"));
            for (int k = 0; k < Axis.Count; k++)
            {
                int axis = k;
                Row(b, table);
                var name = Label(b, Axes()[k], label);
                if (k == primary) name.SetTextColor(PanelStyle.Selected);
                BoundInput(b, minimum[k], v => minimum[axis] = v);
                BoundInput(b, maximum[k], v => maximum[axis] = v);
                EndRow(b, false);
            }
        }

        static void BoundInput(CustomUIBuilder b, string value, Action<string> set)
        {
            void Commit(string text)
            {
                text = (text ?? "").Trim();
                if (text == value) return;
                set(text); Invalidate(); Refresh();
            }
            Row(b, PanelStyle.BoundInput + PanelStyle.BoundArrow + OptimizerTuning.RowGap);
            b.AddInput(Loc.T("opt_unbounded"), value, Commit);
            var input = b.Last;
            input.SetFixedWidth(PanelStyle.BoundInput);
            input.SetInteractable(!Busy);
            if (input.input != null)
            {
                input.input.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber;
                input.input.characterLimit = PanelStyle.BoundDigits;
                if (input.input.textComponent != null) { input.input.textComponent.fontSize = PanelStyle.BodyFont; input.input.textComponent.enableAutoSizing = false; }
                if (input.input.placeholder is TMPro.TMP_Text hint) { hint.fontSize = PanelStyle.BodyFont; hint.enableAutoSizing = false; hint.color = PanelStyle.Muted; }
            }
            var layout = input.root.GetComponent<LayoutElement>() ?? input.root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = PanelStyle.BoundHeight;
            // The game's input box is as dark as the panel: a lighter fill and an outline make it visible.
            var fill = input.root.GetComponent<Image>();
            if (fill != null)
            {
                fill.color = PanelStyle.InputFill;
                var outline = fill.GetComponent<Outline>() ?? fill.gameObject.AddComponent<Outline>();
                outline.effectColor = PanelStyle.InputEdge; outline.effectDistance = new Vector2(1, -1);
            }
            b.BeginColumn(0, TextAnchor.MiddleLeft);
            Arrow(b, true, value, Commit);
            Arrow(b, false, value, Commit);
            b.End();
            EndRow(b, false);
        }

        static void Arrow(CustomUIBuilder b, bool up, string value, Action<string> commit)
        {
            b.AddIconButton(ArrowSprite(up), PanelStyle.BoundArrow, () =>
            {
                int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? OptimizerTuning.RangeStep : 1;
                int n = int.TryParse(value, out var v) ? v + (up ? step : -step) : 0;
                commit(n.ToString());
            });
            b.Last.SetInteractable(!Busy);
            var layout = b.Last.root.GetComponent<LayoutElement>() ?? b.Last.root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = PanelStyle.BoundHeight / 2;
            layout.minWidth = layout.preferredWidth = PanelStyle.BoundArrow;
            if (b.Last.image != null) b.Last.image.preserveAspect = true;
        }

        static Sprite upArrow, downArrow;
        static Sprite ArrowSprite(bool up)
        {
            ref Sprite slot = ref up ? ref upArrow : ref downArrow;
            if (slot != null) return slot;
            string[] rows = { "...#...", "..###..", ".#####.", "#######" };
            int w = rows[0].Length, h = rows.Length;
            var px = new Px[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (rows[up ? y : h - 1 - y][x] == '#') px[y * w + x] = PanelStyle.ArrowInk;
            slot = Pixels.ToSprite(px, w, h, up ? "modepuzzle_up" : "modepuzzle_down");
            return slot;
        }
    }
}
