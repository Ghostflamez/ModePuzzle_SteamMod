using System.Collections.Generic;

namespace ModePuzzle.Model
{
    /// <summary>Grid cell, x to the right and y down, in the bay's own coordinates.</summary>
    public struct Cell
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
    }

    /// <summary>A module or node as read from the live game. Stats in the external tool's axis order.</summary>
    public class ModuleInfo
    {
        public int UniqueId;
        public string Identifier;
        public string Name;
        public bool IsNode;
        public int Efficiency, Quality, Performance;
        /// <summary>Raw effect ids, e.g. MODULE_EFFECT_SIDE_MOUNT.</summary>
        public List<string> Effects = new List<string>();
        public string ModuleType;
        /// <summary>Needs a 模组提取器 to remove: must stay where it is.</summary>
        public bool Stuck;
        /// <summary>Unrotated cells of the item's own shape.</summary>
        public List<Cell> Shape = new List<Cell>();
    }

    public class PlacedModule
    {
        public ModuleInfo Module;
        /// <summary>Cells it covers in the bay, rotation and flip applied.</summary>
        public List<Cell> Cells = new List<Cell>();
    }

    public class MachineInfo
    {
        public int UniqueId;
        public string Identifier;
        public string Name;
        public int Width, Height;
        /// <summary>Row-major, true where a module may go.</summary>
        public bool[] Usable;
        public List<string> Accepts = new List<string>();
        /// <summary>No entry in the known-machine table; accepted types are inferred.</summary>
        public bool Guessed;
        public List<PlacedModule> Installed = new List<PlacedModule>();

        public bool IsUsable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Usable[y * Width + x];
        public int UsableCount { get { int n = 0; foreach (var u in Usable) if (u) n++; return n; } }
        public bool CanHold(ModuleInfo m) => m.IsNode || m.ModuleType == null || Accepts.Contains(m.ModuleType);
    }

    /// <summary>Everything the panel shows, read in one pass.</summary>
    public class Snapshot
    {
        public List<MachineInfo> Machines = new List<MachineInfo>();
        /// <summary>Owned modules not installed in a supported machine.</summary>
        public List<ModuleInfo> Free = new List<ModuleInfo>();
        /// <summary>Items left out, by reason.</summary>
        public Dictionary<string, int> Skipped = new Dictionary<string, int>();

        public void Skip(string reason)
        {
            Skipped.TryGetValue(reason, out int n);
            Skipped[reason] = n + 1;
        }
    }
}
