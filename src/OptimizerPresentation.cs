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
        static View builtView;
        static readonly float[] scrollOffsets = { 0, 0, 0 };
        static CustomUIElement progressElement;
        static readonly List<CustomUIElement> metricElements = new List<CustomUIElement>();

        // Machinery prefabs have generous preferred widths. Bound button rows to the viewport;
        // wrapped captions increase row height instead of pushing the window wider.
        static void FitButtons(CustomUIWindow window, float width)
        {
            foreach (var row in window.root.GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                var buttons = new List<CustomUIElement>();
                float other = 0;
                for (int i = 0; i < row.transform.childCount; i++)
                {
                    var child = row.transform.GetChild(i) as RectTransform;
                    var button = child.GetComponent<Button>();
                    if (button == null) { other += LayoutUtility.GetPreferredWidth(child); continue; }
                    var e = CustomUIElement.Bind(child.gameObject, null);
                    if (e.text != null) { e.text.fontSize = PanelStyle.ButtonFont; e.text.enableAutoSizing = false; }
                    buttons.Add(e);
                }
                if (buttons.Count == 0) continue;
                float available = width - other - row.spacing * (row.transform.childCount - 1);
                float total = buttons.Sum(e => (e.text != null ? e.text.preferredWidth : 0) + PanelStyle.ButtonPadding);
                foreach (var e in buttons)
                {
                    float natural = (e.text != null ? e.text.preferredWidth : 0) + PanelStyle.ButtonPadding;
                    e.SetFixedWidth(Mathf.Min(natural, available * natural / total));
                }
            }
        }

        static void SaveScroll(CustomUIWindow window)
        {
            if (window != null && window.IsAlive && scroll != null)
                scrollOffsets[(int)builtView] = scroll.content.anchoredPosition.y;
        }

        static CustomUIElement Label(CustomUIBuilder b, string text, float width, bool right = false, float font = PanelStyle.BodyFont)
        {
            b.AddLabel(text);
            var e = b.Last;
            e.SetFixedWidth(width);
            if (e.text != null)
            {
                e.text.fontSize = font;
                e.text.enableAutoSizing = false;
                e.text.alignment = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            }
            return e;
        }

        static void Section(CustomUIBuilder b, string key)
        {
            b.AddSpace(PanelStyle.SectionSpace).AddHeader(Loc.T(key));
        }

        static CustomUIElement Tile(CustomUIBuilder b, string caption, string value, float width, bool metric = false)
        {
            b.BeginPanel(width, PanelStyle.TileHeight, TextAnchor.MiddleLeft, PanelStyle.TilePadding, OptimizerTuning.RowGap);
            // BeginPanel creates an Image-backed container with its own vertical layout.
            var image = b.CurrentContainer.GetComponent<Image>();
            if (image != null) image.color = PanelStyle.TileColor;
            float content = width - 2 * PanelStyle.TilePadding;
            Label(b, caption, content, false, PanelStyle.CaptionFont);
            var element = Label(b, value, content, false, metric ? PanelStyle.MetricFont : PanelStyle.ValueFont);
            b.End();
            return element;
        }

        static void ModuleIcon(CustomUIBuilder b, ModuleInfo module)
        {
            b.BeginColumn(0, TextAnchor.MiddleCenter);
            var layout = b.CurrentContainer.GetComponent<LayoutElement>() ?? b.CurrentContainer.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = PanelStyle.IconSlot;
            layout.flexibleWidth = 0;
            var sprite = ModuleArt.SpriteOf(module);
            if (sprite != null)
            {
                float scale = PanelStyle.IconSize / Mathf.Max(sprite.rect.width, sprite.rect.height);
                b.AddImage(sprite, sprite.rect.width * scale, sprite.rect.height * scale);
            }
            else Label(b, "", PanelStyle.IconSlot);
            b.End();
        }

        static void Name(CustomUIBuilder b, ModuleInfo module, float width)
        {
            var e = Label(b, module.Name, width);
            var card = ModuleArt.CardColorOf(module);
            if (card.HasValue) e.SetTextColor(card.Value);
            e.SetTooltip(GameText.Describe(module));
        }

        static void Chips(CustomUIBuilder b, ModuleInfo module, float width)
        {
            var traits = module.Effects.Select(GameText.Effect).ToList();
            if (module.IsNode) traits.Add(GameText.Effect("NODE_EFFECT_SUPPORT"));
            if (module.Stuck) traits.Add(Loc.T("panel_stuck"));
            Label(b, string.Join("  ", traits.Select(t => "[" + t + "]")), width, false, PanelStyle.CaptionFont);
        }

        static void TitleRow(CustomUIBuilder b, ModuleInfo module, int number, float width, bool traits)
        {
            float remaining = width - PanelStyle.NumberWidth - PanelStyle.IconSlot - 3 * OptimizerTuning.RowGap;
            b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
            Label(b, number.ToString(), PanelStyle.NumberWidth, true);
            ModuleIcon(b, module);
            Name(b, module, traits ? remaining / 2 : remaining);
            if (traits) Chips(b, module, remaining / 2);
            b.End();
        }

        static void Legend(CustomUIBuilder b, MachineInfo machine, float width, bool editable)
        {
            if (machine.Installed.Count == 0) b.AddLabel(Loc.T("panel_none"));
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var module = machine.Installed[i].Module;
                TitleRow(b, module, i + 1, width, true);
                if (!editable) continue;
                locks.TryGetValue(module.UniqueId, out var kind);
                b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
                Choice.Buttons(b, new List<string> { Loc.T("opt_lock_none"), Loc.T("opt_lock_required"), Loc.T("opt_lock_fixed") },
                    module.Stuck ? 2 : (int)kind, value => { locks[module.UniqueId] = (EngineBridge.LockKind)value; Invalidate(); Refresh(); },
                    !module.Stuck && !Busy);
                b.End();
            }
        }

        static void BayImage(CustomUIBuilder b, MachineInfo machine, float width, float height)
        {
            Pixels.Destroy(baySprite); baySprite = ModuleArt.ComposeBay(machine);
            float w = baySprite.rect.width, h = baySprite.rect.height;
            float scale = Mathf.Max(1, Mathf.Floor(Mathf.Min(width / w, height / h)));
            b.BeginRow(0, TextAnchor.MiddleCenter);
            var row = b.CurrentContainer.gameObject.AddComponent<LayoutElement>();
            row.minWidth = row.preferredWidth = width;
            b.AddImage(baySprite, w * scale, h * scale).End();
        }

        static void TableRow(CustomUIBuilder b, string caption, IEnumerable<string> values, float width)
        {
            float usable = width - 3 * OptimizerTuning.RowGap;
            float nameWidth = usable * PanelStyle.TableNameShare;
            b.BeginRow(OptimizerTuning.RowGap, TextAnchor.MiddleLeft);
            Label(b, caption, nameWidth);
            foreach (string value in values) Label(b, value, (usable - nameWidth) / 3, true);
            b.End();
        }

        static void ValueRow(CustomUIBuilder b, string key, int[] values, float width, bool omitZero = false)
        {
            if (omitZero && values.All(v => v == 0)) return;
            TableRow(b, Loc.T(key), values.Select(GameText.ColoredValue), width);
        }

        static void Details(CustomUIBuilder b, MachineInfo machine, Scoring.Evaluation evaluation, float width)
        {
            Section(b, "opt_details");
            var rows = evaluation.Rows.ToDictionary(r => r.Item.Id);
            for (int i = 0; i < machine.Installed.Count; i++)
            {
                var module = machine.Installed[i].Module;
                if (!rows.TryGetValue("u" + module.UniqueId, out var row)) continue;
                TitleRow(b, module, i + 1, width, false);
                TableRow(b, Loc.T("detail_item"), Axes(), width);
                ValueRow(b, "detail_base", row.Item.Base, width, true);
                ValueRow(b, "detail_self", row.Self, width, true);
                ValueRow(b, "detail_absorbed", row.Absorbed, width, true);
                ValueRow(b, "detail_receiver", row.Receiver, width, true);
                ValueRow(b, "detail_total", row.Total, width);
                b.AddSeparator();
            }
        }
    }
}
