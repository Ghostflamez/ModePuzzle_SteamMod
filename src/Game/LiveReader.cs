using System;
using System.Collections.Generic;
using ModePuzzle.Model;

namespace ModePuzzle.Game
{
    /// <summary>
    /// Reads machines and modules from the running game (live GameItem objects, not the save file).
    /// Read-only: nothing here changes game state.
    /// </summary>
    static class LiveReader
    {
        const string ModuleTag = "MODULE_TAG";
        const string StuckTag = "MODULE_STUCK_TAG";
        const string ModuleTypeTag = "MODULE_TYPE";
        static readonly string[] EffectTags = { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" };

        public static Snapshot Read()
        {
            var snap = new Snapshot();
            var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
            if (owned == null) return snap;

            // Machines first, so modules can be matched to the bay inventory that holds them.
            var bayOwner = new Dictionary<GameInventory, MachineInfo>();
            machinesById.Clear();
            foreach (var item in owned)
            {
                if (item == null || !item.IsGameItemType("MACHINE")) continue;
                if (MachineTable.ExcludedMachines.Contains(item.identifier)) continue;
                var bay = SafeModuleInv(item);
                if (bay == null) continue;
                var machine = ReadMachine(item, bay);
                if (machine == null) { snap.Skip("bay_shape"); continue; }
                snap.Machines.Add(machine);
                bayOwner[bay] = machine;
                machinesById[item.uniqueId] = item;
            }

            foreach (var item in owned)
            {
                if (item == null || !IsModule(item)) continue;
                if (MachineTable.ExcludedModules.Contains(item.identifier)) { snap.Skip("chem_module"); continue; }
                var module = ReadModule(item);
                var holder = item.parentInventory;
                if (holder != null && bayOwner.TryGetValue(holder, out var machine))
                {
                    var shape = item.modifiedShape;
                    machine.Installed.Add(new PlacedModule
                    {
                        Module = module, Cells = PlacedCells(shape),
                        OriginX = shape?.minX ?? 0, OriginY = shape?.minY ?? 0,
                        Orientation = shape == null ? 0 : ((shape.orientation % 4) + 4) % 4, Flipped = shape?.flipped ?? false,
                    });
                }
                else if (holder != null && holder.GetParentItem() is GameItem parent && parent.IsGameItemType("MACHINE"))
                    snap.Skip(MachineTable.ExcludedMachines.Contains(parent.identifier) ? "in_chem_machine" : "in_unsupported_machine");
                else
                    snap.Free.Add(module);
            }

            foreach (var machine in snap.Machines)
            {
                if (!machine.Guessed) continue;
                // Unknown (e.g. modded) machine: accept universal modules plus every type already installed.
                foreach (var p in machine.Installed)
                    if (p.Module.ModuleType != null && !machine.Accepts.Contains(p.Module.ModuleType)) machine.Accepts.Add(p.Module.ModuleType);
            }
            DisambiguateNames(snap.Machines);
            return snap;
        }

        /// <summary>Machines seen by the last Read, by unique id, so watching one does not rescan everything.</summary>
        static readonly Dictionary<int, GameItem> machinesById = new Dictionary<int, GameItem>();

        /// <summary>
        /// Cheap fingerprint of one machine: its bay shape and where each installed module sits. Changes when the
        /// bay is expanded or a module is added, removed, moved, rotated or flipped. Reads only that machine's
        /// bay (no inventory scan); null if the machine is no longer available.
        /// </summary>
        public static string Signature(int machineUniqueId)
        {
            if (!machinesById.TryGetValue(machineUniqueId, out var machine) || machine == null) return null;
            var bay = SafeModuleInv(machine);
            var shape = bay?.inventoryShape;
            if (shape == null) return null;
            var sb = new System.Text.StringBuilder();
            sb.Append(shape.width).Append('x').Append(shape.height).Append(':');
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++) sb.Append(shape.GetLocal(x, y) == 0 ? '0' : '1');
            var parts = new List<string>();
            var children = bay.childItems;
            if (children != null)
                foreach (var item in children)
                {
                    if (item == null || !IsModule(item)) continue;
                    var m = item.modifiedShape;
                    parts.Add($"{item.uniqueId}@{m?.minX},{m?.minY},{m?.orientation},{m?.flipped}");
                }
            parts.Sort(string.CompareOrdinal);
            sb.Append('|').Append(string.Join(";", parts));
            return sb.ToString();
        }

        static GameGridInventory SafeModuleInv(GameItem machine)
        {
            try { return MachineHelper.GetModuleInv(machine); }
            catch (Exception) { return null; }
        }

        static bool IsModule(GameItem item) => item.IsTag(ModuleTag) || item.IsGameItemType("NODE");

        static MachineInfo ReadMachine(GameItem item, GameGridInventory bay)
        {
            var shape = bay.inventoryShape;
            if (shape == null || shape.width <= 0 || shape.height <= 0) return null;
            var known = MachineTable.Lookup(item.identifier);
            var machine = new MachineInfo
            {
                UniqueId = item.uniqueId,
                Identifier = item.identifier,
                Name = item.GetDisplayName(true),
                Width = shape.width,
                Height = shape.height,
                Usable = new bool[shape.width * shape.height],
                Guessed = known == null,
            };
            machine.Accepts.AddRange(known ?? new[] { MachineTable.Universal });
            // The bay shape marks blocked cells with a non-zero byte (MachineHelper.LoadUpgrade sets "1" on
            // the corners and the centre cross of a stage-0 bay).
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                    machine.Usable[y * shape.width + x] = shape.GetLocal(x, y) == 0;
            return machine;
        }

        static ModuleInfo ReadModule(GameItem item)
        {
            bool node = item.IsGameItemType("NODE");
            var m = new ModuleInfo
            {
                UniqueId = item.uniqueId,
                Identifier = item.identifier,
                Name = item.GetDisplayName(true),
                IsNode = node,
                Stuck = item.IsTag(StuckTag),
                ModuleType = TagString(item, ModuleTypeTag),
                Shape = LocalCells(item.shape),
                ShapeWidth = item.shape?.width ?? 0,
                ShapeHeight = item.shape?.height ?? 0,
                SpriteAtlas = item.spriteAtlasPath,
                SpriteName = item.spritePath,
                SpriteFromMod = item.useSpriteFromMod,
            };
            if (!node)
            {
                m.Efficiency = TagInt(item, "BONUS_PERCENTAGE_EFFICIENCY_INT");
                m.Quality = TagInt(item, "BONUS_PERCENTAGE_QUALITY_INT");
                m.Performance = TagInt(item, "BONUS_PERCENTAGE_PERFORMANCE_INT");
                foreach (var tag in EffectTags)
                {
                    var effect = TagString(item, tag);
                    if (!string.IsNullOrEmpty(effect) && !m.Effects.Contains(effect)) m.Effects.Add(effect);
                }
            }
            return m;
        }

        static TagState Tag(GameItem item, string name)
        {
            try { return item.IsTag(name) ? item.GetTagReadonly(name) : null; }
            catch (Exception) { return null; }
        }

        static int TagInt(GameItem item, string name) => Tag(item, name)?.GetInt() ?? 0;

        static string TagString(GameItem item, string name)
        {
            var s = Tag(item, name)?.GetString();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>Occupied cells of an unrotated item shape.</summary>
        static List<Cell> LocalCells(GridShape shape)
        {
            var cells = new List<Cell>();
            if (shape == null) return cells;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                    if (shape.GetLocal(x, y) != 0) cells.Add(new Cell(x, y));
            return cells;
        }

        /// <summary>Bay cells of a placed item: the game's own ForwardTransform (flip, rotation, offset).</summary>
        static List<Cell> PlacedCells(GridShape shape)
        {
            var cells = new List<Cell>();
            if (shape == null) return cells;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                {
                    if (shape.GetLocal(x, y) == 0) continue;
                    var (gx, gy) = shape.ForwardTransform(x, y);
                    cells.Add(new Cell(gx, gy));
                }
            return cells;
        }

        static void DisambiguateNames(List<MachineInfo> machines)
        {
            var counts = new Dictionary<string, int>();
            foreach (var m in machines) { counts.TryGetValue(m.Name, out int n); counts[m.Name] = n + 1; }
            var seen = new Dictionary<string, int>();
            foreach (var m in machines)
            {
                if (counts[m.Name] < 2) continue;
                seen.TryGetValue(m.Name, out int i);
                seen[m.Name] = ++i;
                m.Name = $"{m.Name} #{i}";
            }
        }
    }
}
