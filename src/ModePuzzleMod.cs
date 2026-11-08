using ModePuzzle.Acquisition;
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

            JacksonStock.Enable();
            Smuggler.Enable();
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
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
            // The mod's localization is loaded after OnEnable, so the labels are added here.
            if (!debugFunctionsAdded)
            {
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_spawn"), SpawnToCounter);
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_smuggler"), Smuggler.QueueNext);
                debugFunctionsAdded = true;
            }
            if (devSpawnOnLoad != null && devSpawnOnLoad.Value) SpawnToCounter();
        }

        static void SpawnToCounter()
        {
            var item = ModHelper.SpawnItem(OptimizerItem.Id);
            if (item == null) { Log.Warning("Could not spawn " + OptimizerItem.Id); return; }
            ModHelper.MoveItemToFrontCounter(item);
        }
    }
}
