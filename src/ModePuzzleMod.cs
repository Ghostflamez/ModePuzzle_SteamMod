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
        internal static ConfigEntry<bool> TraitBadges;
#if DEV
        static ConfigEntry<bool> devSpawnOnLoad;
        internal static ConfigEntry<bool> DevSmugglerDaily;
        internal static ConfigEntry<bool> DevCrateAlways;
        internal static ConfigEntry<bool> DevJacksonAlways;
        internal static ConfigEntry<bool> DevApplyFailure;
        static ConfigEntry<bool> devCratesOnLoad;
        // Counts are dropdowns: the game's settings screen shows no number next to an int slider.
        static ConfigEntry<string> devMachinesPerDay;
        static ConfigEntry<string> devBayKitsPerDay;
        static ConfigEntry<string> devModulesPerDay;
        static ConfigEntry<bool> devFullStorage;
        const string FullStorageId = "hypertech_bay", FullStorageFallbackId = "storage_bay_large";
        /// <summary>Stop filling after this many modules in a row did not fit.</summary>
        const int FullStorageMisses = 12, FullStorageMax = 80;   // leaves room under the 100-piece search limit for installed modules
        static readonly string[] SmallCounts = { "0", "1", "2", "3", "4", "5", "6", "8", "10" };
        static readonly string[] ModuleCounts = { "0", "1", "2", "3", "5", "8", "10", "15", "20", "30" };
        const string BayKitId = "module_bay_expansion_kit";
        // Random universal modules and nodes, as the game's own loot uses them (ItemSpawner.Spawn).
        static readonly string[] RandomModuleIds =
        {
            "random_performance_module", "random_efficiency_module", "random_quality_module",
            "random_overclock_module", "random_eco_module", "random_fineness_module", "random_node",
        };
        static bool debugFunctionsAdded;
#else
        // Release builds have no test switches: these read as off everywhere.
        internal static ConfigEntry<bool> DevSmugglerDaily => null;
        internal static ConfigEntry<bool> DevCrateAlways => null;
        internal static ConfigEntry<bool> DevJacksonAlways => null;
        internal static ConfigEntry<bool> DevApplyFailure => null;
#endif

        public void Init(ModManifest manifest)
        {
            Manifest = manifest;
            Log = new ModLog(manifest);
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnGameLoadedLate += OnGameLoaded;
#if DEV
            ModHook.OnShutterOpenedLate += OnShutterOpened;
#endif

            // The item directory may already exist when the mod is enabled from the mods menu.
            var directory = Object.FindObjectOfType<ModItemDirectory>();
            if (directory != null) RegisterItems(directory);

            var config = ModHelper.GetConfig(ModId);
            TraitBadges = config.Define("trait_badges", true, "display",
                () => Loc.T("cfg_trait_badges"), () => Loc.T("cfg_trait_badges_tip"));
            TraitBadges.OnChanged += _ => UI.TraitOverlay.SettingChanged();
#if DEV
            // Test switches exist in development builds only; release builds leave these entries null (= off).
            devSpawnOnLoad = config.Define("dev_spawn_on_load", false, "dev",
                () => Loc.T("cfg_dev_spawn"), () => Loc.T("cfg_dev_spawn_tip"));
            DevSmugglerDaily = config.Define("dev_smuggler_daily", false, "dev",
                () => Loc.T("cfg_dev_smuggler"), () => Loc.T("cfg_dev_smuggler_tip"));
            DevCrateAlways = config.Define("dev_crate_always", false, "dev",
                () => Loc.T("cfg_dev_crate"), () => Loc.T("cfg_dev_crate_tip"));
            DevJacksonAlways = config.Define("dev_jackson_always", false, "dev",
                () => Loc.T("cfg_dev_jackson"), () => Loc.T("cfg_dev_jackson_tip"));
            DevApplyFailure = config.Define("dev_apply_failure", false, "dev",
                () => Loc.T("cfg_dev_apply_failure"), () => Loc.T("cfg_dev_apply_failure_tip"));
            devMachinesPerDay = config.Define("dev_machines_per_day", "0", "dev",
                () => Loc.T("cfg_dev_machine"), SmallCounts, () => Loc.T("cfg_dev_machine_tip"));
            devBayKitsPerDay = config.Define("dev_bay_kits_per_day", "0", "dev",
                () => Loc.T("cfg_dev_bay_kit"), SmallCounts, () => Loc.T("cfg_dev_open_tip"));
            devFullStorage = config.Define("dev_full_storage", false, "dev",
                () => Loc.T("cfg_dev_full_storage"), () => Loc.T("cfg_dev_full_storage_tip"));
            devModulesPerDay = config.Define("dev_modules_per_day", "0", "dev",
                () => Loc.T("cfg_dev_modules"), ModuleCounts, () => Loc.T("cfg_dev_open_tip"));
            devCratesOnLoad = config.Define("dev_crates_on_load", false, "dev",
                () => Loc.T("cfg_dev_crates_on_load"), () => Loc.T("cfg_dev_crates_on_load_tip"));
#endif

            JacksonStock.Enable();
            Smuggler.Enable();
#if DEV
            Dev.HotPanel.Begin();
#endif
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
#if DEV
            ModHook.OnShutterOpenedLate -= OnShutterOpened;
#endif
            ModHelper.RemoveDebugFunctions(ModId);
            UI.TraitOverlay.Clear();
#if DEV
            debugFunctionsAdded = false;
#endif
            JacksonStock.Disable();
            Smuggler.Disable();
            PanelHost.Close();
#if DEV
            Dev.HotPanel.End();
#endif
        }

        /// <summary>
        /// Every scene load creates a new ModItemDirectory, but DirectoryMaster keeps only the first one and
        /// ignores later instances as duplicates. Directory.Set first removes the id from every registered
        /// directory, so calling it on a later instance unregistered the item from the one the game uses and
        /// saved optimizers were dropped on the second load of a session. Register only when the game cannot
        /// resolve the id, with Add (which never removes).
        /// </summary>
        static void RegisterItems(ModItemDirectory directory)
        {
            if (DirectoryMaster.Has<GameItem>(OptimizerItem.Id)) return;
            directory.Add(OptimizerItem.Id, OptimizerItem.Create);
            Log.Log($"Registered item {OptimizerItem.Id}, resolvable={DirectoryMaster.Has<GameItem>(OptimizerItem.Id)}");
        }

        static void OnGameLoaded()
        {
            PanelHost.ResetSession();
            LogOwnedOptimizers("after load");
#if DEV
            // The mod's localization is loaded after OnEnable, so the labels are added here.
            if (!debugFunctionsAdded)
            {
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_spawn"), SpawnToCounter);
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_smuggler"), Smuggler.QueueNext);
                debugFunctionsAdded = true;
            }
            if (devSpawnOnLoad != null && devSpawnOnLoad.Value) SpawnToCounter();
            if (devCratesOnLoad != null && devCratesOnLoad.Value) SpawnUnlockedCrates();
#endif
        }

#if DEV
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

        /// <summary>Dev test: machines, bay expansion kits and random modules/nodes each time the shutter opens.</summary>
        static void OnShutterOpened()
        {
            var machines = MachineTable.KnownMachines.ToList();
            for (int i = 0; i < Count(devMachinesPerDay); i++) DevGive(machines[RNG.GetRandomInt(0, machines.Count - 1)]);
            for (int i = 0; i < Count(devBayKitsPerDay); i++) DevGive(BayKitId);
            if (devFullStorage?.Value == true) DevFullStorage();
            for (int i = 0; i < Count(devModulesPerDay); i++) DevGive(RandomModuleIds[RNG.GetRandomInt(0, RandomModuleIds.Length - 1)]);
        }

        static int Count(ConfigEntry<string> entry) => int.TryParse(entry?.Value, out int n) && n > 0 ? n : 0;


        /// <summary>Spawns an item for testing: counter, else back counter, else inventory.</summary>
        static GameItem DevGive(string id)
        {
            var item = ModHelper.SpawnItem(id);
            if (item == null) { Log.Warning("Dev: could not spawn " + id); return null; }
            GeneralHelper.SetItemOwned(item, true);
            ModHelper.MoveItemToFrontCounter(item);
            if (item.parentInventory == null) ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory == null) ModHelper.MoveItemToInventory(item);
            Log.Log($"Dev: spawned {id} → {item.identifier}, placed={item.parentInventory != null}");
            return item;
        }

        /// <summary>
        /// Dev test: a 超科技储藏区 (or a large storage bay if that cannot be placed) filled with random modules and
        /// nodes, for long searches with many candidates. Fills until several modules in a row no longer fit.
        /// </summary>
        static void DevFullStorage()
        {
            var storage = DevGive(FullStorageId);
            if (storage == null || storage.parentInventory == null)
            {
                if (storage != null) storage.Destroy();
                storage = DevGive(FullStorageFallbackId);
            }
            if (storage == null || storage.parentInventory == null) { Log.Warning("Dev storage: nowhere to put it"); return; }
            int added = 0, misses = 0;
            while (misses < FullStorageMisses && added < FullStorageMax)
            {
                var module = ModHelper.SpawnItem(RandomModuleIds[RNG.GetRandomInt(0, RandomModuleIds.Length - 1)]);
                if (module == null) { misses++; continue; }
                GeneralHelper.SetItemOwned(module, true);
                if (GraphUtils.TryAcceptAll(storage, module) > 0) { module.Destroy(); misses++; }
                else { added++; misses = 0; }
            }
            Log.Log($"Dev storage {storage.identifier}: {added} modules / nodes added");
        }
#endif

        /// <summary>Diagnostics: where every owned optimizer is.</summary>
        internal static void LogOwnedOptimizers(string when)
        {
            var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
            if (owned == null) { Log.Log($"Optimizers {when}: owned list unavailable"); return; }
            var found = owned.Where(i => i != null && i.identifier == OptimizerItem.Id)
                .Select(i => i.parentInventory?.GetParentItem()?.identifier ?? i.parentInventory?.identifier ?? "?").ToList();
            Log.Log($"Optimizers {when}: {found.Count} owned [{string.Join(", ", found)}]; created {OptimizerItem.CreatedCount}, restored from saves {OptimizerItem.LoadedCount} this session");
        }

#if DEV
        static void SpawnToCounter()
        {
            var item = ModHelper.SpawnItem(OptimizerItem.Id);
            if (item == null) { Log.Warning("Could not spawn " + OptimizerItem.Id); return; }
            ModHelper.MoveItemToFrontCounter(item);
        }
#endif
    }
}
