namespace ModePuzzle
{
    /// <summary>Entry points the development host calls by reflection after loading this assembly.</summary>
    static class PanelEntry
    {
        public static void Open() => OptimizerPanel.Open();
        public static void OpenFor(int machineUniqueId) => OptimizerPanel.OpenFor(machineUniqueId);
        public static void Close() => OptimizerPanel.Close();
        public static void ResetSession() => OptimizerPanel.ResetSession();
        public static bool IsOpen() => OptimizerPanel.IsOpen;

        /// <summary>Called before this assembly is replaced: close the window and stop any search.</summary>
        public static void Shutdown() => OptimizerPanel.ResetSession();

        /// <summary>Development commands (open, machine, tab, bay, axis, target, time, search, stop, scroll, state).</summary>
        public static string Command(string line) => OptimizerPanel.DevCommand(line);
    }
}
