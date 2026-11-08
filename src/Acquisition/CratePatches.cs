using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// Loot crates are filled by private PreBuiltItemHelper.Fill* methods: a fixed list of rolls, rarer and
    /// more valuable items first, each added while there is room (otherwise destroyed). The optimizer
    /// (value 500, 2×3 cells) is inserted into that list by a transpiler, right after an anchor item of
    /// similar rank, so it is placed while the crate is still nearly empty. If a game update removes the
    /// anchor, the roll falls back to the end of the list. With the dev toggle 「必出」 it is added before
    /// everything instead. Roll and add mirror PreBuiltItemHelper.LuckRoll / TryAdd.
    /// </summary>
    static class CrateRoll
    {
        static bool Forced => ModePuzzleMod.DevCrateAlways?.Value == true;

        /// <summary>Patches the roll in after the TryAdd that follows <paramref name="anchorId"/>; sets
        /// <paramref name="inserted"/> when it found the spot.</summary>
        public static IEnumerable<CodeInstruction> Insert(IEnumerable<CodeInstruction> instructions, string anchorId, float chance, out bool inserted)
        {
            var list = new List<CodeInstruction>(instructions);
            inserted = false;
            var roll = AccessTools.Method(typeof(CrateRoll), nameof(Inline));
            for (int i = 0; i < list.Count && !inserted; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr || (list[i].operand as string) != anchorId) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (!(list[j].operand is System.Reflection.MethodInfo m) || m.Name != "TryAdd") continue;
                    var first = new CodeInstruction(OpCodes.Ldarg_0);
                    // The anchor's failed roll branches to the label on the next item; move that label onto
                    // our roll so it runs whether or not the anchor was rolled.
                    if (j + 1 < list.Count) first.MoveLabelsFrom(list[j + 1]);
                    list.InsertRange(j + 1, new[]
                    {
                        first,
                        new CodeInstruction(OpCodes.Ldarg_1),
                        new CodeInstruction(OpCodes.Ldc_R4, chance),
                        new CodeInstruction(OpCodes.Call, roll),
                    });
                    inserted = true;
                    break;
                }
            }
            if (!inserted) ModePuzzleMod.Log?.Warning($"Crate list anchor '{anchorId}' not found; the optimizer rolls at the end instead");
            return list;
        }

        public static void Before(GameItem crate) { if (Forced) Add(crate, "dev: always, placed first"); }

        /// <summary>Called from inside the vanilla list.</summary>
        public static void Inline(GameItem crate, float luckMult, float chance)
        {
            if (Forced || crate == null || !RNG.Roll(chance * luckMult)) return;
            Add(crate, $"roll {chance * luckMult:0.#}%");
        }

        /// <summary>Fallback when the anchor was not found.</summary>
        public static void After(bool inserted, GameItem crate, float luckMult, float chance)
        {
            if (!inserted) Inline(crate, luckMult, chance);
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
        static bool inserted;
        static void Prefix(GameItem gameItem) => CrateRoll.Before(gameItem);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            CrateRoll.Insert(code, Tuning.EngineeringCrateAnchor, Tuning.EngineeringCrateChance, out inserted);
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.After(inserted, gameItem, luckMult, Tuning.EngineeringCrateChance);
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEvidence")]
    static class EvidenceCratePatch
    {
        static bool inserted;
        static void Prefix(GameItem gameItem) => CrateRoll.Before(gameItem);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            CrateRoll.Insert(code, Tuning.EvidenceCrateAnchor, Tuning.EvidenceCrateChance, out inserted);
        static void Postfix(GameItem gameItem, float luckMult) => CrateRoll.After(inserted, gameItem, luckMult, Tuning.EvidenceCrateChance);
    }
}
