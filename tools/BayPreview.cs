// Minimal host for the production bay renderer. No Unity runtime or game assets loaded.
using System;
using System.IO;
using System.Linq;
using ModePuzzle;
using ModePuzzle.Model;
using ModePuzzle.UI;
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r=r; this.g=g; this.b=b; this.a=a; }
    }
    public struct Color32
    {
        byte r,g,b,a;
        public Color32(byte r,byte g,byte b,byte a) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public static implicit operator Color(Color32 c) => new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
    }
    public class Sprite { public Px[] Data; public int W,H; }
}
class CustomUIManager
{
    public static CustomUIManager Instance => null;
    public UnityEngine.Sprite GetModSprite(string a,string b,bool c) => null;
}
static class RenderHandler { public static UnityEngine.Sprite TryLoadFromAtlas(string a,string b) => null; }
namespace ModePuzzle
{
    static class ModePuzzleMod { public static Logger Log => null; }
    class Logger { public void Warning(string s) {} }
}
namespace ModePuzzle.UI
{
    static class Pixels
    {
        public static Px[] Read(UnityEngine.Sprite s,out int w,out int h) { w=s.W;h=s.H;return s.Data; }
        public static UnityEngine.Sprite ToSprite(Px[] p,int w,int h,string name) => new UnityEngine.Sprite { Data=p,W=w,H=h };
    }
}
static class BayPreview
{
    static void Check(bool ok,string reason) { if(!ok) throw new Exception(reason); }
    static void Main(string[] args)
    {
        var m = new MachineInfo { Width=7, Height=5, Usable=new bool[35] };
        for(int y=0;y<5;y++) for(int x=0;x<7;x++)
            m.Usable[y*7+x] = !((x==0 || x==6) && (y==0 || y==4)) && !(x==3 && y>=1 && y<=3);
        var empty = ModuleArt.ComposeBay(m);
        Check(empty.W==114 && empty.H==82,"Frame dimensions");
        for(int y=0;y<empty.H;y++) for(int x=0;x<empty.W;x++)
        {
            Px expected;
            if(x==0 || y==0 || x==empty.W-1 || y==empty.H-1) expected=PanelStyle.BayFrame;
            else
            {
                int a=x-1,b=y-1;
                expected=m.IsUsable(a/16,b/16) && a%16>=1 && a%16<15 && b%16>=1 && b%16<15 ? PanelStyle.BayCell : PanelStyle.BayBackground;
            }
            Check(empty.Data[y*empty.W+x].Same(expected) && empty.Data[y*empty.W+x].A==255,"Cell margin/background/frame");
        }
        int n=0;
        foreach(var c in new[]{new Cell(1,0),new Cell(2,0),new Cell(4,0),new Cell(5,0),new Cell(0,1),new Cell(1,1),new Cell(2,1),new Cell(4,1),new Cell(5,1),new Cell(6,1),new Cell(0,2),new Cell(1,2)})
        {
            var module=new ModuleInfo { UniqueId=++n };
            module.Effects.Add("MODULE_EFFECT_PREMIUM");
            module.Effects.Add("MODULE_EFFECT_RECEIVER");
            var p=new PlacedModule { Module=module }; p.Cells.Add(c); m.Installed.Add(p);
        }
        var filled=ModuleArt.ComposeBay(m);
        // Number stamps stay in the last cell, including 3-digit search-limit labels.
        foreach(int number in new[]{1,9,10,99,100})
        {
            var pixels=Enumerable.Repeat(PanelStyle.BayBackground,32*32).ToArray();
            var p=new PlacedModule {Module=new ModuleInfo()};
            p.Cells.Add(new Cell(1,1)); p.Cells.Add(new Cell(0,0));
            BayNumbers.Stamp(pixels,32,32,p,number);
            Check(pixels.Any(c=>c.Same(PanelStyle.NumberInk)),"Missing digits");
            for(int i=0;i<pixels.Length;i++) if(i%32<16 || i/32<16)
                Check(pixels[i].Same(PanelStyle.BayBackground),"Stamp escaped last cell");
        }
        foreach(var pair in new[]{Tuple.Create("bay_empty",empty),Tuple.Create("bay_numbered",filled)})
            using(var f=new BinaryWriter(File.Create(Path.Combine(args[0],pair.Item1+".rgba"))))
                foreach(var p in pair.Item2.Data) { f.Write(p.R);f.Write(p.G);f.Write(p.B);f.Write(p.A); }
        Console.WriteLine("BAY OK: production ComposeBay, opaque cells/frame, 1/2/3-digit placement; synthetic fallback art only.");
    }
}
