using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Game;
using ModePuzzle.Model;
using ModePuzzle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle
{
    /// <summary>
    /// 空闲模组 with search and filters, after the web tool's candidate list: a search box (every word must match
    /// the name, a trait, the kind or the lending machine), drop-downs for kind, trait and state, a sort, the count
    /// and 清除筛选. Typing refreshes only the cards (the box keeps its focus), after a short pause.
    /// </summary>
    static partial class OptimizerPanel
    {
        const string NodeKind = "node", NodeEffect = "NODE_EFFECT_SUPPORT";
        enum FreeState { All, Risk, Learning, Borrowed, Used, Unused }
        static string filterText = "", filterKind = "", filterTrait = "";
        static bool filterSpecial;   // only modules made for a particular machine (not universal, not nodes)
        static FreeState filterState;
        static int sortAxis = -1;
        static float filterDue = -1;
        static RectTransform cardsArea;
        static float cardsWidth;

        static bool FilterActive => filterText.Trim().Length > 0 || filterKind != "" || filterTrait != "" || filterState != FreeState.All || filterSpecial;

        static string KindOf(ModuleInfo m) => m.IsNode ? NodeKind : m.ModuleType ?? MachineTable.Universal;
        static string KindName(string kind) => kind == NodeKind ? Loc.T("filter_node") : TypeName(kind);
        static IEnumerable<string> EffectsOf(ModuleInfo m) => (m.Effects ?? new List<string>()).Concat(m.IsNode ? new[] { NodeEffect } : new string[0]);

        /// <summary>The free modules and the modules lent by unlocked machines, with the lender's name for the latter.</summary>
        static List<(ModuleInfo module, string lender)> Candidates()
        {
            var lenders = new Dictionary<int, string>();
            foreach (var other in tabSnap.Machines)
                foreach (var p in other.Installed) lenders[p.Module.UniqueId] = other.Name;
            return tabFits.Select(m => (m, (string)null))
                .Concat(tabBorrowed.Select(m => (m, lenders.TryGetValue(m.UniqueId, out var name) ? name : "?"))).ToList();
        }

        static HashSet<int> UsedIds() => result?.Feasible == true
            ? new HashSet<int>(result.Layout.Select(p => int.TryParse(p.Item.Id.Substring(1), out var id) ? id : -1)) : new HashSet<int>();

        static bool Matches(ModuleInfo m, string lender, HashSet<int> used)
        {
            if (filterKind != "" && KindOf(m) != filterKind) return false;
            if (filterSpecial && (m.IsNode || m.ModuleType == null || m.ModuleType == MachineTable.Universal)) return false;
            var effects = EffectsOf(m).ToList();
            if (filterTrait == "none" ? effects.Count > 0 : filterTrait != "" && !effects.Contains(filterTrait)) return false;
            switch (filterState)
            {
                case FreeState.Risk: if (!effects.Contains(DegradingEffect) && !effects.Contains(OvervoltedEffect)) return false; break;
                case FreeState.Learning: if (!effects.Contains(LearningEffect)) return false; break;
                case FreeState.Borrowed: if (lender == null) return false; break;
                case FreeState.Used: if (!used.Contains(m.UniqueId)) return false; break;
                case FreeState.Unused: if (used.Contains(m.UniqueId)) return false; break;
            }
            var words = filterText.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToList();
            if (words.Count == 0) return true;
            string hay = string.Join("|", new[] { m.Name, KindName(KindOf(m)), lender ?? "" }.Concat(effects.Select(GameText.Effect))).ToLowerInvariant();
            return words.All(hay.Contains);
        }

        static void FreeTab(CustomUIBuilder b, float inner)
        {
            var all = Candidates();
            float gap = OptimizerTuning.RowGap;

            // Search box over the whole width.
            Row(b, inner);
            b.AddInput(Loc.T("filter_search"), filterText, text => { filterText = text ?? ""; filterDue = 0; }, null,
                text => { filterText = text ?? ""; filterDue = Time.unscaledTime + PanelStyle.FilterDelay; });
            var box = b.Last;
            box.SetFixedWidth(inner);
            StyleInput(box);
            EndRow(b, false);

            // Kind / trait / state / sort.
            float drop = Mathf.Floor((inner - 3 * gap) / 4);
            var kinds = all.Select(c => KindOf(c.module)).Distinct().OrderBy(k => k == NodeKind ? 1 : 0).ThenBy(KindName).ToList();
            var kindLabels = new List<string> { Loc.T("filter_kind_all") }; kindLabels.AddRange(kinds.Select(KindName));
            var traits = all.SelectMany(c => EffectsOf(c.module)).Distinct().OrderBy(GameText.Effect).ToList();
            var traitLabels = new List<string> { Loc.T("filter_trait_all") }; traitLabels.AddRange(traits.Select(GameText.Effect)); traitLabels.Add(Loc.T("filter_trait_none"));
            var stateLabels = new List<string> { Loc.T("filter_state_all"), Loc.T("filter_state_risk"), Loc.T("filter_state_learning"),
                Loc.T("filter_state_borrowed"), Loc.T("filter_state_used"), Loc.T("filter_state_unused") };
            var sortLabels = new List<string> { Loc.T("sort_default") }; sortLabels.AddRange(Axes().Select(a => Loc.T("sort_axis", a)));
            int kindIndex = filterKind == "" ? 0 : kinds.IndexOf(filterKind) + 1;
            int traitIndex = filterTrait == "" ? 0 : filterTrait == "none" ? traitLabels.Count - 1 : traits.IndexOf(filterTrait) + 1;
            Row(b, inner);
            Dropdown(b, kindLabels[Mathf.Max(0, kindIndex)], drop, kindLabels, kindIndex,
                i => { filterKind = i == 0 ? "" : kinds[i - 1]; FiltersChanged(); }, Loc.T("filter_kind_hint"), true, true);
            Dropdown(b, traitLabels[Mathf.Max(0, traitIndex)], drop, traitLabels, traitIndex,
                i => { filterTrait = i == 0 ? "" : i == traitLabels.Count - 1 ? "none" : traits[i - 1]; FiltersChanged(); }, Loc.T("filter_trait_hint"), true, true);
            Dropdown(b, stateLabels[(int)filterState], drop, stateLabels, (int)filterState,
                i => { filterState = (FreeState)i; FiltersChanged(); }, Loc.T("filter_state_hint"), true, true);
            Dropdown(b, sortLabels[sortAxis + 1], drop, sortLabels, sortAxis + 1,
                i => { sortAxis = i - 1; FiltersChanged(); }, Loc.T("sort_hint"), true, true);
            EndRow(b);

            // The cards (with the count) in their own container, refreshed while typing.
            Column(b, inner);
            cardsArea = b.CurrentContainer;
            cardsWidth = inner;
            FreeCards(b, inner);
            b.End();
        }

        static void FreeCards(CustomUIBuilder b, float inner)
        {
            var all = Candidates();
            var used = UsedIds();
            var shown = all.Where(c => Matches(c.module, c.lender, used)).ToList();
            if (sortAxis >= 0)
                shown = shown.OrderByDescending(c => sortAxis == 0 ? c.module.Efficiency : sortAxis == 1 ? c.module.Quality : c.module.Performance).ToList();

            // Count, the 仅显示专用模组 switch, and 清除筛选 while a filter is on.
            bool active = FilterActive;
            string count = active ? Loc.T("filter_count_some", shown.Count, all.Count) : Loc.T("filter_count_all", all.Count);
            float gap = OptimizerTuning.RowGap, clear = active ? TextWidth(Loc.T("filter_clear"), true) : 0;
            string special = Loc.T("filter_special");
            float toggle = ToggleWidth(special);
            Row(b, inner);
            Caption(b, count, Mathf.Max(0, inner - toggle - gap - (active ? clear + gap : 0)));
            CompactToggle(b, special, Loc.T("filter_special_hint"), filterSpecial,
                v => { filterSpecial = v; freeShown = PanelStyle.FreeBatch; RefreshCards(); }, toggle, true);
            if (active) Button(b, Loc.T("filter_clear"), clear, ClearFilters);
            EndRow(b, false);

            var list = shown.Take(freeShown).ToList();
            CardGrid(b, list.Select(c => c.module).ToList(), (m, i) =>
            {
                var info = new CardInfo { Values = new[] { m.Efficiency, m.Quality, m.Performance } };
                var lender = list[i].lender;
                if (lender != null) { info.Status = Loc.T("card_borrowed", lender); info.StatusColor = PanelStyle.StatusBorrowed; }
                else if (used.Contains(m.UniqueId)) { info.Status = Loc.T("card_used"); info.StatusColor = PanelStyle.StatusAdded; }
                return info;
            }, inner);
            if (shown.Count > freeShown)
            {
                Row(b, inner);
                b.AddButton(Loc.T("panel_show_more", Mathf.Min(PanelStyle.FreeBatch, shown.Count - freeShown), shown.Count - freeShown),
                    () => { freeShown += PanelStyle.FreeBatch; RefreshCards(); });
                EndRow(b);
            }
            if (shown.Count == 0) Caption(b, Loc.T(all.Count == 0 ? "panel_none" : "filter_empty"), inner);
        }

        static void FiltersChanged()
        {
            freeShown = PanelStyle.FreeBatch;
            SwitchTab(Tab.Free);   // the drop-down labels change too
        }

        static void ClearFilters()
        {
            filterText = filterKind = filterTrait = ""; filterState = FreeState.All; sortAxis = -1; filterSpecial = false;
            FiltersChanged();
        }

        /// <summary>Typing: rebuild the cards a moment after the last key (called with the search heartbeat).</summary>
        internal static void PollFilter()
        {
            if (filterDue < 0 || Time.unscaledTime < filterDue) return;
            filterDue = -1;
            freeShown = PanelStyle.FreeBatch;
            RefreshCards();
        }

        static void RefreshCards()
        {
            var manager = CustomUIManager.Instance;
            var window = manager?.GetWindow(WindowId);
            if (window == null || !window.IsAlive || cardsArea == null || scroll == null || tab != Tab.Free) return;
            float y = scroll.content.anchoredPosition.y;
            for (int i = cardsArea.childCount - 1; i >= 0; i--)
            {
                var child = cardsArea.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            rowWidths.Clear();
            if (measureText == null || !measureText.isActiveAndEnabled) measureText = statusElement?.text;
            var b = new CustomUIBuilder(manager, window, cardsArea);
            cardsArea.gameObject.SetActive(false);
            FreeCards(b, cardsWidth);
            cardsArea.gameObject.SetActive(true);
            CustomUIBuilder.RebuildLayout(window.Rect);
            KeepScroll(y);
        }

        /// <summary>The game's input box is as dark as the panel: a lighter fill and an outline make it visible.</summary>
        static void StyleInput(CustomUIElement input)
        {
            if (input.input != null)
            {
                if (input.input.textComponent != null) { input.input.textComponent.fontSize = PanelStyle.BodyFont; input.input.textComponent.enableAutoSizing = false; }
                if (input.input.placeholder is TMPro.TMP_Text hint) { hint.fontSize = PanelStyle.BodyFont; hint.enableAutoSizing = false; hint.color = PanelStyle.Muted; }
            }
            var layout = input.root.GetComponent<LayoutElement>() ?? input.root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = PanelStyle.BoundHeight;
            var fill = input.root.GetComponent<Image>();
            if (fill == null) return;
            fill.color = PanelStyle.InputFill;
            var outline = fill.GetComponent<Outline>() ?? fill.gameObject.AddComponent<Outline>();
            outline.effectColor = PanelStyle.InputEdge; outline.effectDistance = new Vector2(1, -1);
        }
    }
}
