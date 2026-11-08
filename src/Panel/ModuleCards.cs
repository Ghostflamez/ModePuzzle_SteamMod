using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// Modules as paper cards in two columns, after the web tool's candidate cards: a strip in the module's card
    /// colour, the picture in a dark frame, "#n name", trait chips with their pixel icons, three stat tiles, an
    /// optional status strip and, for installed modules in the current layout, the lock choice.
    /// </summary>
    static partial class OptimizerPanel
    {
        /// <summary>What one card shows besides the module itself.</summary>
        sealed class CardInfo
        {
            public int Number;            // bay number, 0 = none
            public int[] Values;          // efficiency, quality, performance
            public string Status;         // optional status strip text
            public Color StatusColor;
            public bool Locks;            // show 不锁 / 保留 / 固定
        }

        static readonly Dictionary<int, Sprite> chipIcons = new Dictionary<int, Sprite>();

        static void CardGrid(CustomUIBuilder b, IList<ModuleInfo> modules, Func<ModuleInfo, int, CardInfo> info, float width)
        {
            float gap = PanelStyle.CardGap, card = Mathf.Floor((width - gap) / 2);
            for (int i = 0; i < modules.Count; i += 2)
            {
                Row(b, width, gap);
                var group = b.CurrentContainer.GetComponent<HorizontalLayoutGroup>();
                group.childAlignment = TextAnchor.UpperLeft; group.childControlHeight = true; group.childForceExpandHeight = true;
                for (int k = i; k < Math.Min(i + 2, modules.Count); k++) Card(b, modules[k], info(modules[k], k), card);
                EndRow(b, false);
            }
        }

        static void Card(CustomUIBuilder b, ModuleInfo m, CardInfo info, float width)
        {
            float pad = PanelStyle.CardPadding, inner = width - 2 * pad, gap = OptimizerTuning.RowGap;
            Column(b, width);
            var column = b.CurrentContainer;
            var group = column.GetComponent<VerticalLayoutGroup>();
            int p = (int)pad;
            group.padding = new RectOffset(p, p, p + (int)PanelStyle.CardStrip, p); group.spacing = PanelStyle.CardSpacing;
            column.gameObject.AddComponent<Image>().color = PanelStyle.CardPaper;
            // Strip in the module's card colour along the top edge.
            var strip = new GameObject("strip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var sr = (RectTransform)strip.transform; sr.SetParent(column, false);
            strip.GetComponent<LayoutElement>().ignoreLayout = true;
            sr.anchorMin = new Vector2(0, 1); sr.anchorMax = new Vector2(1, 1); sr.pivot = new Vector2(.5f, 1);
            sr.sizeDelta = new Vector2(0, PanelStyle.CardStrip); sr.anchoredPosition = Vector2.zero;
            strip.GetComponent<Image>().color = ModuleArt.CardColorOf(m) ?? PanelStyle.CardStripDefault;
            // Head: framed picture, then "#n name" over the trait chips.
            float frame = PanelStyle.CardIcon;
            Row(b, inner, gap);
            b.CurrentContainer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            CardPicture(b, m, frame);
            Column(b, inner - frame - gap);
            string title = (info.Number > 0 ? $"<color=#{PanelStyle.PaperMutedHex}>#{info.Number}</color> " : "") + m.Name;
            var name = Label(b, title, inner - frame - gap);
            name.SetTextColor(PanelStyle.PaperInk); name.text.fontStyle = FontStyles.Bold;
            name.text.enableWordWrapping = false; name.text.overflowMode = TextOverflowModes.Ellipsis;
            name.SetTooltip(GameText.Describe(m));
            TraitChips(b, m, inner - frame - gap);
            b.End();
            EndRow(b, false);

            // Stat tiles.
            float tile = Mathf.Floor((inner - 2 * gap) / 3);
            string[] labels = { GameText.Efficiency, GameText.Quality, GameText.Performance };
            Row(b, inner, gap);
            for (int k = 0; k < 3; k++)
            {
                // One label per tile: the caption at the left and the bold value at the right of the same line.
                Row(b, tile, 0);
                var bg = b.CurrentContainer.gameObject.AddComponent<Image>(); bg.color = PanelStyle.CardTile; bg.raycastTarget = false;
                var t = Label(b, $"<align=left><size={PanelStyle.CaptionFont}><color=#{PanelStyle.PaperMutedHex}>{labels[k]}</color></size><line-height=0>\n<align=right><b>{PaperValue(info.Values[k])}</b>", tile);
                t.text.margin = new Vector4(6, 0, 6, 0);
                EndRow(b, false);
            }
            EndRow(b, false);
            if (!string.IsNullOrEmpty(info.Status))
            {
                Row(b, inner, 0);
                var s = b.CurrentContainer;
                s.gameObject.AddComponent<Image>().color = info.StatusColor;
                s.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(6, 6, 2, 2);
                var text = Label(b, info.Status, inner - 12, false, PanelStyle.CaptionFont);
                text.SetTextColor(PanelStyle.StatusInk); text.text.fontStyle = FontStyles.Bold;
                EndRow(b, false);
            }
            if (info.Locks) LockButtons(b, m, inner);
            b.End();
        }

        static void CardPicture(CustomUIBuilder b, ModuleInfo m, float frame)
        {
            Row(b, frame, 0);
            var box = b.CurrentContainer;
            box.gameObject.AddComponent<Image>().color = PanelStyle.CardFrame;
            var g = box.GetComponent<HorizontalLayoutGroup>(); g.childAlignment = TextAnchor.MiddleCenter;
            var le = box.GetComponent<LayoutElement>(); le.minHeight = le.preferredHeight = frame;
            var sprite = ModuleArt.SpriteOf(m);
            if (sprite != null)
            {
                float side = Mathf.Max(sprite.rect.width, sprite.rect.height), room = frame - 2 * PanelStyle.CardFrameInset;
                // Every picture fills the frame along its longer side, so tall and small modules look alike in size.
                float scale = room / side;
                b.AddImage(sprite, sprite.rect.width * scale, sprite.rect.height * scale);
            }
            EndRow(b, false);
        }

        /// <summary>One chip per trait: its pixel icon and name on a dark plate; stuck modules get a plain chip.</summary>
        static void TraitChips(CustomUIBuilder b, ModuleInfo m, float width)
        {
            var chips = new List<(Sprite icon, string text)>();
            foreach (var id in m.Effects ?? new List<string>())
                if (TraitIcons.TryEffect(id, out var icon)) chips.Add((ChipIcon(icon), GameText.Effect(id)));
            if (m.IsNode) chips.Add((ChipIcon(TraitIcons.Icon.SupportNode), GameText.Effect("NODE_EFFECT_SUPPORT")));
            if (m.Stuck) chips.Add((null, Loc.T("panel_stuck")));
            if (chips.Count == 0) { Label(b, "", width).SetTextColor(PanelStyle.PaperMuted); return; }
            Row(b, width, PanelStyle.ChipGap);
            foreach (var (icon, text) in chips)
            {
                Row(b, (icon != null ? PanelStyle.ChipIcon + 4 : 0) + TextWidth(text) * PanelStyle.CaptionFont / PanelStyle.BodyFont + 12, 4);
                var chip = b.CurrentContainer;
                chip.gameObject.AddComponent<Image>().color = PanelStyle.ChipPlate;
                chip.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 6, 2, 2);
                if (icon != null) b.AddImage(icon, PanelStyle.ChipIcon, PanelStyle.ChipIcon);
                var label = Label(b, text, TextWidth(text) * PanelStyle.CaptionFont / PanelStyle.BodyFont + 2, false, PanelStyle.CaptionFont);
                label.SetTextColor(PanelStyle.ChipInk); label.text.enableWordWrapping = false;
                EndRow(b, false);
            }
            EndRow(b, false);
        }

        static Sprite ChipIcon(TraitIcons.Icon icon)
        {
            if (chipIcons.TryGetValue((int)icon, out var s) && s != null) return s;
            s = Pixels.ToSprite(TraitIcons.Bitmap(icon), TraitIcons.Side, TraitIcons.Side, "modepuzzle_chip_" + icon);
            chipIcons[(int)icon] = s;
            return s;
        }

        /// <summary>A stat on the paper card: dark green / dark red / muted zero.</summary>
        static string PaperValue(int v) => v == 0 ? $"<color=#{PanelStyle.PaperMutedHex}>0</color>"
            : $"<color=#{(v > 0 ? PanelStyle.PaperPositiveHex : PanelStyle.PaperNegativeHex)}>{GameText.Signed(v)}</color>";
    }
}
