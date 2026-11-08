namespace ModePuzzle.Acquisition
{
    /// <summary>Where and how often the 超科技模组优化器 can be obtained. Chances are percentages.</summary>
    static class Tuning
    {
        // 杰克逊博士: offered on every visit and at his shop while the player owns none.
        public const bool JacksonOnlyWhenNotOwned = true;

        // 可疑的走私犯: sells only the optimizer, at a markup over its value.
        public const string SmugglerId = "modepuzzle_smuggler";
        public const int SmugglerMarkupPercent = 50;
        public const int SmugglerFirstDay = 5;
        public const float SmugglerDailyChance = 10f;
        public const int SmugglerCooldownDays = 7;

        // Loot crates: one roll per crate, multiplied by the crate's luck like the game's own rolls.
        public const float EngineeringCrateChance = 3f;   // 工程铸造厂物资箱
        public const float EvidenceCrateChance = 3f;      // 治安部物证柜
    }
}
