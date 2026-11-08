namespace ModePuzzle
{
    static class OptimizerTuning
    {
        public const float PollSeconds = 0.125f, WatchSeconds = 0.5f;
        public static readonly int[] TimeLimitsSeconds = { 5, 15, 30, 60 };
        public const int DefaultTimeIndex = 1;
        public const float RowGap = 6f;
        public const float ControlWidth = 120f, RangeWidth = 65f, ResultHeightShare = 0.37f;
    }
}
