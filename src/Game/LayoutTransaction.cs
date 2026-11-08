using System;
using System.Collections.Generic;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Model;

namespace ModePuzzle.Game
{
    /// <summary>Main-thread-only snapshot guard and reversible, checked inventory operations.</summary>
    sealed class LayoutTransaction
    {
        sealed class Record
        {
            public GameItem Item;
            public GameInventory Parent;
            public GridShape Shape;
            public string Signature;
            public Record(GameItem item)
            {
                Item = item; Parent = item.parentInventory; Shape = item.modifiedShape.Clone();
                Signature = LiveReader.ModuleSignature(item);
            }
            public bool Unchanged() => Item.parentInventory == Parent && LiveReader.ModuleSignature(Item) == Signature;
        }
        sealed class Move
        {
            public GameItem Item;
            public GameInventory Parent;
            public GridShape Shape;
        }
        readonly EmporiumEntry scene;
        readonly GameItem machine;
        readonly GameGridInventory bay;
        readonly GameInventory machineParent;
        readonly string signature;
        readonly Dictionary<int, Record> records;
        readonly Dictionary<int, ModuleInfo> modules;
        readonly HashSet<int> fixedIds;
        readonly MachineInfo baseline;
        List<Record> undo;
        List<Record> after;
        string afterSignature;
        public bool CanRestore => undo != null;
        public int MachineId => baseline.UniqueId;

        /// <summary>A restore can run: there is a record and nothing changed since the apply.</summary>
        public bool RestoreReady()
        {
            try
            {
                return undo != null && scene == EmporiumEntry.Instance && afterSignature == LiveReader.Signature(baseline.UniqueId) &&
                       after.All(r => r.Unchanged());
            }
            catch (Exception) { return false; }
        }

        public LayoutTransaction(MachineInfo baseline, SearchConfig config, IEnumerable<ModuleInfo> candidates)
        {
            this.baseline = baseline;
            scene = EmporiumEntry.Instance;
            var owned = scene.GetAllOwnedItems().Where(i => i != null).ToDictionary(i => i.uniqueId);
            machine = owned[baseline.UniqueId]; machineParent = machine.parentInventory; bay = MachineHelper.GetModuleInv(machine);
            signature = LiveReader.Signature(baseline.UniqueId);
            modules = candidates.ToDictionary(m => m.UniqueId);
            records = modules.Keys.ToDictionary(id => id, id => new Record(owned[id]));
            fixedIds = new HashSet<int>(config.Fixed.Select(p => int.Parse(p.Item.Id.Substring(1))));
        }

        public bool Unchanged() => ChangeReason() == null;

        /// <summary>Diagnostics: what differs from the snapshot, or null when nothing does.</summary>
        public string ChangeReason()
        {
            if (scene != EmporiumEntry.Instance) return "scene";
            if (machine.parentInventory != machineParent) return "machine moved";
            var now = LiveReader.Signature(baseline.UniqueId);
            if (signature != now) return "machine bay";
            foreach (var r in records.Values)
            {
                if (r.Item.parentInventory != r.Parent) return $"module {r.Item.uniqueId} changed parent";
                var sig = LiveReader.ModuleSignature(r.Item);
                if (sig != r.Signature) return $"module {r.Item.uniqueId}";
            }
            return null;
        }

        public MachineInfo Preview(SearchResult result)
        {
            var preview = new MachineInfo { UniqueId = baseline.UniqueId, Width = baseline.Width, Height = baseline.Height,
                Usable = baseline.Usable, Name = baseline.Name, Identifier = baseline.Identifier, Guessed = baseline.Guessed };
            preview.Accepts.AddRange(baseline.Accepts);
            foreach (var p in result.Layout)
            {
                int id = int.Parse(p.Item.Id.Substring(1));
                var old = baseline.Installed.FirstOrDefault(x => x.Module.UniqueId == id);
                // Preserve the exact sprite orientation when occupied cells did not change.
                var placed = old != null && old.Cells.Select(c => c.Y * baseline.Width + c.X).OrderBy(c => c).SequenceEqual(p.Placement.Cells.OrderBy(c => c))
                    ? old : EngineBridge.Proposed(modules[id], p.Placement, baseline.Width);
                preview.Installed.Add(placed);
            }
            return preview;
        }

        static GridShape Shape(GameItem item, PlacedModule p) => new GridShapeBuilder(item.shape)
            .SetTransform(p.OriginX, p.OriginY, p.Flipped, p.Orientation).Build();
        static bool Same(GridShape a, GridShape b) => a.minX == b.minX && a.minY == b.minY &&
            a.orientation == b.orientation && a.flipped == b.flipped &&
            LiveReader.PlacedCells(a).Select(c => (c.X,c.Y)).OrderBy(c => c).SequenceEqual(LiveReader.PlacedCells(b).Select(c => (c.X,c.Y)).OrderBy(c => c));
        static bool At(Move m) => m.Item.parentInventory == m.Parent && Same(m.Item.modifiedShape, m.Shape);
        static SlotMarker Slot(Move m) => new SlotMarker { item = m.Item, inventory = m.Parent, itemGridShape = m.Shape, numTransfer = m.Item.unitCount };
        static Exception Failure(string key) => new InvalidOperationException(Loc.T(key));
        static bool CanRemove(GameItem item) => !item.IsTag("MODULE_STUCK_TAG") && item.MaxNumRemove() >= item.unitCount && item.parentInventory?.IsRemoveLocked() != true;

        void CheckScene()
        {
            if (scene != EmporiumEntry.Instance || !scene.GetAllOwnedItems().Contains(machine) || MachineHelper.GetModuleInv(machine) != bay)
                throw Failure("opt_stale");
            if (machine.IsTag(MachineHelper.LOCKED_WORKING_TAG) || bay.IsInsertLocked() || bay.IsRemoveLocked()) throw Failure("opt_locked");
        }

        static void Validate(List<Move> moves)
        {
            var ignored = moves.Select(m => m.Item).ToList();
            foreach (var m in moves)
            {
                if (m.Item.unitCount != 1 || m.Item.IsTag("MODULE_STUCK_TAG") || m.Item.MaxNumRemove() < 1 ||
                    m.Item.parentInventory?.IsRemoveLocked() == true || m.Parent.IsInsertLocked()) throw Failure("opt_locked");
                if (!m.Parent.MayHaveValidInventorySlot(m.Item) || !m.Parent.CheckShapePlacement(m.Item, m.Shape, ignored) ||
                    !m.Parent.CheckAcceptParentShapeStateModifier(m.Item, Slot(m), m.Shape, m.Item.modifiedState) || m.Item.MaxNumAdd(Slot(m)) < 1)
                    throw Failure("opt_illegal");
                if (moves.Any(n => n != m && n.Parent == m.Parent && n.Shape.Intersects(m.Shape))) throw Failure("opt_overlap");
            }
        }

        // Reserve actual cells for ALL removed modules before expelling anything. Existing storage items stay
        // occupied even if they will be added to the bay: conservative, with no optimistic capacity assumption.
        Move Reserve(GameItem item, List<Move> reserved)
        {
            foreach (var dest in new[] { scene.invElement, scene.backInvinvElement, scene.frontInvinvElement })
            {
                if (dest == null || dest.IsInsertLocked() || !dest.MayHaveValidInventorySlot(item)) continue;
                for (int flip = 0; flip < 2; flip++)
                    for (int r = 0; r < 4; r++)
                        for (int y = dest.inventoryShape.minY; y <= dest.inventoryShape.maxY; y++)
                            for (int x = dest.inventoryShape.minX; x <= dest.inventoryShape.maxX; x++)
                            {
                                var shape = new GridShapeBuilder(item.shape).SetTransform(x,y,flip == 1,r).Build();
                                var move = new Move { Item=item, Parent=dest, Shape=shape };
                                if (!dest.CheckShapePlacement(item,shape,new List<GameItem>()) || reserved.Any(m => m.Parent == dest && m.Shape.Intersects(shape))) continue;
                                if (!dest.CheckAcceptParentShapeStateModifier(item,Slot(move),shape,item.modifiedState) || item.MaxNumAdd(Slot(move)) < 1) continue;
                                reserved.Add(move); return move;
                            }
            }
            throw Failure("opt_storage_full");
        }

        public string Apply(SearchResult result)
        {
            string phase = "preflight";
            List<Record> originals = null;
            bool began = false;
            try
            {
                CheckScene();
                if (!Unchanged() || !result.Feasible || result.Layout.Select(p => p.Item.Id).Distinct().Count() != result.Layout.Count) throw Failure("opt_stale");
                var owned = new HashSet<GameItem>(scene.GetAllOwnedItems());
                if (records.Values.Any(r => !owned.Contains(r.Item))) throw Failure("opt_stale");
                var preview = Preview(result);
                var desired = preview.Installed.ToDictionary(p => p.Module.UniqueId);
                foreach (int id in fixedIds)
                    if (!desired.TryGetValue(id,out var p) || !Same(records[id].Shape, Shape(records[id].Item,p))) throw Failure("opt_locked");
                var moves = new List<Move>();
                foreach (var p in preview.Installed)
                {
                    var item = records[p.Module.UniqueId].Item;
                    if (!baseline.CanHold(LiveReader.ReadModule(item))) throw Failure("opt_illegal");
                    var m = new Move { Item=item, Parent=bay, Shape=Shape(item,p) };
                    if (!At(m)) moves.Add(m);
                }
                var reserved = new List<Move>();
                foreach (var p in baseline.Installed)
                    if (!desired.ContainsKey(p.Module.UniqueId)) moves.Add(Reserve(records[p.Module.UniqueId].Item,reserved));
                Validate(moves);
                if (moves.Count == 0) return Loc.T("opt_no_changes");
                originals = moves.Select(m => new Record(m.Item)).ToList();
                began = true;
                Execute(moves,ref phase, true);
                phase = "verify";
                if (bay.childItems.Count != desired.Count || preview.Installed.Any(p => !At(new Move { Item=records[p.Module.UniqueId].Item, Parent=bay, Shape=Shape(records[p.Module.UniqueId].Item,p) })))
                    throw Failure("opt_verify_failed");
                undo = originals;
                after = originals.Select(r => new Record(r.Item)).ToList();
                afterSignature = LiveReader.Signature(baseline.UniqueId);
                return Loc.T("opt_applied");
            }
            catch (Exception e)
            {
                bool restored = !began || Rollback(originals);
                ModePuzzleMod.Log?.Warning($"Apply machine={baseline.UniqueId} phase={phase} restored={restored}: {e}");
                return Loc.T("opt_failure", Loc.T("opt_phase_"+phase), e.Message, Loc.T(restored ? "opt_restored" : "opt_restore_failed"));
            }
        }

        public string Restore()
        {
            string phase = "preflight";
            List<Record> current = null;
            bool began = false;
            try
            {
                CheckScene();
                if (undo == null || afterSignature != LiveReader.Signature(baseline.UniqueId) || after.Any(r => !r.Unchanged()) ||
                    after.Any(r => !scene.GetAllOwnedItems().Contains(r.Item))) throw Failure("opt_stale");
                var moves = undo.Select(r => new Move { Item=r.Item, Parent=r.Parent, Shape=r.Shape }).ToList();
                Validate(moves);
                current = moves.Select(m => new Record(m.Item)).ToList();
                began = true; Execute(moves,ref phase); undo = null; after = null;
                return Loc.T("opt_restored");
            }
            catch (Exception e)
            {
                bool restored = !began || Rollback(current);
                ModePuzzleMod.Log?.Warning($"Restore machine={baseline.UniqueId} phase={phase} restored={restored}: {e}");
                return Loc.T("opt_failure", Loc.T("opt_phase_"+phase),e.Message,Loc.T(restored ? "opt_restored" : "opt_restore_failed"));
            }
        }

        void Execute(List<Move> moves, ref string phase, bool testFailure = false)
        {
            phase = "remove";
            foreach (var m in moves)
                if (!CanRemove(m.Item) || m.Item.parentInventory != null && !m.Item.parentInventory.Expel(m.Item))
                    throw new InvalidOperationException(Loc.T("opt_remove_failed") + " #" + m.Item.uniqueId);
            phase = "insert";
            foreach (var m in moves)
            {
                // TryAcceptOnce only checks removal amount; explicitly validate the destination first.
                if (!m.Parent.CheckInventorySlotStillValid(m.Item,m.Shape,m.Item.modifiedState) || Slot(m).TryAcceptOnce() != m.Item.unitCount || !At(m))
                    throw new InvalidOperationException(Loc.T("opt_insert_failed") + " #" + m.Item.uniqueId);
                if (testFailure && ModePuzzleMod.DevApplyFailure?.Value == true) throw Failure("opt_test_failure");
            }
            phase = "refresh"; RefreshMachine(); RefreshDonors(moves);
            phase = "verify";
            if (moves.Any(m => !At(m))) throw Failure("opt_verify_failed");
        }

        bool Rollback(List<Record> original)
        {
            bool ok = true;
            foreach (var r in original)
                try
                {
                    if (At(new Move { Item=r.Item, Parent=r.Parent, Shape=r.Shape })) continue;
                    if (r.Item.parentInventory != null && (!CanRemove(r.Item) || !r.Item.parentInventory.Expel(r.Item))) ok = false;
                }
                catch (Exception e) { ok=false; ModePuzzleMod.Log?.Warning($"Rollback expel uid={r.Item.uniqueId}: {e}"); }
            foreach (var r in original)
                try
                {
                    var m = new Move { Item=r.Item, Parent=r.Parent, Shape=r.Shape };
                    if (At(m)) continue;
                    if (r.Item.parentInventory != null || !r.Parent.CheckInventorySlotStillValid(r.Item,r.Shape,r.Item.modifiedState) || Slot(m).TryAcceptOnce() != r.Item.unitCount || !At(m)) ok=false;
                }
                catch (Exception e) { ok=false; ModePuzzleMod.Log?.Warning($"Rollback insert uid={r.Item.uniqueId}: {e}"); }
            try { RefreshMachine(); RefreshDonors(original.Select(r => new Move { Item=r.Item, Parent=r.Parent })); }
            catch (Exception e) { ok=false; ModePuzzleMod.Log?.Warning("Rollback refresh: "+e); }
            foreach (var r in original)
                if (!At(new Move { Item=r.Item, Parent=r.Parent, Shape=r.Shape }))
                {
                    ok=false;
                    ModePuzzleMod.Log?.Warning($"Rollback placement missing uid={r.Item.uniqueId}");
                    // Never destroy an unplaced item; try an ordinary storage destination as a last resort.
                    if (r.Item.parentInventory == null)
                        try { var m=Reserve(r.Item,new List<Move>()); Slot(m).TryAcceptOnce(); }
                        catch (Exception e) { ModePuzzleMod.Log?.Warning($"Recovery storage uid={r.Item.uniqueId}: {e}"); }
                }
            return ok;
        }

        void RefreshMachine() => Refresh(machine, bay);

        /// <summary>Other machines that a borrowed module left or returned to (an unlocked machine's bay).</summary>
        void RefreshDonors(IEnumerable<Move> moves)
        {
            var bays = new HashSet<GameInventory>();
            foreach (var m in moves)
            {
                if (m.Parent != null) bays.Add(m.Parent);
                if (records.TryGetValue(m.Item.uniqueId, out var r) && r.Parent != null) bays.Add(r.Parent);
            }
            foreach (var inv in bays)
                if (inv != bay && inv is GameGridInventory grid && inv.GetParentItem() is GameItem owner &&
                    owner.IsGameItemType("MACHINE") && MachineHelper.GetModuleInv(owner) == grid)
                    Refresh(owner, grid);
        }

        static void Refresh(GameItem machine, GameGridInventory bay)
        {
            ModuleEffectHelper.ComputeAllModuleTempStat(bay.childItems);
            ModuleHelper.ComputeModuleEffect(bay, machine, machine);
            if (machine.IsTag("BASE_PROCESSING_SPEED_TAG") && machine.IsTag("MACHINERY_DRAWN_POWER_BASE"))
                ModuleHelper.ApplyBasicModuleEffect(bay, machine, machine);
            MachineryHelper.UpdatePowerUsageProcessTypeMachine(machine);
            bay.Validate(); machine.Validate();
        }
    }
}
