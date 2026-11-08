using System;
using UnityEngine;

namespace ModePuzzle
{
    /// <summary>
    /// Generates the item sprite at runtime from the game's own loaded sprites and registers it under this
    /// mod, so SetSpriteAndShapeFromMod can use it. No game image is shipped with the mod.
    /// </summary>
    static class IconSprite
    {
        public const string Key = "modepuzzle_optimizer";
        public const string ScannerAtlas = "Items/items_amenities";
        public const string ScannerSprite = "genetic_scanner";
        const string ModuleAtlas = "Items/items_module";
        const string ModuleSprite = "module_black_4";   // 神经核心模组（受限）

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
                var module = RenderHandler.TryLoadFromAtlas(ModuleAtlas, ModuleSprite);
                if (scanner == null || module == null) throw new Exception("source sprite not found");

                int w = (int)scanner.rect.width, h = (int)scanner.rect.height;
                var pixels = IconForge.Build(Read(scanner), w, h, Read(module));

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = Key,
                };
                tex.SetPixels32(ToUnity(pixels, w, h));
                tex.Apply();
                // Same pivot and pixels-per-unit as sprites the ModLoader loads from a mod's Textures folder.
                cached = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
                cached.name = Key;
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

        /// <summary>A sprite's pixels, top-left origin. Atlas textures are CPU-readable (the game reads
        /// them to build item shapes); a GPU copy covers the case where one is not.</summary>
        static Px[] Read(Sprite sprite)
        {
            var r = sprite.textureRect;
            int x = (int)r.x, y = (int)r.y, w = (int)r.width, h = (int)r.height;
            Color32[] raw;
            try
            {
                var colors = sprite.texture.GetPixels(x, y, w, h);
                raw = Array.ConvertAll(colors, c => (Color32)c);
            }
            catch (UnityException)
            {
                raw = ReadViaGpu(sprite.texture, x, y, w, h);
            }
            var result = new Px[w * h];
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    var c = raw[(h - 1 - row) * w + col];   // Unity rows run bottom to top
                    result[row * w + col] = new Px(c.r, c.g, c.b, c.a);
                }
            return result;
        }

        static Color32[] ReadViaGpu(Texture2D source, int x, int y, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(x, y, w, h), 0, 0);
                copy.Apply();
                var pixels = copy.GetPixels32();
                UnityEngine.Object.Destroy(copy);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Color32[] ToUnity(Px[] px, int w, int h)
        {
            var result = new Color32[w * h];
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    var c = px[row * w + col];
                    result[(h - 1 - row) * w + col] = new Color32(c.R, c.G, c.B, c.A);
                }
            return result;
        }
    }
}
