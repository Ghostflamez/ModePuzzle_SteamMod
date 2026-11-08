#if DEV
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ModePuzzle.Dev
{
    /// <summary>
    /// Development builds only: the mod loader reads Localization/*.csv once at start-up, so strings added while the
    /// game runs are read from the repository instead, in the language the game currently shows.
    /// </summary>
    static class DevLoc
    {
        static Dictionary<string, string> table;

        public static bool TryGet(string key, out string value)
        {
            value = null;
            return table != null && table.TryGetValue(key, out value);
        }

        public static void Reload()
        {
            var zh = Read("zh-Hans.csv");
            var en = Read("en.csv");
            table = null;
            string shown = ModHelper.GetLocalized(ModePuzzleMod.ModId, "item_name");
            table = en.TryGetValue("item_name", out var e) && e == shown ? en : zh;
        }

        static Dictionary<string, string> Read(string file)
        {
            var result = new Dictionary<string, string>();
            var path = Path.Combine(DevPaths.Root, "Localization", file);
            if (!File.Exists(path)) return result;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string key = line.Substring(0, comma).Trim().TrimStart('﻿'), value = line.Substring(comma + 1);
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
                result[key] = value;
            }
            return result;
        }
    }
}
#endif
