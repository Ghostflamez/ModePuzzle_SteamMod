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
            // A stuck or broken module stays where it is: 固定 turns red and reads 卡住 / 损坏, and every button
            // explains why on hover. Otherwise hover explains each choice.
            bool broken = IsBroken(module), pinned = module.Stuck || broken;
            var labels = LockLabels();
            if (pinned) labels[2] = Loc.T(module.Stuck ? "lock_stuck" : "lock_broken");
            var tips = pinned ? Enumerable.Repeat(Loc.T(module.Stuck ? "lock_stuck_hint" : "lock_broken_hint"), 3).ToList()
                : new List<string> { Loc.T("lock_none_hint"), Loc.T("lock_required_hint"), Loc.T("lock_fixed_hint") };
            Choice.Buttons(b, labels, pinned ? 2 : (int)kind,
                value => { locks[module.UniqueId] = (EngineBridge.LockKind)value; Invalidate(); Refresh(); }, !pinned && !Busy, tips);
            if (pinned)
            {
                var red = b.CurrentContainer.GetChild(b.CurrentContainer.childCount - 1);
                var image = red.GetComponent<Image>();
                if (image != null) image.color = PanelStyle.PinnedButton;
                var button = red.GetComponent<Button>();
                if (button != null) { var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors; }
                var text = red.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (text != null) { text.text = labels[2]; text.color = PanelStyle.PinnedText; }
            }
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
        static void Details(CustomUIBuilder b, MachineInfo machine, Scoring.Evaluation evaluation, float width)
        {
            Row(b, width);
            Choice.Buttons(b, Axes(), detailAxis, i => { detailAxis = i; Refresh(); });
            EndRow(b);
            // Icon, "#n name" (plain ink: the card colour is in the icon), five numbers; striped rows, a bold total.
            float value = PanelStyle.DetailColumn, gap = OptimizerTuning.RowGap;
            float name = Mathf.Max(0, width - PanelStyle.IconSlot - 5 * value - 6 * gap);
            var c = Columns(width, gap, PanelStyle.IconSlot, name, value, value, value, value, value);
            string[] heads = { "", Loc.T("panel_module"), Loc.T("detail_base"), Loc.T("detail_self"), Loc.T("detail_absorbed"), Loc.T("detail_receiver"), Loc.T("detail_total") };
            Row(b, width);
            var band = b.CurrentContainer.gameObject.AddComponent<Image>(); band.color = PanelStyle.PanelColor; band.raycastTarget = false;
            for (int i = 0; i < heads.Length; i++)
            {
                var h = Caption(b, heads[i], c[i], i >= 2);
                h.text.enableWordWrapping = false;
                if (i >= 2) NumberMargin(h);
                if (i == 3) h.SetTooltip(Loc.T("detail_self_hint"));
            }
            EndRow(b, false);
            var rows = evaluation.Rows.ToDictionary(r => r.Item.Id);
            var totals = new int[5];
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var m = machine.Installed[i].Module;
                var r = rows["u" + m.UniqueId];
                int[] values = { r.Item.Base[detailAxis], r.Self[detailAxis], r.Absorbed[detailAxis], r.Receiver[detailAxis], r.Total[detailAxis] };
                Row(b, width); b.CurrentContainer.GetComponent<LayoutElement>().minHeight = PanelStyle.RowHeight;
                if (i % 2 == 1) { var stripe = b.CurrentContainer.gameObject.AddComponent<Image>(); stripe.color = PanelStyle.RowStripe; stripe.raycastTarget = false; }
                ModuleIcon(b, m, c[0]);
                var label = Label(b, $"<color=#{PanelStyle.MutedHex}>#{i + 1}</color> {m.Name}", c[1]);
                label.text.enableWordWrapping = false; label.text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                label.SetTooltip(GameText.Describe(m));
                for (int k = 0; k < values.Length; k++) { totals[k] += values[k]; NumberMargin(Label(b, GameText.ColoredValue(values[k]), c[k + 2], true)); }
                CompactRow(b); EndRow(b, false);
            }
            Separator(b, width);
            Row(b, width); Label(b, "", c[0]);
            Label(b, Loc.T("detail_total"), c[1]).text.fontStyle = TMPro.FontStyles.Bold;
            foreach (var pair in totals.Select((v, i) => new { v, i }))
            {
                var t = Label(b, GameText.ColoredValue(pair.v), c[pair.i + 2], true);
                t.text.fontStyle = TMPro.FontStyles.Bold; NumberMargin(t);
            }
            EndRow(b, false);
            int expected = bayShows == BayShows.Result && result?.Feasible == true ? result.Totals[detailAxis] : evaluation.Total[detailAxis];
            if (totals[4] != expected) ModePuzzleMod.Log?.Warning($"Detail total assertion failed: axis={detailAxis}, rows={totals[4]}, dashboard={expected}");
        }
        /// <summary>Width of the 目标 row in the last build (development diagnostics).</summary>
        static float lastDataWidth;
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
        /// <summary>The three search options, one per line.</summary>
        static void SearchOptions(CustomUIBuilder b, float width)
        {
            void Line(string label, string hint, bool on, Action<bool> set)
            {
                Row(b, width);
                SmallToggle(b, label, hint, on, set, Mathf.Min(ToggleWidth(label), width));
                EndRow(b, false);
            }
            Line(Loc.T("opt_exclude_degrading"), Loc.T("opt_exclude_degrading_hint"), excludeDegrading, v => excludeDegrading = v);
            Line(Loc.T("opt_exclude_overvolted"), Loc.T("opt_exclude_overvolted_hint"), excludeOvervolted, v => excludeOvervolted = v);
            Line(Loc.T("opt_only_installed"), null, onlyInstalled, v => onlyInstalled = v);
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

        static Sprite upArrow, downArrow, darkDownArrow, darkUpArrow;
        /// <summary>A small pixel triangle; light for the dark panel, dark for drawing on a light button.</summary>
        static Sprite ArrowSprite(bool up, bool dark = false)
        {
            ref Sprite slot = ref dark ? ref (up ? ref darkUpArrow : ref darkDownArrow) : ref (up ? ref upArrow : ref downArrow);
            if (slot != null) return slot;
            string[] rows = { "...#...", "..###..", ".#####.", "#######" };
            int w = rows[0].Length, h = rows.Length;
            var px = new Px[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (rows[up ? y : h - 1 - y][x] == '#') px[y * w + x] = dark ? PanelStyle.ArrowDark : PanelStyle.ArrowInk;
            slot = Pixels.ToSprite(px, w, h, (dark ? "modepuzzle_dark_" : "modepuzzle_") + (up ? "up" : "down"));
            return slot;
        }
    }
}
