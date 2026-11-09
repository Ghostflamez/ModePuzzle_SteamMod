using System.Linq;
namespace ModePuzzle
{
    /// <summary>The 超科技模组优化器 item: a tool and high-level contraband that opens the optimizer panel.</summary>
    static class OptimizerItem
    {
        public const string Id = "modepuzzle_optimizer";
        public const long Value = 500;
        const int ContrabandLevelHigh = 3;   // ContrabandHelper: 1 low, 2 mid, 3 high, 4 critical

        /// <summary>Diagnostics: factory calls since the game started (spawns and items decoded from a save).</summary>
        public static int CreatedCount;
        /// <summary>Diagnostics: items restored from a save (their onLoaded ran).</summary>
        public static int LoadedCount;

        public static GameItem Create()
        {
            CreatedCount++;
            var item = ItemDirectory.CreateEmptyItem(Id);
            SetLook(item);

            ApplyTexts(item);
            // A save stores the item's name and texts and restores them over the factory's; onLoaded runs
            // after that, so saved copies pick up the current name and language.
            item.onLoaded += loaded => { LoadedCount++; ApplyTexts(loaded); FixLegacyShape(loaded); };
            item.SetGameItemType("TOOL");
            ContrabandHelper.InitContrabandItem(item, ContrabandLevelHigh);
            item.SetValue(Value);
            ModHelper.SetSource(item, ModePuzzleMod.ModId, ModePuzzleMod.Manifest?.Name);

            // Double-click, the context menu's 激活 and the default activate key all end in ActivateSlotItem.
            item.mayActivateSlotItemFunc = (self, inventory, slot) => true;
            item.canActivateSlotItemFunc = (self, inventory, slot) => true;
            item.onActivateSlotItemFunc = (self, inventory, slot) => PanelHost.Open();
            // 装备 (right click) and then click a machine, or drag the item onto a machine: open the panel on it.
            // Any owned machine with a module bay qualifies, chemistry machines excepted.
            item.maySelectSlotItemFunc = (self, inventory, slot) => true;
            item.canSelectSlotItemFunc = (self, inventory, slot) => true;
            item.mayThisTargetItemFunc = (self, target) => Game.LiveReader.IsSupportedMachine(target);
            item.canThisTargetItemFunc = (self, target) => Game.LiveReader.IsSupportedMachine(target);
            item.onThisTargetItemFunc = (self, target) =>
            {
                PanelHost.OpenFor(target.uniqueId);
                // The game keeps a used tool equipped (ContinueEquipAfterUse); put this one down so the cursor is
                // free for the panel.
                try { ItemSelectHandler.current?.EndSelect(); }
                catch (System.Exception e) { ModePuzzleMod.Log?.Warning("End equip: " + e.Message); }
            };
            return item;
        }

        /// <summary>Sprite and shape: the shape is the base sprite's as the game reads it (3x2, like the Deal Maker).</summary>
        static void SetLook(GameItem item)
        {
            item.SetSpriteAndShape(IconSprite.ScannerAtlas, IconSprite.ScannerSprite);
            var shape = item.shape;
            if (IconSprite.Ensure())
            {
                item.SetSpriteAndShapeFromMod(ModePuzzleMod.ModId, IconSprite.Key);
                if (shape != null) item.SetShape(shape);
            }
        }

        /// <summary>
        /// Saves from the time the item looked like the (upright) gene scanner store a 2x3 shape, so the sideways
        /// Deal Maker picture did not match the cells it took. Such an item gets the 3x2 shape turned a quarter,
        /// which covers exactly the same cells: nothing moves, and the picture now turns with it. If the cells
        /// would differ, everything is put back.
        /// </summary>
        static void FixLegacyShape(GameItem item)
        {
            try
            {
                var oldShape = item.shape;
                var placed = item.modifiedShape;
                if (oldShape == null || placed == null || oldShape.width != 2 || oldShape.height != 3) return;
                var oldCells = Game.LiveReader.PlacedCells(placed).ConvertAll(c => c.X + "," + c.Y);
                SetLook(item);
                if (item.shape == null || item.shape.width != 3 || item.shape.height != 2) { item.SetShape(oldShape); item.modifiedShape = placed; return; }
                var turned = new GridShapeBuilder(item.shape).SetTransform(placed.minX, placed.minY, placed.flipped, (placed.orientation + 1) % 4).Build();
                var newCells = Game.LiveReader.PlacedCells(turned).ConvertAll(c => c.X + "," + c.Y);
                oldCells.Sort(); newCells.Sort();
                if (!oldCells.SequenceEqual(newCells))
                {
                    item.SetShape(oldShape); item.modifiedShape = placed;
                    ModePuzzleMod.Log?.Warning("Legacy optimizer shape kept: the turned shape would cover other cells");
                    return;
                }
                item.modifiedShape = turned;
                ModePuzzleMod.Log?.Log("Legacy optimizer shape converted to 3x2 (turned a quarter, same cells)");
            }
            catch (System.Exception e) { ModePuzzleMod.Log?.Warning("Legacy optimizer shape: " + e.Message); }
        }

        static void ApplyTexts(GameItem item)
        {
            item.name = Loc.T("item_name");
            item.shortDescription = Loc.T("item_desc");
            item.flavorText = Loc.T("item_flavor");
        }
    }
}
