using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModePuzzle.UI
{
    /// <summary>
    /// An item as it is drawn right now: its main sprite and every state sprite on top of it (a hydroponic
    /// system's plants and so on), turned and flipped the way the item is placed. Read from the images under the
    /// item's rotating node and laid out in its non-rotating "middle" node, at the sprites' own pixel size.
    /// Read-only: nothing on the item changes.
    /// </summary>
    static class LiveItemArt
    {
        /// <summary>Top-left RGBA pixels, or null when the item has no drawn sprite.</summary>
        public static Px[] Capture(GameItemElement element, out int width, out int height)
        {
            width = height = 0;
            var middle = element?.middle?.rectTransform;
            var rotator = element?.rotator?.rectTransform;
            if (middle == null || rotator == null) return null;
            var images = new List<Image>();
            foreach (var image in rotator.GetComponentsInChildren<Image>(false))
                if (image.enabled && image.sprite != null && image.color.a > 0.01f) images.Add(image);
            if (images.Count == 0) return null;

            // Pixels per middle-space unit, from the main image (the largest sprite): sprite pixels over its drawn size.
            var main = images[0];
            foreach (var image in images)
                if (image.sprite.rect.width * image.sprite.rect.height > main.sprite.rect.width * main.sprite.rect.height) main = image;
            var mainCorners = Corners(main.rectTransform, middle);
            float drawnW = Vector2.Distance(mainCorners[0], mainCorners[3]), drawnH = Vector2.Distance(mainCorners[0], mainCorners[1]);
            if (drawnW <= 0 || drawnH <= 0) return null;
            float ppu = main.sprite.rect.width / drawnW;

            // The frame is the rotating node's own rectangle as placed.
            var frame = Corners(rotator, middle);
            float minX = Mathf.Min(frame[0].x, frame[2].x), maxX = Mathf.Max(frame[0].x, frame[2].x);
            float minY = Mathf.Min(frame[0].y, frame[2].y), maxY = Mathf.Max(frame[0].y, frame[2].y);
            width = Mathf.RoundToInt((maxX - minX) * ppu);
            height = Mathf.RoundToInt((maxY - minY) * ppu);
            if (width <= 0 || height <= 0 || width > 512 || height > 512) { width = height = 0; return null; }
            var dst = new Px[width * height];

            foreach (var image in images)
            {
                Px[] src;
                int sw, sh;
                try { src = Pixels.Read(image.sprite, out sw, out sh); }
                catch (Exception) { continue; }
                var c = Corners(image.rectTransform, middle);
                Color32 tint = image.color;
                for (int row = 0; row < sh; row++)
                    for (int col = 0; col < sw; col++)
                    {
                        var p = src[row * sw + col];
                        if (p.A == 0) continue;
                        float u = (col + 0.5f) / sw, v = (sh - row - 0.5f) / sh;   // v runs up from the bottom edge
                        Vector2 at = c[0] + u * (c[3] - c[0]) + v * (c[1] - c[0]);
                        int x = Mathf.FloorToInt((at.x - minX) * ppu), y = Mathf.FloorToInt((maxY - at.y) * ppu);
                        if (x < 0 || y < 0 || x >= width || y >= height) continue;
                        var q = new Px((byte)(p.R * tint.r / 255), (byte)(p.G * tint.g / 255), (byte)(p.B * tint.b / 255), (byte)(p.A * tint.a / 255));
                        dst[y * width + x] = q.A == 255 ? q : Over(q, dst[y * width + x]);
                    }
            }
            return dst;
        }

        /// <summary>A rectangle's corners (bottom-left, top-left, top-right, bottom-right) in another node's space.</summary>
        static Vector2[] Corners(RectTransform rect, RectTransform space)
        {
            var world = new Vector3[4];
            rect.GetWorldCorners(world);
            var local = new Vector2[4];
            for (int i = 0; i < 4; i++) local[i] = space.InverseTransformPoint(world[i]);
            return local;
        }

        static Px Over(Px a, Px b)
        {
            int alpha = a.A * 255 + b.A * (255 - a.A);
            if (alpha == 0) return default;
            byte Mix(byte t, byte u) => (byte)((t * a.A * 255 + u * b.A * (255 - a.A) + alpha / 2) / alpha);
            return new Px(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), (byte)((alpha + 127) / 255));
        }
    }
}
