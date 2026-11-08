using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Model;
using UnityEngine;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Module pictures from the game's own sprites, read at runtime: each module's item sprite, its card colour,
    /// and the bay drawn as one pixel image (the game's bay cell plus every installed module, flipped and turned
    /// the way the game placed it). Nothing is shipped with the mod.
    /// </summary>
    static class ModuleArt
    {
        /// <summary>Pixels per bay cell in the game's item art.</summary>
        public const int CellPixels = 16;

        sealed class Art
        {
            public Sprite Sprite;
            public Px[] Pixels;
            public int W, H;
            public Color Card;
        }

        static readonly Dictionary<string, Art> cache = new Dictionary<string, Art>();
        static Px[] cellPixels;
        static bool cellRead;

        static string Key(ModuleInfo m) => (m.SpriteFromMod ? "mod:" : "") + m.SpriteAtlas + "/" + m.SpriteName;

        static Art Get(ModuleInfo m)
        {
            if (string.IsNullOrEmpty(m.SpriteName)) return null;
            var key = Key(m);
            if (cache.TryGetValue(key, out var art)) return art;
            art = null;
            try
            {
                var sprite = m.SpriteFromMod
                    ? CustomUIManager.Instance?.GetModSprite(m.SpriteAtlas, m.SpriteName, true)
                    : RenderHandler.TryLoadFromAtlas(m.SpriteAtlas, m.SpriteName);
                if (sprite != null)
                {
                    var px = Pixels.Read(sprite, out int w, out int h);
                    art = new Art { Sprite = sprite, Pixels = px, W = w, H = h, Card = CardColor(px) };
                }
            }
            catch (Exception e)
            {
                ModePuzzleMod.Log?.Warning($"Module sprite {key} unreadable: {e.Message}");
            }
            cache[key] = art;
            return art;
        }

        public static Sprite SpriteOf(ModuleInfo m) => Get(m)?.Sprite;

        /// <summary>The module's main colour: the most common saturated, mid-bright colour of its sprite.</summary>
        public static Color? CardColorOf(ModuleInfo m) => Get(m)?.Card;

        static Color CardColor(Px[] px)
        {
            var counts = new Dictionary<int, int>();
            foreach (var c in px)
            {
                if (!c.Opaque) continue;
                int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                if (max < 90 || max - min < 40) continue;   // outlines, shadows and greys
                int key = (c.R << 16) | (c.G << 8) | c.B;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            if (counts.Count == 0) return new Color32(0xc4, 0xc4, 0xb0, 255);
            int best = counts.OrderByDescending(kv => kv.Value).First().Key;
            return new Color32((byte)(best >> 16), (byte)(best >> 8), (byte)best, 255);
        }

        static Px[] Cell()
        {
            if (cellRead) return cellPixels;
            cellRead = true;
            try
            {
                var s = RenderHandler.TryLoadFromAtlas("Window/machinery", "cell");
                if (s != null && (int)s.textureRect.width == CellPixels) cellPixels = Pixels.Read(s, out _, out _);
            }
            catch (Exception) { cellPixels = null; }
            return cellPixels;
        }

        /// <summary>
        /// The bay as one image, 16 px per cell: the game's cell sprite under usable cells, each installed module's
        /// sprite transformed like GridShape.ForwardTransform (flip, quarter turns, offset) at pixel scale.
        /// Modules without a readable sprite are filled with their card colour or a neutral tone.
        /// </summary>
        public static Sprite ComposeBay(MachineInfo m)
        {
            int W = m.Width * CellPixels, H = m.Height * CellPixels;
            var dst = new Px[W * H];
            var cell = Cell();
            for (int cy = 0; cy < m.Height; cy++)
                for (int cx = 0; cx < m.Width; cx++)
                {
                    if (!m.IsUsable(cx, cy)) continue;
                    for (int y = 0; y < CellPixels; y++)
                        for (int x = 0; x < CellPixels; x++)
                            dst[(cy * CellPixels + y) * W + cx * CellPixels + x] = cell != null ? cell[y * CellPixels + x] : new Px(32, 30, 36, 204);
                }

            foreach (var p in m.Installed)
            {
                var art = Get(p.Module);
                if (art != null && art.W == p.Module.ShapeWidth * CellPixels && art.H == p.Module.ShapeHeight * CellPixels)
                    Blit(dst, W, H, art, p);
                else
                    FillCells(dst, W, H, p, art?.Card ?? new Color(0.77f, 0.77f, 0.69f));
            }
            return Pixels.ToSprite(dst, W, H, "modepuzzle_bay");
        }

        static void Blit(Px[] dst, int W, int H, Art art, PlacedModule p)
        {
            int sw = art.W, sh = art.H;
            for (int y0 = 0; y0 < sh; y0++)
                for (int x0 = 0; x0 < sw; x0++)
                {
                    var c = art.Pixels[y0 * sw + x0];
                    if (c.A == 0) continue;
                    int x = p.Flipped ? sw - 1 - x0 : x0, y = y0;
                    switch (p.Orientation)
                    {
                        case 1: { int t = x; x = y; y = sw - 1 - t; break; }
                        case 2: x = sw - 1 - x; y = sh - 1 - y; break;
                        case 3: { int t = x; x = sh - 1 - y; y = t; break; }
                    }
                    int gx = p.OriginX * CellPixels + x, gy = p.OriginY * CellPixels + y;
                    if (gx < 0 || gy < 0 || gx >= W || gy >= H) continue;
                    dst[gy * W + gx] = c.A == 255 ? c : Over(c, dst[gy * W + gx]);
                }
        }

        static void FillCells(Px[] dst, int W, int H, PlacedModule p, Color color)
        {
            var c = new Px((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255));
            foreach (var cell in p.Cells)
                for (int y = 1; y < CellPixels - 1; y++)
                    for (int x = 1; x < CellPixels - 1; x++)
                    {
                        int gx = cell.X * CellPixels + x, gy = cell.Y * CellPixels + y;
                        if (gx < W && gy < H) dst[gy * W + gx] = c;
                    }
        }

        static Px Over(Px top, Px under)
        {
            float a = top.A / 255f, b = under.A / 255f * (1 - a), out_a = a + b;
            if (out_a <= 0) return default;
            byte Mix(byte t, byte u) => (byte)((t * a + u * b) / out_a);
            return new Px(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B), (byte)(out_a * 255));
        }
    }
}
