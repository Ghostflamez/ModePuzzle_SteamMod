namespace ModePuzzle.Engine
{
    /// <summary>Exact two-channel, three-axis numerators stored inline for search backtracking.</summary>
    internal struct Accumulator
    {
        long a0, a1, a2, b0, b1, b2;

        public Accumulator(long[][] values)
        {
            a0 = values[0][0]; a1 = values[0][1]; a2 = values[0][2];
            b0 = values[1][0]; b1 = values[1][1]; b2 = values[1][2];
        }

        public long Get(int channel, int axis) => channel == 0
            ? (axis == 0 ? a0 : axis == 1 ? a1 : a2)
            : (axis == 0 ? b0 : axis == 1 ? b1 : b2);

        public int Total(int axis) => Scoring.Trunc(Get(0, axis)) + Scoring.Trunc(Get(1, axis));

        public void Add(Accumulator extra)
        {
            a0 += extra.a0; a1 += extra.a1; a2 += extra.a2;
            b0 += extra.b0; b1 += extra.b1; b2 += extra.b2;
        }

        static int Difference(long current, long extra) => extra == 0 ? 0
            : Scoring.Trunc(current + extra) - Scoring.Trunc(current);

        public void AddDifference(Accumulator extra, int[] delta)
        {
            delta[0] += Difference(a0, extra.a0) + Difference(b0, extra.b0);
            delta[1] += Difference(a1, extra.a1) + Difference(b1, extra.b1);
            delta[2] += Difference(a2, extra.a2) + Difference(b2, extra.b2);
        }
    }
}
