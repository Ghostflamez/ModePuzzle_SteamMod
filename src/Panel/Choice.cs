using System;
using System.Collections.Generic;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Button-based controls. The machinery window style draws labels, buttons, images, toggles and sliders,
    /// but has no dropdown or text input of its own, so every choice in the panel is a row of buttons.
    /// </summary>
    static class Choice
    {
        /// <summary>One button per option; the selected one is bracketed and highlighted, and clicking it does nothing.</summary>
        public static void Buttons(CustomUIBuilder b, IList<string> labels, int selected, Action<int> pick, bool enabled = true)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                bool on = i == selected;
                b.AddButton(on ? $"[ {labels[i]} ]" : labels[i], () => { if (!on) pick(index); });
                b.Last.SetInteractable(enabled);
                if (on && enabled) b.Last.SetTextColor(PanelStyle.Selected);
            }
        }

        /// <summary>
        /// A bound as text ("" = none): −step / −1 / value / +1 / +step and a clear button. An empty bound starts
        /// from <paramref name="start"/> on the first step.
        /// </summary>
        public static void Stepper(CustomUIBuilder b, string value, int step, int start, Action<string> set, string noneText, string clearText, bool enabled)
        {
            int? current = int.TryParse(value, out int n) ? n : (int?)null;
            void Add(string text, int delta)
            {
                b.AddButton(text, () => set(((current ?? start) + (current == null ? 0 : delta)).ToString()));
                b.Last.SetInteractable(enabled);
            }
            Add("-" + step, -step);
            Add("-1", -1);
            b.AddLabel(current?.ToString() ?? noneText);
            b.Last.SetFixedWidth(OptimizerTuning.RangeWidth);
            Add("+1", 1);
            Add("+" + step, step);
            b.AddButton(clearText, () => set(""));
            b.Last.SetInteractable(enabled && current != null);
        }
    }
}
