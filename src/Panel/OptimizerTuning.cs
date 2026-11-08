namespace ModePuzzle
{
    static class OptimizerTuning
    {
        public const float PollSeconds = 0.125f, WatchSeconds = 0.5f;
        public static readonly int[] TimeLimitsSeconds = { 5, 15, 30, 60 };
        public const int DefaultTimeIndex = 1;
        public const float RowGap = 6f;
        /// <summary>Large step of the custom range steppers.</summary>
        public const int RangeStep = 10;
    }
}
