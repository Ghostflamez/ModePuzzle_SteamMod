using System;
using UnityEngine;

namespace ModePuzzle.UI
{
    /// <summary>Reads sprite pixels (top-left origin, as IconForge) and turns pixel arrays back into sprites.</summary>
    static class Pixels
    {
        /// <summary>A sprite's pixels, top-left origin. Atlas textures are CPU-readable (the game reads them to
        /// build item shapes); a GPU copy covers the case where one is not.</summary>
        public static Px[] Read(Sprite sprite, out int w, out int h)
        {
            var r = sprite.textureRect;
            int x = (int)r.x, y = (int)r.y;
            w = (int)r.width;
            h = (int)r.height;
            Color32[] raw;
            try
            {
                raw = Array.ConvertAll(sprite.texture.GetPixels(x, y, w, h), c => (Color32)c);
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

        /// <summary>A point-filtered sprite from top-left pixels (same pivot and pixels-per-unit as mod sprites).</summary>
        public static Sprite ToSprite(Px[] px, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            var colors = new Color32[w * h];
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    var c = px[row * w + col];
                    colors[(h - 1 - row) * w + col] = new Color32(c.R, c.G, c.B, c.A);
                }
            tex.SetPixels32(colors);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        public static void Destroy(Sprite sprite)
        {
            if (sprite == null) return;
            if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }
    }
}
