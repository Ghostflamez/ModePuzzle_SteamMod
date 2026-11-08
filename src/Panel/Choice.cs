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
    }
}
