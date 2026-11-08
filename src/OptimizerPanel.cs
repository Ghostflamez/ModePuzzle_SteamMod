namespace ModePuzzle
{
    /// <summary>The optimizer window, built with the game's CustomUI builder. Placeholder for now.</summary>
    static class OptimizerPanel
    {
        const string WindowId = "modepuzzle.optimizer.panel";
        const float Width = 560f, Height = 380f;

        public static void Open()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var open = manager.GetWindow(WindowId);
            if (open != null && open.IsAlive) { open.Show(); open.BringToFront(); return; }

            CustomUIManager.Window(WindowId, Loc.T("panel_title"))
                .SetSize(Width, Height)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .AddHeader(Loc.T("panel_header"))
                .AddLabel(Loc.T("panel_wip"))
                .AddFlexibleSpace()
                .AddLabel(Loc.T("panel_version", ModePuzzleMod.Manifest?.ModVersion ?? "?"))
                .Center()
                .Show();
        }

        public static void Close()
        {
            var manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(WindowId)) manager.CloseWindow(WindowId);
        }
    }
}
