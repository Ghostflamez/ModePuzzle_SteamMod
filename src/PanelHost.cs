namespace ModePuzzle
{
    /// <summary>
    /// The only way the rest of the mod reaches the optimizer window. Release builds compile the panel into this
    /// assembly and call it directly; development builds (DEV) load it from a separate assembly that can be
    /// replaced while the game runs (Dev.HotPanel).
    /// </summary>
    static class PanelHost
    {
#if DEV
        public static void Open() => Dev.HotPanel.Call("Open");
        public static void Close() => Dev.HotPanel.Call("Close");
        public static void ResetSession() => Dev.HotPanel.Call("ResetSession");
#else
        public static void Open() => OptimizerPanel.Open();
        public static void Close() => OptimizerPanel.Close();
        public static void ResetSession() => OptimizerPanel.ResetSession();
#endif
    }
}
