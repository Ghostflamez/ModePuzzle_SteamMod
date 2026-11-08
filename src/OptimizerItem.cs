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
            // The shape comes from the base sprite as the game reads it (3x2, like the Deal Maker itself):
            // SetSpriteAndShapeFromMod derives a transposed 2x3 shape from the same 48x32 sprite.
            item.SetSpriteAndShape(IconSprite.ScannerAtlas, IconSprite.ScannerSprite);
            var shape = item.shape;
            if (IconSprite.Ensure())
            {
                item.SetSpriteAndShapeFromMod(ModePuzzleMod.ModId, IconSprite.Key);
                if (shape != null) item.SetShape(shape);
            }

            ApplyTexts(item);
            // A save stores the item's name and texts and restores them over the factory's; onLoaded runs
            // after that, so saved copies pick up the current name and language.
            item.onLoaded += loaded => { LoadedCount++; ApplyTexts(loaded); };
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
            item.onThisTargetItemFunc = (self, target) => PanelHost.OpenFor(target.uniqueId);
            return item;
        }

        static void ApplyTexts(GameItem item)
        {
            item.name = Loc.T("item_name");
            item.shortDescription = Loc.T("item_desc");
            item.flavorText = Loc.T("item_flavor");
        }
    }
}
