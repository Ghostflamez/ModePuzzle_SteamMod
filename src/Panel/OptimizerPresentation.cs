using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ModePuzzle
{
    static partial class OptimizerPanel
    {
        static ScrollRect scroll;
        static Tab builtTab;
        static readonly float[] scrollOffsets = { 0, 0, 0 };
        static CustomUIElement progressElement, elapsedElement;
        static readonly List<CustomUIElement> metricElements = new List<CustomUIElement>();
        static TMP_Text measureText;
        static readonly Stack<float> rowWidths = new Stack<float>();

        // Floor cumulative boundaries: integer widths, no rounding overflow, even with fractional gaps.
        internal static int[] Columns(float total, float gap, params float[] shares)
        {
            var widths = new int[shares.Length];
            if (shares.Length == 0) return widths;
            int available = Mathf.Max(0, Mathf.FloorToInt(total - gap * (shares.Length - 1)));
            double sum = shares.Sum(s => (double)Mathf.Max(0, s)), cumulative = 0;
            int previous = 0;
            for (int i = 0; i < shares.Length; i++)
            {
                cumulative += sum > 0 ? Mathf.Max(0, shares[i]) : 1;
                int end = i == shares.Length - 1 ? available : (int)Math.Floor(available * cumulative / (sum > 0 ? sum : shares.Length));
                widths[i] = end - previous; previous = end;
            }
            return widths;
        }

        static float ContentWidth(float windowWidth)
        {
            var viewport = scroll?.viewport;
            var bar = scroll?.verticalScrollbar?.transform as RectTransform;
            var layout = scroll?.content?.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (viewport == null || bar == null || layout == null || viewport.rect.width <= 0 || bar.rect.width <= 0)
                return Mathf.Max(0, windowWidth - PanelStyle.ChromeFallback);
            return Mathf.Max(0, Mathf.Floor(viewport.rect.width - bar.rect.width - layout.padding.horizontal - PanelStyle.RightSafety));
        }
        static void Fixed(RectTransform t, float width)
        {
            var e = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
            e.minWidth = e.preferredWidth = Mathf.Max(0, width); e.flexibleWidth = 0;
        }
        static void Column(CustomUIBuilder b, float width)
        {
            b.BeginColumn(OptimizerTuning.RowGap, TextAnchor.UpperLeft);
            Fixed(b.CurrentContainer, width);
            var group = b.CurrentContainer.GetComponent<VerticalLayoutGroup>();
            group.childForceExpandWidth = false;
        }
        static void Row(CustomUIBuilder b, float width, float gap = OptimizerTuning.RowGap)
        {
            b.BeginRow(gap, TextAnchor.MiddleLeft);
            Fixed(b.CurrentContainer, width);
            var group = b.CurrentContainer.GetComponent<HorizontalLayoutGroup>();
            group.childForceExpandWidth = false; group.childForceExpandHeight = false;
            rowWidths.Push(width);
        }
        // Measure actual prefab text, constrain every direct child to the local row (never the window).
        static void EndRow(CustomUIBuilder b, bool fit = true)
        {
            float width = rowWidths.Pop();
            var row = b.CurrentContainer;
            var children = new List<RectTransform>();
            var natural = new List<float>();
            for (int i = 0; i < row.childCount; i++)
            {
                var child = (RectTransform)row.GetChild(i);
                var text = child.GetComponentInChildren<TMP_Text>();
                if (text != null && child.GetComponent<Button>() != null)
                {
                    float w = ButtonText(text);
                    if (fit && child != timeButton) Fixed(child, w + PanelStyle.ButtonPadding);
                }
                children.Add(child); natural.Add(Mathf.Max(0, LayoutUtility.GetPreferredWidth(child)));
            }
            if (fit && children.Count > 0)
            {
                float gap = row.GetComponent<HorizontalLayoutGroup>().spacing;
                float used = Mathf.Min(width, natural.Sum() + gap * (children.Count - 1));
                var columns = Columns(used, gap, natural.ToArray());
                for (int i = 0; i < children.Count; i++) Fixed(children[i], columns[i]);
            }
            b.End();
        }
        static void SaveScroll(CustomUIWindow window)
        {
            if (window != null && window.IsAlive && scroll != null)
                scrollOffsets[(int)builtTab] = scroll.content.anchoredPosition.y;
        }
        static CustomUIElement Label(CustomUIBuilder b, string text, float width, bool right = false, float font = PanelStyle.BodyFont)
        {
            b.AddLabel(text);
            var e = b.Last; e.SetFixedWidth(width);
            if (e.text != null)
            {
                measureText = e.text;
                e.text.fontSize = font; e.text.enableAutoSizing = false; e.text.enableWordWrapping = true;
                e.text.overflowMode = TextOverflowModes.Truncate;
                e.text.alignment = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            }
            return e;
        }
        static CustomUIElement Caption(CustomUIBuilder b, string text, float width, bool right = false)
        {
            var e = Label(b, text, width, right, PanelStyle.CaptionFont); e.SetTextColor(PanelStyle.Muted); return e;
        }
        static float TextWidth(string text, bool button = false)
        {
            if (measureText == null) return PanelStyle.StatWidth;
            float old = measureText.fontSize;
            measureText.fontSize = PanelStyle.BodyFont;
            float width = measureText.GetPreferredValues(text).x;
            measureText.fontSize = old;
            return Mathf.Ceil(width + (button ? PanelStyle.ButtonPadding : 0));
        }
        static void Separator(CustomUIBuilder b, float width) { b.AddSeparator(); b.Last.SetFixedWidth(width); }
        static void Button(CustomUIBuilder b, string text, float width, Action click, bool enabled = true)
        {
            b.AddButton(text, click); b.Last.SetFixedWidth(width); b.Last.SetInteractable(enabled);
            if (b.Last.text != null) ButtonText(b.Last.text);
        }
        /// <summary>Button labels stay on one line; returns the label's natural width.</summary>
        static float ButtonText(TMP_Text text)
        {
            text.fontSize = PanelStyle.ButtonFont; text.enableAutoSizing = false;
            text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
            return Mathf.Ceil(text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity).x);
        }
        static void MachineBar(CustomUIBuilder b, Snapshot snap, MachineInfo machine, int index, float width)
        {
            Row(b, width);
            // The focused machine's own sprite first, in the window's top-left corner.
            float art = PanelStyle.MachineArtSize;
            MachineArt(b, machine, art, null, false);
            var name = Label(b, $"{machine.Name} ({index + 1}/{snap.Machines.Count})", width * PanelStyle.MachineNameShare);
            name.text.enableWordWrapping = false; name.text.overflowMode = TextOverflowModes.Ellipsis;
            float nameWidth = Mathf.Min(Mathf.Ceil(name.text.GetPreferredValues(name.text.text, float.PositiveInfinity, float.PositiveInfinity).x) + 2, width * PanelStyle.MachineNameShare);
            string change = Loc.T("panel_change_machine"), refresh = Loc.T("panel_refresh");
            float gap = OptimizerTuning.RowGap, lockWidth = PanelStyle.LockButtonWidth;
            // The status follows the buttons (spinner, then the coloured word), not the far edge of the row.
            string[] states = { "opt_ready", "opt_searching", "opt_stopped", "opt_timeout", "opt_infeasible", "opt_optimal", "opt_failed_tag", "opt_invalid_tag" };
            float status = states.Max(k => TextWidth(Loc.T(k))) + 4;
            name.SetFixedWidth(nameWidth);
            LockButton(b, machine, lockWidth);
            Button(b, change, TextWidth(change, true), OpenPicker);
            Button(b, refresh, TextWidth(refresh, true), UserRefresh);
            Label(b, "", PanelStyle.StatusGap - gap);
            b.AddImage(SpinnerFrame(0), PanelStyle.SpinnerSize, PanelStyle.SpinnerSize);
            spinnerElement = b.Last;
            if (spinnerElement.image != null) spinnerElement.image.color = new Color(1, 1, 1, Busy ? 1 : 0);
            statusElement = Label(b, StatusTag(), status);
            statusElement.text.enableWordWrapping = false; statusElement.text.overflowMode = TextOverflowModes.Ellipsis;
            EndRow(b, false);
        }
        static string StatusTag()
        {
            string state = Status();
            string color = state == Loc.T("opt_failed_tag") || state == Loc.T("opt_invalid_tag") || state == Loc.T("opt_infeasible") ? PanelStyle.NegativeHex :
                state == Loc.T("opt_searching") ? PanelStyle.SearchingHex : state == Loc.T("opt_optimal") ? PanelStyle.PositiveHex :
                state == Loc.T("opt_timeout") || state == Loc.T("opt_stopped") ? PanelStyle.StoppedHex : PanelStyle.MutedHex;
            return $"<color=#{color}>{state}</color>";
        }
        static void ModuleIcon(CustomUIBuilder b, ModuleInfo module, float width)
        {
            Column(b, width);
            var sprite = ModuleArt.SpriteOf(module);
            if (sprite != null)
            {
                float scale = Mathf.Min(width, PanelStyle.IconSize) / Mathf.Max(sprite.rect.width, sprite.rect.height);
                b.AddImage(sprite, sprite.rect.width * scale, sprite.rect.height * scale);
            }
            else Label(b, "", width);
            b.End();
        }
        static void Name(CustomUIBuilder b, ModuleInfo module, float width)
        {
            var e = Label(b, module.Name, width);
            e.text.enableWordWrapping = false; e.text.overflowMode = TextOverflowModes.Ellipsis;
            var card = ModuleArt.CardColorOf(module); if (card.HasValue) e.SetTextColor(card.Value);
            e.SetTooltip(GameText.Describe(module));
        }
        static void Chips(CustomUIBuilder b, ModuleInfo module, float width)
        {
            var traits = module.Effects.Select(GameText.Effect).ToList();
            if (module.IsNode) traits.Add(GameText.Effect("NODE_EFFECT_SUPPORT"));
            if (module.Stuck) traits.Add(Loc.T("panel_stuck"));
            string text = string.Join(" ", traits.Select(t => "[" + t + "]"));
            Caption(b, text, width).SetTooltip(text);
        }
        static void BayImage(CustomUIBuilder b, MachineInfo machine, float width, int scale)
        {
            Pixels.Destroy(baySprite); baySprite = ModuleArt.ComposeBay(machine);
            float w = baySprite.rect.width, h = baySprite.rect.height;
            Row(b, width, 0);
            // The integer minimum is one; clip unusually large modded bays rather than overrun the column.
            var frame = b.CurrentContainer;
            frame.gameObject.AddComponent<RectMask2D>();
            var layout = frame.GetComponent<LayoutElement>(); layout.minHeight = layout.preferredHeight = Mathf.Min(h * scale, PanelStyle.MaxBayHeight);
            b.AddImage(baySprite, w * scale, h * scale);
            EndRow(b, false);
        }
        /// <summary>
        /// The machine's totals as a compact table read across: one row per axis, columns 当前 / 优化后 / 变化 (the last
        /// two only with a result). The target axis is highlighted; the header sits on the table band colour.
        /// </summary>
        static void StatsTable(CustomUIBuilder b, int[] before, int[] after, int axis, float width)
        {
            var labels = after == null ? new[] { "", Loc.T("result_before") } : new[] { "", Loc.T("result_before"), Loc.T("result_after"), Loc.T("result_delta") };
            float name = Axes().Max(a => TextWidth(a)) + PanelStyle.StatNamePad, gap = OptimizerTuning.RowGap;
            var widths = new List<float> { name };
            for (int i = 1; i < labels.Length; i++) widths.Add(PanelStyle.StatColumn);
            float table = Mathf.Min(width, widths.Sum() + gap * (widths.Count - 1));
            var c = Columns(table, gap, widths.ToArray());
            Row(b, table);
            var band = b.CurrentContainer.gameObject.AddComponent<Image>(); band.color = PanelStyle.PanelColor; band.raycastTarget = false;
            for (int i = 0; i < labels.Length; i++) NumberMargin(Caption(b, labels[i], c[i], i > 0));
            EndRow(b, false);
            for (int k = 0; k < Axis.Count; k++)
            {
                Row(b, table);
                var label = Label(b, Axes()[k], c[0]);
                if (k == axis) label.SetTextColor(PanelStyle.Selected);
                var values = new List<string> { GameText.ColoredValue(before[k]) };
                if (after != null) { values.Add(GameText.ColoredValue(after[k])); values.Add(GameText.ColoredValue(after[k] - before[k])); }
                for (int i = 0; i < values.Count; i++) NumberMargin(Label(b, values[i], c[i + 1], true));
                EndRow(b, false);
            }
        }
        static MachineInfo Dashboard(CustomUIBuilder b, MachineInfo machine, float width)
        {
            MachineInfo preview = null;
            if (result?.Feasible == true && transaction != null)
                try { preview = transaction.Preview(result); }
                catch (Exception e) { bayShows = BayShows.Current; message = Loc.T("opt_search_failed"); ModePuzzleMod.Log?.Warning("Preview: " + e); }
            var shown = bayShows == BayShows.Result && preview != null ? preview : machine;
            // The bay column is as wide as the bay drawn at the largest whole scale that fits (pixels stay crisp),
            // never narrower than the cell count and restore button need; the data column takes the rest.
            float bayW = machine.Width * ModuleArt.CellPixels, bayH = machine.Height * ModuleArt.CellPixels;
            float restoreNeed = TextWidth(Loc.T("panel_capacity", 99, 99)) + OptimizerTuning.RowGap + TextWidth(Loc.T("opt_restore"), true);
            int bayScale = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(width * PanelStyle.MaxBayShare / bayW, PanelStyle.MaxBayHeight / bayH)), 1, PanelStyle.MaxBayScale);
            float left = Mathf.Min(Mathf.Max(bayW * bayScale, restoreNeed), width * PanelStyle.MaxBayShare);
            var c = Columns(width, PanelStyle.ColumnGap, left, width - PanelStyle.ColumnGap - left);
            Row(b, width, PanelStyle.ColumnGap);
            b.CurrentContainer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Column(b, c[0]);
            // Reserve the switch slot before feasibility so the bay's top never changes.
            Row(b, c[0]);
            var switchLayout = b.CurrentContainer.GetComponent<LayoutElement>();
            switchLayout.minHeight = switchLayout.preferredHeight = PanelStyle.RowHeight;
            if (preview != null)
            {
                Choice.Buttons(b, new List<string> { Loc.T("bay_current"), Loc.T("opt_result") }, (int)bayShows,
                    i => { bayShows = (BayShows)i; Refresh(); });
                CompactRow(b);
            }
            EndRow(b);
            BayImage(b, shown, c[0], bayScale);
            int used = shown.Installed.SelectMany(p => p.Cells).Count(cell => shown.IsUsable(cell.X, cell.Y));
            string capacity = Loc.T("panel_capacity", used, shown.UsableCount);
            float restoreWidth = TextWidth(Loc.T("opt_restore"), true);
            Row(b, c[0]);
            Caption(b, capacity, Mathf.Max(0, c[0] - restoreWidth - OptimizerTuning.RowGap));
            Button(b, Loc.T("opt_restore"), restoreWidth, RestorePrevious, RestoreAvailable);
            EndRow(b, false);
            Caption(b, Loc.T("panel_accepts", string.Join(Loc.T("panel_list_sep"), machine.Accepts.Select(TypeName))), c[0]);
            b.End();
            Column(b, c[1]);
            int axis = target == 3 ? primary : Targets[target];
            var before = EngineBridge.EvaluateCurrent(machine, new Rules()).Total;
            StatsTable(b, before, preview != null ? result.Totals : null, axis, c[1]);
            Separator(b, c[1]);
            float gap = OptimizerTuning.RowGap;

            // Settings, in the order they are chosen: target (custom bounds open right under it), time, options.
            var targets = new List<string> { GameText.Performance, GameText.Efficiency, GameText.Quality, Loc.T("opt_custom") };
            ChoiceSetting(b, Loc.T("opt_target"), targets, target, i => { target = i; detailAxis = i == 3 ? primary : Targets[i]; Invalidate(); Refresh(); }, c[1]);
            if (target == 3) CustomTarget(b, c[1]);
            SearchOptions(b, c[1]);

            // One action bar: 时长 drop-down, start / stop on the left, apply / restore on the right, each a fixed block.
            float timeWidth = OptimizerTuning.TimeLimitsSeconds.Max(s => TextWidth(Loc.T("opt_seconds", s))) + PanelStyle.DropArrowSpace + PanelStyle.DropPadding;
            float startWidth = timeWidth + gap + TextWidth(Loc.T(preview == null ? "opt_start" : "opt_restart"), true) + gap + TextWidth(Loc.T("opt_stop"), true);
            float applyWidth = preview == null ? 0 : TextWidth(Loc.T("opt_apply"), true);
            float actionsWidth = applyWidth;
            bool oneBar = preview == null || startWidth + 2 * gap + actionsWidth <= c[1];
            Row(b, c[1]);
            Row(b, oneBar && preview != null ? c[1] - actionsWidth - gap : c[1]);
            TimeDropdown(b, timeWidth);
            b.AddButton(Loc.T(preview == null ? "opt_start" : "opt_restart"), StartSearch); b.Last.SetInteractable(!Busy);
            b.AddButton(Loc.T("opt_stop"), () => session?.Stop()); b.Last.SetInteractable(Busy);
            EndRow(b);
            void Actions() { Button(b, Loc.T("opt_apply"), applyWidth, ApplyResult, !Busy && !invalid); applyElement = b.Last; }
            if (preview != null && oneBar) { Row(b, actionsWidth); Actions(); EndRow(b, false); }
            EndRow(b, false);
            if (preview != null && !oneBar) { Row(b, c[1]); Label(b, "", Mathf.Max(0, c[1] - actionsWidth - gap)); Actions(); EndRow(b, false); }

            // Progress, then what the search found.
            Row(b, c[1]);
            b.AddProgressBar(); progressElement = b.Last; progressElement.SetFixedWidth(c[1]);
            EndRow(b, false);
            // Under the bar: one plain line about the search on the left, the time on the right; the heartbeat
            // replaces both texts.
            var under = Columns(c[1], gap, Mathf.Max(0, c[1] - PanelStyle.ProgressLabelWidth - gap), PanelStyle.ProgressLabelWidth);
            Row(b, c[1]);
            metricElements.Add(Caption(b, "", under[0]));
            elapsedElement = Caption(b, "", under[1], true);
            EndRow(b, false);
            UpdateMetrics();
            if (preview != null)
            {
                var oldIds = new HashSet<int>(original.Installed.Select(p => p.Module.UniqueId));
                var newIds = new HashSet<int>(preview.Installed.Select(p => p.Module.UniqueId));
                int moved = preview.Installed.Count(p => oldIds.Contains(p.Module.UniqueId) && !original.Installed.Any(o => o.Module.UniqueId == p.Module.UniqueId &&
                    o.OriginX == p.OriginX && o.OriginY == p.OriginY && o.Orientation == p.Orientation && o.Flipped == p.Flipped));
                Caption(b, Loc.T("opt_counts", moved, oldIds.Except(newIds).Count(), newIds.Except(oldIds).Count()), c[1]);
            }
            messageElement = Label(b, string.Join("\n", new[] { machine.Guessed ? Loc.T("panel_guessed") : null, message }.Where(t => !string.IsNullOrEmpty(t))), c[1]);
            b.End(); EndRow(b, false);
            return shown;
        }
    }
}
