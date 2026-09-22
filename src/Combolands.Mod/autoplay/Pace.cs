namespace Combolands.Mod.Autoplay
{
    // How much hurry the run is in, and what that should change.
    //
    // A milestone is "reach `ScoreRequired` within `WeeksRemaining` weeks". Whether a
    // placement is good depends on which of those is scarce:
    //
    //   plenty of weeks left  ->  build the engine. A tile that sets up future
    //                             synergies is worth more than one that scores now
    //   running out of weeks  ->  score. An engine that pays off in four weeks is
    //                             worthless with two left
    //
    // The valuation cannot tell those apart on its own - it has one set of weights -
    // so this supplies the dial and Value applies it.
    //
    // This is a POLICY, not an optimum. A true optimum would have to see the trigger
    // cascades, which mutate live state as they run and so cannot be evaluated
    // without committing to them. What is computable is the board as it stands and
    // how far behind pace it leaves you, and that is enough to decide which way to
    // lean.
    internal struct Pace
    {
        // 0 = the milestone is already met, nothing is urgent.
        // 1 = exactly on pace.
        // >1 = behind, and the number is how many times the current rate is needed.
        public float Pressure;

        public long Remaining;
        public int WeeksLeft;
        public bool Known;

        // No information is not the same as no hurry, but it has to behave like
        // something - and "play normally" is the least wrong default.
        internal static Pace Unknown { get { return new Pace { Pressure = 1f, Known = false }; } }

        internal static Pace From(long score, long required, int weeksLeft, long scoredLastWeek)
        {
            var pace = new Pace { Known = true, WeeksLeft = weeksLeft };

            pace.Remaining = required - score;
            if (pace.Remaining <= 0) { pace.Pressure = 0f; return pace; }

            // Out of weeks with points still owed is as urgent as it gets. Saying
            // "infinitely urgent" would make every weight meaningless, so it is
            // clamped to the same ceiling as merely hopeless.
            if (weeksLeft <= 0) { pace.Pressure = Ceiling; return pace; }

            // Last week's score is the only honest estimate of this week's. Averaging
            // the whole milestone would understate a board that has just come good,
            // which is exactly when the decision matters.
            var rate = scoredLastWeek > 0 ? scoredLastWeek : 1L;
            var needPerWeek = (double)pace.Remaining / weeksLeft;

            pace.Pressure = (float)(needPerWeek / rate);
            if (pace.Pressure > Ceiling) pace.Pressure = Ceiling;
            if (pace.Pressure < 0f) pace.Pressure = 0f;
            return pace;
        }

        private const float Ceiling = 4f;

        // What the points-now term gets multiplied by.
        //
        // Comfortably ahead it is halved, so the engine terms decide. Badly behind it
        // doubles, and a tile that pays this week wins. The curve is deliberately
        // gentle: pace is an estimate built on one week of history, and a valuation
        // that swings wildly on it would be worse than one that ignores it.
        internal float ScoreNowWeight
        {
            get
            {
                if (!Known) return 1f;
                if (Pressure <= 0f) return 0.5f;
                if (Pressure < 1f) return 0.5f + 0.5f * Pressure;

                var value = 1f + 0.5f * (Pressure - 1f);
                return value > 2f ? 2f : value;
            }
        }

        // And what the room-to-grow term gets multiplied by: the mirror of the above,
        // because being behind is exactly when future space stops being worth
        // anything.
        internal float FutureWeight
        {
            get
            {
                if (!Known) return 1f;
                if (Pressure <= 0f) return 1.5f;
                if (Pressure < 1f) return 1.5f - 0.5f * Pressure;
                var value = 1f - 0.4f * (Pressure - 1f);
                return value < 0.2f ? 0.2f : value;
            }
        }

        public override string ToString()
        {
            if (!Known) return "pace unknown";
            if (Pressure <= 0f) return "milestone already met";
            return string.Format("{0:N0} pts in {1} wk, pressure {2:0.0}",
                Remaining, WeeksLeft, Pressure);
        }
    }
}
