using HarmonyLib;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// Loot crates are filled by private PreBuiltItemHelper.Fill* methods (hard-coded rolls, not loot
    /// tables), so Harmony patches add one extra roll; the ModLoader applies them when it loads the mod.
    /// In normal play the roll comes after the vanilla rolls (postfix), like an item at the end of the
    /// crate's list, so a full crate destroys it as the game does for its own items. With the dev toggle
    /// 「必出」 it is placed first (prefix) so a test always finds it.
    /// The roll and the add mirror PreBuiltItemHelper.LuckRoll / TryAdd: chance × luck, destroy if no room.
    /// </summary>
    static class CrateRoll
    {
        static bool Forced => ModePuzzleMod.DevCrateAlways?.Value == true;

        public static void Before(GameItem crate) { if (Forced) Add(crate, "dev: always, placed first"); }

        public static void After(GameItem crate, float luckMult, float chance)
        {
            if (Forced || crate == null || !RNG.Roll(chance * luckMult)) return;
            Add(crate, $"roll {chance * luckMult:0.#}%");
        }

        static void Add(GameItem crate, string reason)
        {
            if (crate == null) return;
            var item = DirectoryMaster.Item(OptimizerItem.Id, true);
            if (item == null) { ModePuzzleMod.Log.Warning($"Crate {crate.identifier}: could not create the optimizer"); return; }
            int left = GraphUtils.TryAcceptAll(crate, item);
            if (left > 0) item.Destroy();
            ModePuzzleMod.Log.Log($"Crate {crate.identifier}: optimizer {(left > 0 ? "did not fit, destroyed" : "added")} ({reason})");
        }
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEngineering")]
    static class EngineeringCratePatch
    {
        static void Prefix(GameItem gameItem) => CrateRoll.Before(gameItem);
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.After(gameItem, luckMult, Tuning.EngineeringCrateChance);
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEvidence")]
    static class EvidenceCratePatch
    {
        static void Prefix(GameItem gameItem) => CrateRoll.Before(gameItem);
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.After(gameItem, luckMult, Tuning.EvidenceCrateChance);
    }
}
