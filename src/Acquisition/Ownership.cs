using System.Collections.Generic;

namespace ModePuzzle.Acquisition
{
    static class Ownership
    {
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
