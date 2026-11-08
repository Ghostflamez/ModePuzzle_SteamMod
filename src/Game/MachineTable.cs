using System.Collections.Generic;

namespace ModePuzzle.Game
{
    /// <summary>
    /// Which module types each vanilla machine accepts (the machines' constructor filters), as in the
    /// external tool's src/config/machines.js. Chemical machines use a separate module system and are left out.
    /// </summary>
    static class MachineTable
    {
        public const string Universal = "MODULE_TYPE_UNIVERSAL";

        static readonly Dictionary<string, string[]> Accepts = new Dictionary<string, string[]>
        {
            ["hydroponic"] = new[] { Universal, "MODULE_TYPE_HYDROPONIC" },
            ["water_purifier"] = new[] { Universal },
            ["furnace"] = new[] { Universal, "MODULE_TYPE_FURNACE" },
            ["plant_dna_manipulator"] = new[] { Universal, "MODULE_TYPE_DNA_MANIPULATOR" },
            ["printer"] = new[] { Universal, "MODULE_TYPE_PRINTER" },
            ["deal_maker"] = new[] { Universal },
            ["security_alarm"] = new[] { Universal, "MODULE_TYPE_ALARM" },
            ["mirage_projector"] = new[] { Universal },
            ["moisture_farm"] = new[] { Universal },
            ["desequencer"] = new[] { Universal, "MODULE_TYPE_ALARM" },
            ["wine_rack"] = new[] { Universal },
            ["evaporator"] = new[] { Universal, "MODULE_TYPE_EVAPORATOR" },
        };

        public static readonly HashSet<string> ExcludedMachines = new HashSet<string> { "blender", "heating_plate", "chem_finisher" };
        public static readonly HashSet<string> ExcludedModules = new HashSet<string> { "chem_module" };

        /// <summary>Accepted types for a known machine, or null.</summary>
        public static string[] Lookup(string identifier) => Accepts.TryGetValue(identifier, out var a) ? a : null;
    }
}
