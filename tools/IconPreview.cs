using System;
using System.IO;
using ModePuzzle;

/// <summary>Runs IconForge outside the game: raw RGBA in, raw RGBA out (see icon_preview.py).</summary>
static class IconPreview
{
    static Px[] Load(string path)
    {
        var b = File.ReadAllBytes(path);
        var px = new Px[b.Length / 4];
        for (int i = 0; i < px.Length; i++) px[i] = new Px(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]);
        return px;
    }

    static int Main(string[] args)
    {
        if (args.Length != 5) { Console.Error.WriteLine("usage: scanner.raw width height module.raw out.raw"); return 2; }
        int w = int.Parse(args[1]), h = int.Parse(args[2]);
        var result = IconForge.Build(Load(args[0]), w, h, Load(args[3]));
        var bytes = new byte[result.Length * 4];
        for (int i = 0; i < result.Length; i++)
        {
            bytes[i * 4] = result[i].R; bytes[i * 4 + 1] = result[i].G;
            bytes[i * 4 + 2] = result[i].B; bytes[i * 4 + 3] = result[i].A;
        }
        File.WriteAllBytes(args[4], bytes);
        var screen = IconForge.FindScreen(Load(args[0]), w, h);
        Console.WriteLine(screen == null ? "screen: none" : $"screen: {screen.Value.X},{screen.Value.Y} {screen.Value.W}x{screen.Value.H}");
        return 0;
    }
}
