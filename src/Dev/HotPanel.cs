#if DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ModePuzzle.Dev
{
    /// <summary>
    /// Development builds only. Loads the panel from build/dev/ModePuzzle.Panel.dll in the repository and loads it
    /// again whenever that file changes, so the window can be reworked without restarting the game. Also runs
    /// commands from build/dev/cmd.txt (first line: a batch id; then one command per line) and writes the results to
    /// build/dev/out.txt; "screenshot name" saves build/shots/name.png. Never writes into the game folder and never
    /// touches saves; the panel commands only drive the window.
    /// </summary>
    sealed class HotPanel : MonoBehaviour
    {
        const float PollSeconds = 0.1f, SettleSeconds = 0.5f, MaxWaitSeconds = 60f;
        static HotPanel instance;
        static Type entry;
        static DateTime loadedStamp;
        static int generation;
        float next;
        string lastBatch;
        bool running;

        static string DevDir => Path.Combine(DevPaths.Root, "build", "dev");
        static string DllPath => Path.Combine(DevDir, "ModePuzzle.Panel.dll");
        static string CmdPath => Path.Combine(DevDir, "cmd.txt");
        static string OutPath => Path.Combine(DevDir, "out.txt");
        static string ShotDir => Path.Combine(DevPaths.Root, "build", "shots");

        public static void Begin()
        {
            if (instance != null) return;
            var go = new GameObject("ModePuzzle.HotPanel");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HotPanel>();
            instance.lastBatch = ReadBatchId();   // do not replay a batch left over from an earlier session
            EnsureLoaded();
        }

        public static void End()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        public static object Call(string method, params object[] args)
        {
            EnsureLoaded();
            var m = entry?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return null;
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException e)
            {
                ModePuzzleMod.Log?.Warning($"Hot panel {method}: {e.InnerException}");
                return null;
            }
        }

        static void EnsureLoaded()
        {
            if (entry == null) Reload();
        }

        static void Reload()
        {
            if (!File.Exists(DllPath)) { ModePuzzleMod.Log?.Warning("Hot panel: missing " + DllPath); return; }
            bool wasOpen = false;
            if (entry != null)
            {
                wasOpen = Call("IsOpen") is bool open && open;
                Call("Shutdown");
            }
            var stamp = File.GetLastWriteTimeUtc(DllPath);
            var assembly = Assembly.Load(File.ReadAllBytes(DllPath));
            entry = assembly.GetType("ModePuzzle.PanelEntry");
            loadedStamp = stamp;
            generation++;
            DevLoc.Reload();
            ModePuzzleMod.Log?.Log($"Hot panel loaded: {assembly.GetName().Version} (#{generation})");
            if (wasOpen) Call("Open");
        }

        static string ReadBatchId()
        {
            try { return File.Exists(CmdPath) ? File.ReadLines(CmdPath).FirstOrDefault()?.Trim() : null; }
            catch (Exception) { return null; }
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + PollSeconds;
            try
            {
                if (File.Exists(DllPath))
                {
                    var t = File.GetLastWriteTimeUtc(DllPath);
                    if (t != loadedStamp && (DateTime.UtcNow - t).TotalSeconds > SettleSeconds) Reload();
                }
                if (!running)
                {
                    var id = ReadBatchId();
                    if (!string.IsNullOrEmpty(id) && id != lastBatch)
                    {
                        lastBatch = id;
                        StartCoroutine(RunBatch(id, File.ReadAllLines(CmdPath).Skip(1).ToList()));
                    }
                }
            }
            catch (Exception e) { ModePuzzleMod.Log?.Warning("Hot panel: " + e.Message); }
        }

        IEnumerator RunBatch(string id, List<string> lines)
        {
            running = true;
            var results = new List<string> { id };
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ' }, 2);
                string verb = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim() : "";
                string result;
                if (verb == "wait")
                {
                    float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s);
                    yield return new WaitForSecondsRealtime(Mathf.Clamp(s, 0f, MaxWaitSeconds));
                    result = "ok";
                }
                else if (verb == "screenshot")
                {
                    yield return new WaitForEndOfFrame();
                    System.IO.Directory.CreateDirectory(ShotDir);
                    var name = string.Concat((arg.Length > 0 ? arg : "shot").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
                    ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, name + ".png"));
                    result = "ok " + name + ".png";
                }
                else if (verb == "reload")
                {
                    try { Reload(); result = "ok #" + generation; }
                    catch (Exception e) { result = "error " + e.Message; }
                }
                else
                {
                    try { result = Call("Command", line) as string ?? "error (see log)"; }
                    catch (Exception e) { result = "error " + e.Message; }
                }
                results.Add(line + " -> " + result);
                yield return null;   // let the UI lay out before the next command
            }
            yield return null;
            results.Add("done");
            try { File.WriteAllLines(OutPath, results); }
            catch (Exception e) { ModePuzzleMod.Log?.Warning("Hot panel: " + e.Message); }
            running = false;
        }
    }
}
#endif
