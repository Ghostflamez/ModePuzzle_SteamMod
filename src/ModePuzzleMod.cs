using System.Linq;
using ModePuzzle.Acquisition;
using ModePuzzle.Game;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>Mod entry point, loaded by the game's ModLoader.</summary>
    public class ModePuzzleMod : IMod
    {
        public const string ModId = "modepuzzle.optimizer";

        internal static ModManifest Manifest;
        internal static ModLog Log;
        static ConfigEntry<bool> devSpawnOnLoad;
        internal static ConfigEntry<bool> DevSmugglerDaily;
        internal static ConfigEntry<bool> DevCrateAlways;
        internal static ConfigEntry<bool> DevJacksonAlways;
        static ConfigEntry<bool> devCratesOnLoad;
        static ConfigEntry<int> devMachinesPerDay;
        const int DevMachinesMax = 10;
        static bool registered;
        static bool debugFunctionsAdded;

        public void Init(ModManifest manifest)
        {
            Manifest = manifest;
            Log = new ModLog(manifest);
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            ModHook.OnShutterOpenedLate += OnShutterOpened;

            // The item directory may already exist when the mod is enabled from the mods menu.
            var directory = Object.FindObjectOfType<ModItemDirectory>();
            if (directory != null) RegisterItems(directory);

            var config = ModHelper.GetConfig(ModId);
            devSpawnOnLoad = config.Define("dev_spawn_on_load", false, "dev",
                () => Loc.T("cfg_dev_spawn"), () => Loc.T("cfg_dev_spawn_tip"));
            DevSmugglerDaily = config.Define("dev_smuggler_daily", false, "dev",
                () => Loc.T("cfg_dev_smuggler"), () => Loc.T("cfg_dev_smuggler_tip"));
            DevCrateAlways = config.Define("dev_crate_always", false, "dev",
                () => Loc.T("cfg_dev_crate"), () => Loc.T("cfg_dev_crate_tip"));
            DevJacksonAlways = config.Define("dev_jackson_always", false, "dev",
                () => Loc.T("cfg_dev_jackson"), () => Loc.T("cfg_dev_jackson_tip"));
            devMachinesPerDay = config.Define("dev_machines_per_day", 0, "dev",
                () => Loc.T("cfg_dev_machine"), 0, DevMachinesMax, () => Loc.T("cfg_dev_machine_tip"));
            devCratesOnLoad = config.Define("dev_crates_on_load", false, "dev",
                () => Loc.T("cfg_dev_crates_on_load"), () => Loc.T("cfg_dev_crates_on_load_tip"));

            JacksonStock.Enable();
            Smuggler.Enable();
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHook.OnShutterOpenedLate -= OnShutterOpened;
            ModHelper.RemoveDebugFunctions(ModId);
            debugFunctionsAdded = false;
            JacksonStock.Disable();
            Smuggler.Disable();
            OptimizerPanel.Close();
        }

        static void RegisterItems(ModItemDirectory directory)
        {
            if (registered && directory.Has(OptimizerItem.Id)) return;
            directory.Set(OptimizerItem.Id, OptimizerItem.Create);
            registered = true;
            Log.Log("Registered item " + OptimizerItem.Id);
        }

        static void OnGameLoaded()
        {
            LogOwnedOptimizers("after load");
            // The mod's localization is loaded after OnEnable, so the labels are added here.
            if (!debugFunctionsAdded)
            {
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_spawn"), SpawnToCounter);
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_smuggler"), Smuggler.QueueNext);
                debugFunctionsAdded = true;
            }
            if (devSpawnOnLoad != null && devSpawnOnLoad.Value) SpawnToCounter();
            if (devCratesOnLoad != null && devCratesOnLoad.Value) SpawnUnlockedCrates();
        }

        /// <summary>Dev test: one engineering and one evidence crate, filled by the game (and our patch)
        /// and unlocked, on the front counter.</summary>
        static void SpawnUnlockedCrates()
        {
            foreach (var crate in new[] { PreBuiltItemHelper.LootCrateEngineering(), PreBuiltItemHelper.LootCrateEvidence() })
            {
                if (crate == null) { Log.Warning("Dev crate: the game returned no crate"); continue; }
                LockHelper.UnlockContainer(crate, true);
                ModHelper.MoveItemToFrontCounter(crate);
                Log.Log($"Dev crate {crate.identifier} placed on the counter, locked={LockHelper.IsCurrentlyLocked(crate)}");
            }
        }

        /// <summary>Dev test: random machines with a module bay each time the shutter opens.</summary>
        static void OnShutterOpened()
        {
            int count = devMachinesPerDay?.Value ?? 0;
            if (count <= 0) return;
            var ids = MachineTable.KnownMachines.ToList();
            for (int i = 0; i < count; i++)
            {
                var id = ids[RNG.GetRandomInt(0, ids.Count - 1)];
                var machine = ModHelper.SpawnItem(id);
                if (machine == null) { Log.Warning("Dev machine: could not spawn " + id); continue; }
                ModHelper.MoveItemToFrontCounter(machine);
                if (machine.parentInventory == null) ModHelper.MoveItemToBackCounter(machine);
                if (machine.parentInventory == null) ModHelper.MoveItemToInventory(machine);
                Log.Log($"Dev machine {id} spawned, placed={machine.parentInventory != null}");
            }
        }

        /// <summary>Diagnostics: where every owned optimizer is.</summary>
        internal static void LogOwnedOptimizers(string when)
        {
            var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
            if (owned == null) { Log.Log($"Optimizers {when}: owned list unavailable"); return; }
            var found = owned.Where(i => i != null && i.identifier == OptimizerItem.Id)
                .Select(i => i.parentInventory?.GetParentItem()?.identifier ?? i.parentInventory?.identifier ?? "?").ToList();
            Log.Log($"Optimizers {when}: {found.Count} owned [{string.Join(", ", found)}]; created {OptimizerItem.CreatedCount}, restored from saves {OptimizerItem.LoadedCount} this session");
        }

        static void SpawnToCounter()
        {
            var item = ModHelper.SpawnItem(OptimizerItem.Id);
            if (item == null) { Log.Warning("Could not spawn " + OptimizerItem.Id); return; }
            ModHelper.MoveItemToFrontCounter(item);
        }
    }
}
