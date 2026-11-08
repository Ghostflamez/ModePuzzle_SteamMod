using System.Collections.Generic;

namespace ModePuzzle.Game
{
    /// <summary>
    /// Which machines are unlocked, as in the external tool: an unlocked machine lends its installed modules to
    /// the optimisation of other machines. Machines start locked. Kept for the game session only (the save is
    /// not touched), in the main assembly so a reloaded panel keeps it.
    /// </summary>
    static class MachineLocks
    {
        static readonly HashSet<int> unlocked = new HashSet<int>();

        /// <summary>The player ticked 不再提示 in the unlock confirmation: no more confirmations until the game closes.</summary>
        public static bool SkipConfirmation;

        public static bool IsUnlocked(int machineUniqueId) => unlocked.Contains(machineUniqueId);

        public static void Set(int machineUniqueId, bool isUnlocked)
        {
            if (isUnlocked) unlocked.Add(machineUniqueId);
            else unlocked.Remove(machineUniqueId);
        }
    }
}
