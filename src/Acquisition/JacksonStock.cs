using System.Collections.Generic;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// 杰克逊博士 stocks the optimizer, both when he visits the store and at his own shop: the game fires
    /// OnPlaceInventorInventoryItemLate from PlaceInventorInventory in both cases, with the player's owned
    /// items. Adding it to the selling table there gives it his usual pricing (markup, perks, events).
    /// </summary>
    static class JacksonStock
    {
        public static void Enable() => ModHook.OnPlaceInventorInventoryItemLate += OnPlaceStock;
        public static void Disable() => ModHook.OnPlaceInventorInventoryItemLate -= OnPlaceStock;

        static void OnPlaceStock(List<GameItem> ownedItems)
        {
            if (Tuning.JacksonOnlyWhenNotOwned && Ownership.OwnsOptimizer(ownedItems)) return;
            PlayerStore.Instance?.AddDirectSellingItemToTable(OptimizerItem.Id);
        }
    }
}
