using System;
using ModePuzzle.UI;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// Generates the item sprite at runtime from the game's own loaded scanner sprite and registers it under this
    /// mod, so SetSpriteAndShapeFromMod can use it. No game image is shipped with the mod.
    /// </summary>
    static class IconSprite
    {
        public const string Key = "modepuzzle_optimizer";
        // Base sprite: the Deal Maker (谈判专家), recoloured; its screen shows the miniature module.
        public const string ScannerAtlas = "Items/items_tool";
        public const string ScannerSprite = "dealmaker";

        static Sprite cached;
        static bool failed;

        /// <summary>True when the sprite is registered; false falls back to the plain scanner sprite.</summary>
        public static bool Ensure()
        {
            if (cached != null && Register(cached)) return true;
            if (failed) return false;
            try
            {
                var scanner = RenderHandler.TryLoadFromAtlas(ScannerAtlas, ScannerSprite);
                if (scanner == null) throw new Exception("base sprite not found");
                var source = Pixels.Read(scanner, out int w, out int h);
                cached = Pixels.ToSprite(IconForge.Build(source, w, h), w, h, Key);
                return Register(cached);
            }
            catch (Exception e)
            {
                failed = true;
                ModePuzzleMod.Log?.Warning("Icon generation failed, using the scanner sprite: " + e.Message);
                return false;
            }
        }

        static bool Register(Sprite sprite)
        {
            var mod = ModLoader.Mods?.Find(m => m.Manifest != null && m.Manifest.ID == ModePuzzleMod.ModId);
            if (mod == null || mod.Sprites == null) return false;
            mod.Sprites[Key] = sprite;
            return true;
        }
    }
}
