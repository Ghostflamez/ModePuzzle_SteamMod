using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Model;

namespace ModePuzzle.Game
{
    /// <summary>Game terms in the current language, from the game's own localization at runtime.</summary>
    static class GameText
    {
        static readonly object[] NoArgs = new object[0];

        static string Mechanic(string key, string fallback)
        {
            try { return LocHelper.GetLocalizedMechanicOrDefault(key, fallback, NoArgs); }
            catch (Exception) { return fallback; }
        }

        public static string Efficiency => Mechanic("mech_module_stat_efficiency", "Efficiency");
        public static string Quality => Mechanic("mech_module_stat_quality", "Quality");
        public static string Performance => Mechanic("mech_module_stat_performance", "Performance");

        /// <summary>MODULE_EFFECT_SIDE_MOUNT → 侧边挂载 (mech_module_effect_side_mount_name).</summary>
        public static string Effect(string effectId)
        {
            const string prefix = "MODULE_EFFECT_";
            var id = effectId.StartsWith(prefix) ? effectId.Substring(prefix.Length) : effectId;
            if (id == "NODE_EFFECT_SUPPORT") id = "SUPPORT_NODE";
            return Mechanic("mech_module_effect_" + id.ToLowerInvariant() + "_name", id);
        }

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        // Value colours: a soft green and red that sit with the machinery window's palette.
        public const string PositiveHex = "8cc56a", NegativeHex = "d9675a", WarnHex = "e0a35a";

        /// <summary>A signed value in green (positive) or red (negative); zero stays in the body colour.</summary>
        public static string ColoredValue(int v) =>
            v > 0 ? $"<color=#{PositiveHex}>+{v}</color>" : v < 0 ? $"<color=#{NegativeHex}>{v}</color>" : "0";

        /// <summary>Rich-text line: name in the module's card colour, coloured non-zero stats, effects, stuck.</summary>
        public static string DescribeRich(ModuleInfo m, string nameHex)
        {
            var parts = new List<string> { nameHex == null ? m.Name : $"<color=#{nameHex}>{m.Name}</color>" };
            if (m.IsNode) parts.Add(Loc.T("panel_node"));
            else
            {
                var stats = new List<string>();
                if (m.Efficiency != 0) stats.Add($"{Efficiency} {ColoredValue(m.Efficiency)}");
                if (m.Quality != 0) stats.Add($"{Quality} {ColoredValue(m.Quality)}");
                if (m.Performance != 0) stats.Add($"{Performance} {ColoredValue(m.Performance)}");
                if (stats.Count > 0) parts.Add(string.Join("  ", stats));
                if (m.Effects.Count > 0) parts.Add("[" + string.Join(", ", m.Effects.Select(Effect)) + "]");
            }
            if (m.Stuck) parts.Add($"<color=#{WarnHex}>{Loc.T("panel_stuck")}</color>");
            return string.Join("  ", parts);
        }

        /// <summary>One line: name, non-zero stats, effects, stuck.</summary>
        public static string Describe(ModuleInfo m)
        {
            var parts = new List<string> { m.Name };
            if (m.IsNode) parts.Add(Loc.T("panel_node"));
            else
            {
                var stats = new List<string>();
                if (m.Efficiency != 0) stats.Add($"{Efficiency} {Signed(m.Efficiency)}");
                if (m.Quality != 0) stats.Add($"{Quality} {Signed(m.Quality)}");
                if (m.Performance != 0) stats.Add($"{Performance} {Signed(m.Performance)}");
                if (stats.Count > 0) parts.Add(string.Join(" ", stats));
                if (m.Effects.Count > 0) parts.Add("[" + string.Join(", ", m.Effects.Select(Effect)) + "]");
            }
            if (m.Stuck) parts.Add(Loc.T("panel_stuck"));
            return string.Join("  ", parts);
        }
    }
}
