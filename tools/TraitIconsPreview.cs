using System;
using System.IO;
using System.Linq;
using ModePuzzle;
using ModePuzzle.Model;
using ModePuzzle.UI;

static class TraitIconsPreview
{
    static void Save(string path, Px[] pixels)
    {
        using (var f = new BinaryWriter(File.Create(path)))
            foreach (var p in pixels) { f.Write(p.R); f.Write(p.G); f.Write(p.B); f.Write(p.A); }
    }
    static PlacedModule Piece(int x, int y, int rotation, bool flip, int count)
    {
        var p = new PlacedModule { Module = new ModuleInfo(), OriginX = x, OriginY = y, Orientation = rotation, Flipped = flip };
        var shape = count == 1 ? new[] { new Cell(0, 0) } : new[] { new Cell(0, 0), new Cell(0, 1), new Cell(1, 1) };
        foreach (var c in shape)
        {
            int a = flip ? 1 - c.X : c.X, b = c.Y;
            for (int r = 0; r < rotation; r++) { int t = a; a = b; b = 1 - t; }
            p.Cells.Add(new Cell(x + a, y + b));
        }
        return p;
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main(string[] args)
    {
        string output = args[0];
        foreach (TraitIcons.Icon icon in Enum.GetValues(typeof(TraitIcons.Icon)))
            Save(Path.Combine(output, "trait_" + icon + ".rgba"), TraitIcons.Bitmap(icon));
        // Synthetic textured modules: deliberately bright/dark details test backing contrast.
        for (int scene = 0; scene < 3; scene++)
        {
            const int w = 128, h = 64;
            var pixels = Enumerable.Repeat(new Px(30, 28, 36), w * h).ToArray();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (x % 16 == 0 || y % 16 == 0) pixels[y * w + x] = new Px(51, 48, 60);
            for (int n = 0; n < 4; n++)
            {
                var p = Piece(n * 2, 1, n, scene == 1, scene == 2 ? 1 : 3);
                p.Module.Effects.Add(new[] { "MODULE_EFFECT_SIDE_MOUNT", "MODULE_EFFECT_NEGATIVE_FEEDBACK", "MODULE_EFFECT_PREMIUM", "MODULE_EFFECT_DEGRADING" }[n]);
                p.Module.Effects.Add(new[] { "MODULE_EFFECT_TOP_MOUNT", "MODULE_EFFECT_RECEIVER", "MODULE_EFFECT_INFERIOR", "MODULE_EFFECT_LEARNING_ALGORITHM" }[n]);
                p.Module.IsNode = scene == 2 && n == 3;
                foreach (var c in p.Cells)
                    for (int y = 1; y < 15; y++) for (int x = 1; x < 15; x++)
                        pixels[(c.Y * 16 + y) * w + c.X * 16 + x] = (x + y) % 5 == 0 ? new Px(213, 210, 184) : new Px((byte)(75 + n * 25), 105, 80);
                var before = (Px[])pixels.Clone();
                Check(TraitIcons.Stamp(pixels, w, h, p) == (p.Module.IsNode ? 3 : 2), "Missing badges");
                for (int i = 0; i < pixels.Length; i++)
                    if (!p.Cells.Any(c => c.X == i % w / 16 && c.Y == i / w / 16))
                        Check(pixels[i].Same(before[i]) && pixels[i].A == before[i].A, "Escaped module cells");
            }
            Save(Path.Combine(output, "trait_bay" + scene + ".rgba"), pixels);
        }
        // All ten compact variants, including overload and node, shown in real stamp output.
        string[] ids = { "SIDE_MOUNT", "TOP_MOUNT", "NEGATIVE_FEEDBACK", "RECEIVER", "PREMIUM", "INFERIOR", "OVERVOLTED", "DEGRADING", "LEARNING_ALGORITHM" };
        var strip = Enumerable.Repeat(new Px(125, 140, 110), 160 * 16).ToArray();
        for (int i = 0; i < 10; i++)
        {
            var p = Piece(i, 0, 0, false, 1);
            p.Module.Effects.Add("MODULE_EFFECT_" + ids[i % 9]);
            p.Module.Effects.Add("MODULE_EFFECT_" + ids[(i + 1) % 9]);
            p.Module.IsNode = i == 9;
            TraitIcons.Stamp(strip, 160, 16, p);
        }
        Save(Path.Combine(output, "trait_compact.rgba"), strip);
        var test = Piece(0, 0, 0, false, 1);
        test.Module.Effects.AddRange(new[] { "unknown", "MODULE_EFFECT_RECEIVER", "MODULE_EFFECT_RECEIVER", "MODULE_EFFECT_PREMIUM" });
        Check(TraitIcons.ForModule(test.Module).Length == 2, "Unknown/duplicate handling");
        test.Cells.Clear();
        Check(TraitIcons.Stamp(new Px[256], 16, 16, test) == 0, "Empty cells");
        test.Cells.Add(new Cell(-1, 0)); test.Cells.Add(new Cell(int.MaxValue, 0));
        Check(TraitIcons.Stamp(new Px[256], 16, 16, test) == 0, "Out-of-range cells");
        Check(TraitIcons.Stamp(new Px[256], 16, 16, null) == 0, "Null placement");
        Console.WriteLine("Trait icon checks passed: ownership, rotations/flips, 2+node, unknown/duplicate, empty/off-image.");
    }
}
