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
                    if (fit) Fixed(child, w + PanelStyle.ButtonPadding);
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
            MachineArt(b, machine, art, null);
            var name = Label(b, $"{machine.Name} ({index + 1}/{snap.Machines.Count})", width * PanelStyle.MachineNameShare);
            name.text.enableWordWrapping = false; name.text.overflowMode = TextOverflowModes.Ellipsis;
            float nameWidth = Mathf.Min(Mathf.Ceil(name.text.GetPreferredValues(name.text.text, float.PositiveInfinity, float.PositiveInfinity).x) + 2, width * PanelStyle.MachineNameShare);
            string change = Loc.T("panel_change_machine"), refresh = Loc.T("panel_refresh");
            float status = Mathf.Floor(width * PanelStyle.StatusShare), gap = OptimizerTuning.RowGap;
            float lockWidth = PanelStyle.LockButtonWidth;
            float used = art + nameWidth + lockWidth + TextWidth(change, true) + TextWidth(refresh, true) + (art > 0 ? 6 : 5) * gap;
            var c = Columns(width - status - gap - (art > 0 ? art + gap : 0) - lockWidth - gap, gap, nameWidth, TextWidth(change, true), TextWidth(refresh, true),
                Mathf.Max(0, width - status - used));
            name.SetFixedWidth(c[0]);
            LockButton(b, machine, lockWidth);
            Button(b, change, c[1], OpenPicker);
            Button(b, refresh, c[2], UserRefresh);
            Label(b, "", c[3]);
            statusElement = Label(b, StatusTag(), status, true);
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
        static void BayImage(CustomUIBuilder b, MachineInfo machine, float width)
        {
            Pixels.Destroy(baySprite); baySprite = ModuleArt.ComposeBay(machine);
            float w = baySprite.rect.width, h = baySprite.rect.height;
            int scale = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(width / w, PanelStyle.MaxBayHeight / h)), 1, PanelStyle.MaxBayScale);
            Row(b, width, 0);
            // The integer minimum is one; clip unusually large modded bays rather than overrun the column.
            var frame = b.CurrentContainer;
            frame.gameObject.AddComponent<RectMask2D>();
            var layout = frame.GetComponent<LayoutElement>(); layout.minHeight = layout.preferredHeight = Mathf.Min(h * scale, PanelStyle.MaxBayHeight);
            b.AddImage(baySprite, w * scale, h * scale);
            EndRow(b, false);
        }
        static void StatsRow(CustomUIBuilder b, string caption, IEnumerable<string> values, float width, bool header = false)
        {
            var c = Columns(width, OptimizerTuning.RowGap, PanelStyle.StatCaptionShare, PanelStyle.StatShare, PanelStyle.StatShare, PanelStyle.StatShare);
            Row(b, width);
            if (header) Caption(b, caption, c[0]); else Label(b, caption, c[0]);
            int i = 1;
            foreach (var value in values) { if (header) Caption(b, value, c[i++], true); else Label(b, value, c[i++], true); }
            EndRow(b, false);
        }
        static MachineInfo Dashboard(CustomUIBuilder b, MachineInfo machine, float width)
        {
            MachineInfo preview = null;
            if (result?.Feasible == true && transaction != null)
                try { preview = transaction.Preview(result); }
                catch (Exception e) { bayShows = BayShows.Current; message = Loc.T("opt_search_failed"); ModePuzzleMod.Log?.Warning("Preview: " + e); }
            var shown = bayShows == BayShows.Result && preview != null ? preview : machine;
            float left = Mathf.Clamp((machine.Width * ModuleArt.CellPixels + 2 * PanelStyle.Frame) * PanelStyle.MaxBayScale,
                width * PanelStyle.MinBayShare, width * PanelStyle.MaxBayShare);
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
            BayImage(b, shown, c[0]);
            int used = shown.Installed.SelectMany(p => p.Cells).Count(cell => shown.IsUsable(cell.X, cell.Y));
            Caption(b, Loc.T("panel_capacity", shown.Width, shown.Height, used, shown.UsableCount), c[0]);
            Caption(b, Loc.T("panel_accepts", string.Join(Loc.T("panel_list_sep"), machine.Accepts.Select(TypeName))), c[0]);
            b.End();
            Column(b, c[1]);
            int axis = target == 3 ? primary : Targets[target];
            StatsRow(b, "", Axes().Select((name, i) => i == axis ? Loc.T("axis_target", name) : name), c[1], true);
            var before = EngineBridge.EvaluateCurrent(machine, new Rules()).Total;
            StatsRow(b, Loc.T("result_before"), before.Select(GameText.ColoredValue), c[1]);
            if (preview != null)
            {
                StatsRow(b, Loc.T("result_after"), result.Totals.Select(GameText.ColoredValue), c[1]);
                StatsRow(b, Loc.T("result_delta"), result.Totals.Select((v, i) => GameText.ColoredValue(v - before[i])), c[1]);
            }
            Separator(b, c[1]);
            Row(b, c[1]);
            b.AddButton(Loc.T(preview == null ? "opt_start" : "opt_restart"), StartSearch); b.Last.SetInteractable(!Busy);
            b.AddButton(Loc.T("opt_stop"), () => session?.Stop()); b.Last.SetInteractable(Busy);
            EndRow(b);
            var progress = Columns(c[1], OptimizerTuning.RowGap, Mathf.Max(0, c[1] - PanelStyle.ProgressLabelWidth - OptimizerTuning.RowGap), PanelStyle.ProgressLabelWidth);
            Row(b, c[1]);
            b.AddProgressBar(); progressElement = b.Last; progressElement.SetFixedWidth(progress[0]);
            elapsedElement = Label(b, "", progress[1], true, PanelStyle.CaptionFont);
            EndRow(b, false);
            TargetAndTime(b, c[1]);
            string[] keys = { "metric_nodes", "metric_best", "metric_bound", "metric_improvement" };
            int count = c[1] < PanelStyle.MetricRowMinimum ? 2 : keys.Length;
            var metrics = Columns(c[1], OptimizerTuning.RowGap, Enumerable.Repeat(1f, count).ToArray());
            for (int start = 0; start < keys.Length; start += count)
            {
                Row(b, c[1]);
                for (int i = 0; i < count; i++)
                {
                    // Caption and value form one wrapping pair; the heartbeat replaces only this label's text.
                    metricElements.Add(Label(b, "", metrics[i]));
                }
                EndRow(b, false);
            }
            UpdateMetrics();
            Row(b, c[1]);
            string counts = "";
            if (preview != null)
            {
                var oldIds = new HashSet<int>(original.Installed.Select(p => p.Module.UniqueId));
                var newIds = new HashSet<int>(preview.Installed.Select(p => p.Module.UniqueId));
                int moved = preview.Installed.Count(p => oldIds.Contains(p.Module.UniqueId) && !original.Installed.Any(o => o.Module.UniqueId == p.Module.UniqueId &&
                    o.OriginX == p.OriginX && o.OriginY == p.OriginY && o.Orientation == p.Orientation && o.Flipped == p.Flipped));
                counts = Loc.T("opt_counts", moved, oldIds.Except(newIds).Count(), newIds.Except(oldIds).Count());
            }
            float restoreWidth = TextWidth(Loc.T("opt_restore"), true), applyWidth = preview == null ? 0 : TextWidth(Loc.T("opt_apply"), true);
            var actions = preview == null ? Columns(c[1], OptimizerTuning.RowGap, Mathf.Max(0, c[1] - restoreWidth - OptimizerTuning.RowGap), restoreWidth) :
                Columns(c[1], OptimizerTuning.RowGap, Mathf.Max(0, c[1] - restoreWidth - applyWidth - 2 * OptimizerTuning.RowGap), applyWidth, restoreWidth);
            Caption(b, counts, actions[0]);
            if (preview != null) { Button(b, Loc.T("opt_apply"), actions[1], ApplyResult, !Busy && !invalid); applyElement = b.Last; }
            Button(b, Loc.T("opt_restore"), actions[actions.Length - 1], RestorePrevious, RestoreAvailable);
            EndRow(b, false);
            messageElement = Label(b, string.Join("\n", new[] { machine.Guessed ? Loc.T("panel_guessed") : null, message }.Where(t => !string.IsNullOrEmpty(t))), c[1]);
            b.End(); EndRow(b, false);
            return shown;
        }
    }
}
