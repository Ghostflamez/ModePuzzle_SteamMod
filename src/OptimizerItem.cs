namespace ModePuzzle
{
    /// <summary>The 超科技模组优化器 item: a tool and high-level contraband that opens the optimizer panel.</summary>
    static class OptimizerItem
    {
        public const string Id = "modepuzzle_optimizer";
        public const long Value = 500;
        const int ContrabandLevelHigh = 3;   // ContrabandHelper: 1 low, 2 mid, 3 high, 4 critical

        public static GameItem Create()
        {
            var item = ItemDirectory.CreateEmptyItem(Id);
            if (IconSprite.Ensure()) item.SetSpriteAndShapeFromMod(ModePuzzleMod.ModId, IconSprite.Key);
            else item.SetSpriteAndShape(IconSprite.ScannerAtlas, IconSprite.ScannerSprite);

            item.name = Loc.T("item_name");
            item.shortDescription = Loc.T("item_desc");
            item.flavorText = Loc.T("item_flavor");
            item.SetGameItemType("TOOL");
            ContrabandHelper.InitContrabandItem(item, ContrabandLevelHigh);
            item.SetValue(Value);
            ModHelper.SetSource(item, ModePuzzleMod.ModId, ModePuzzleMod.Manifest?.Name);

            // Double-click, the context menu's 激活 and the default activate key all end in ActivateSlotItem.
            item.mayActivateSlotItemFunc = (self, inventory, slot) => true;
            item.canActivateSlotItemFunc = (self, inventory, slot) => true;
            item.onActivateSlotItemFunc = (self, inventory, slot) => OptimizerPanel.Open();
            return item;
        }
    }
}
