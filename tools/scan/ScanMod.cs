using System; using System.IO; using System.Linq; using System.Reflection; using System.Collections;
/// <summary>Runs the game's own ModScanner on a built mod DLL (read-only) and prints the capabilities it finds.</summary>
class ScanMod {
  static string M = @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
  static int Main(string[] a){
    AppDomain.CurrentDomain.AssemblyResolve += (s,e)=>{ var p=Path.Combine(M,new AssemblyName(e.Name).Name+".dll"); return File.Exists(p)?Assembly.LoadFrom(p):null; };
    var game=Assembly.LoadFrom(Path.Combine(M,"Assembly-CSharp.dll"));
    var scanner=game.GetType("ModScanner");
    var scan=scanner.GetMethod("Scan");
    var res=scan.Invoke(null,new object[]{ new[]{ a[0] }, null, a[1], true });
    var t=res.GetType();
    string Get(string f){ var v=t.GetField(f).GetValue(res); return v is IEnumerable e && !(v is string) ? string.Join(" | ", e.Cast<object>()) : ""+v; }
    Console.WriteLine("methods="+Get("MethodsScanned")+" refused="+t.GetProperty("Refused").GetValue(res)+" error="+Get("Error"));
    Console.WriteLine("caps: "+Get("Capabilities"));
    Console.WriteLine("hard: "+Get("HardFlags"));
    Console.WriteLine("findings: "+Get("Findings"));
    return 0;
  }
}
