using HarmonyLib;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// Loot crates are filled by private PreBuiltItemHelper.Fill* methods: a fixed list of rolls (LuckRoll), rarer
    /// and more valuable items first, each rolled item added while there is room (TryAdd, otherwise destroyed).
    /// The optimizer (value 500, 3×2 cells) rolls right after an anchor item of similar rank, while the crate is
    /// still nearly empty: a prefix on LuckRoll counts the list's rolls and rolls the optimizer just before the
    /// roll that follows the anchor's. No IL is rewritten. If a game update shortens the list, the roll falls back
    /// to the end; if it moves the anchor, a warning is logged when the anchor shows up elsewhere. With the dev
    /// toggle 「必出」 the optimizer is added before everything instead. Roll and add mirror LuckRoll / TryAdd.
    /// </summary>
    static class CrateRoll
    {
        static bool Forced => ModePuzzleMod.DevCrateAlways?.Value == true;

        // The crate being filled: its luck, where the optimizer rolls, and how far the list has got.
        static GameItem crate;
        static float luck, chance;
        static int slot, rolls;
        static string anchor;
        static bool done;

        public static void Begin(GameItem filling, float luckMult, float rollChance, string anchorId, int rollsBefore)
        {
            crate = filling; luck = luckMult; chance = rollChance; anchor = anchorId; slot = rollsBefore; rolls = 0; done = false;
            if (Forced && crate != null) { Add("dev: always, placed first"); done = true; }
        }

        /// <summary>Before each of the list's rolls: the optimizer's turn comes after <c>slot</c> rolls.</summary>
        public static void BeforeRoll()
        {
            if (crate == null) return;
            if (!done && rolls == slot) Roll();
            rolls++;
        }

        /// <summary>Before each add: checks that the anchor still comes where the optimizer expects it.</summary>
        public static void BeforeAdd(GameItem target, GameItem item)
        {
            if (crate == null || target != crate || item == null || item.identifier != anchor) return;
            if (rolls != slot) ModePuzzleMod.Log?.Warning($"Crate {crate.identifier}: anchor '{anchor}' came after roll {rolls}, expected {slot}; the optimizer rolls after roll {slot}");
        }

        public static void End()
        {
            if (crate != null && !done)
            {
                ModePuzzleMod.Log?.Warning($"Crate {crate.identifier}: only {rolls} rolls (expected more than {slot}); the optimizer rolled at the end");
                Roll();
            }
            crate = null;
        }

        static void Roll()
        {
            done = true;
            if (RNG.Roll(chance * luck)) Add($"roll {chance * luck:0.#}%");
        }

        static void Add(string reason)
        {
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
        static void Prefix(GameItem gameItem, float luckMult) => CrateRoll.Begin(gameItem, luckMult,
            Tuning.EngineeringCrateChance, Tuning.EngineeringCrateAnchor, Tuning.EngineeringCrateRollsBefore);
        static void Postfix() => CrateRoll.End();
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEvidence")]
    static class EvidenceCratePatch
    {
        static void Prefix(GameItem gameItem, float luckMult) => CrateRoll.Begin(gameItem, luckMult,
            Tuning.EvidenceCrateChance, Tuning.EvidenceCrateAnchor, Tuning.EvidenceCrateRollsBefore);
        static void Postfix() => CrateRoll.End();
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "LuckRoll")]
    static class CrateLuckRollPatch
    {
        static void Prefix() => CrateRoll.BeforeRoll();
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "TryAdd")]
    static class CrateTryAddPatch
    {
        static void Prefix(GameItem crate, GameItem item) => CrateRoll.BeforeAdd(crate, item);
    }
}
