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
        static bool registered;

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
            ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, Loc.T("debug_spawn"), SpawnToCounter);
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHelper.RemoveDebugFunctions(ModId);
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
