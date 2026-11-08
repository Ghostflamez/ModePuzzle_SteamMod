namespace ModePuzzle.Acquisition
{
    /// <summary>Where and how often the 超科技模组优化器 can be obtained. Chances are percentages.</summary>
    static class Tuning
    {
        // 杰克逊博士: offered on every visit and at his shop while the player owns none.
        public const bool JacksonOnlyWhenNotOwned = true;

        // Both sellers offer it as stolen goods (from the looted warehouse) whose heat has already cooled
        // while passing through other hands: a random 赃物热度 in [min, max] percent, never 100%.
        public const int JacksonHeatMin = 20, JacksonHeatMax = 35;
        public const int SmugglerHeatMin = 45, SmugglerHeatMax = 65;

        // 可疑的走私犯: sells only the optimizer, at a markup over its value.
        public const string SmugglerId = "modepuzzle_smuggler";
        public const int SmugglerMarkupPercent = 50;
        public const int SmugglerFirstDay = 5;
        public const float SmugglerDailyChance = 10f;
        public const int SmugglerCooldownDays = 7;

        // Loot crates: one roll per crate, multiplied by the crate's luck like the game's own rolls.
        // The vanilla lists put rare, valuable items first; the optimizer (500, 2×3 cells) rolls right after
        // the anchor item, while the crate is still nearly empty.
        public const float EngineeringCrateChance = 3f;   // 工程铸造厂物资箱
        public const string EngineeringCrateAnchor = "system_capped_neural_core";   // 250, 2×2, 4%; before the 扩建套件
        public const float EvidenceCrateChance = 3f;      // 治安部物证柜
        public const string EvidenceCrateAnchor = "current_converter";              // 300, 1×2, 20%; before the 500 精华液
    }
}
