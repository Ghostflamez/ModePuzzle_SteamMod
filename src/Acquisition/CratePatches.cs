using HarmonyLib;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// Loot crates are filled by private PreBuiltItemHelper.Fill* methods (hard-coded rolls, not loot
    /// tables), so a Harmony postfix adds one extra roll. The ModLoader applies these patches when it
    /// loads the mod. The roll and the add mirror PreBuiltItemHelper.LuckRoll / TryAdd: chance × luck,
    /// and an item that does not fit is destroyed.
    /// </summary>
    static class CrateRoll
    {
        public static void Try(GameItem crate, float luckMult, float chance)
        {
            if (crate == null) return;
            if (ModePuzzleMod.DevCrateAlways?.Value != true && !RNG.Roll(chance * luckMult)) return;
            var item = DirectoryMaster.Item(OptimizerItem.Id, true);
            if (item != null && GraphUtils.TryAcceptAll(crate, item) > 0) item.Destroy();
        }
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEngineering")]
    static class EngineeringCratePatch
    {
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.Try(gameItem, luckMult, Tuning.EngineeringCrateChance);
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEvidence")]
    static class EvidenceCratePatch
    {
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.Try(gameItem, luckMult, Tuning.EvidenceCrateChance);
    }
}
