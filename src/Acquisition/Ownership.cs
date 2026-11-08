using System.Collections.Generic;

namespace ModePuzzle.Acquisition
{
    static class Ownership
    {
        /// <summary>Puts the optimizer on the selling table as stolen goods with the given heat (percent).</summary>
        public static void OfferStolen(int heatMin, int heatMax)
        {
            int heat = RNG.GetRandomInt(heatMin, heatMax);   // inclusive of both ends
            PlayerStore.Instance?.AddDirectSellingItemToTable(OptimizerItem.Id, false, true, false, heat);
        }

        public static bool OwnsOptimizer(List<GameItem> owned = null)
        {
            owned = owned ?? EmporiumEntry.Instance?.GetAllOwnedItems();
            if (owned == null) return false;
            foreach (var item in owned)
                if (item != null && item.identifier == OptimizerItem.Id) return true;
            return false;
        }
    }
}
