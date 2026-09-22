using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Combolands.Mod.Autoplay.Sim
{
    // How a placement is chosen, and the handful of numbers that decide it.
    //
    // The features below are exact quantities, not proxies. `Now` is the points this
    // placement adds to every remaining week - which is the correction that matters
    // most, because the old valuation had no week multiplier at all. Every building
    // on the board scores again every single turn
    // (`TriggerController.AddEndOfTurnTriggersToAll` walks all of them), so a
    // placement made with eight weeks left is worth eight times what the same
    // placement is worth on the last one. Ranking by this week's delta alone treats
    // those as equal, and in a long milestone that is how you arrive at the last week
    // short.
    //
    // The weights are not guessed. They are fitted by `tools/train` against simulated
    // milestones, scored on whether the target was actually cleared - see
    // docs/SIMULATION.md. What is hand-written here is the SHAPE: which quantities a
    // placement is allowed to be judged on. That is a modelling decision and should
    // be legible; the numbers are an optimisation and should not be.
    internal sealed class Policy
    {
        // Four, not six, and the missing two are the point.
        //
        // The first version weighted all three features and could not be trained at
        // all: every sampled policy played identically, to the placement, across
        // every generation. An argmax is invariant to a positive rescaling of the
        // whole weight vector, so one degree of freedom was pure redundancy - and
        // with `Now` an order of magnitude larger than the other terms, the argmax
        // was simply "most points", whatever the other weights said.
        //
        // So the points term is FIXED at one and everything else is priced against
        // it. Now the weights mean something a person can argue with: potentialFlat
        // is how many points of future setup are worth one point today.
        internal const int Size = 4;

        internal float PotentialFlat;   // future points, valued against present ones
        internal float PotentialUrgent; // ...and how that changes when behind pace
        internal float RoomFlat;        // elbow room, priced in weeks of scoring
        internal float RoomUrgent;

        // A starting point that already plays sensibly, so training improves on
        // something rather than starting from noise - and so the mod has usable
        // numbers before anyone runs the trainer.
        //
        // Read: a point of set-up is worth about half a point scored now, and behind
        // pace it is worth nothing at all. Room is a tiebreaker worth a fraction of
        // one week's quota.
        internal static Policy Default()
        {
            return new Policy
            {
                PotentialFlat = 0.50f,
                PotentialUrgent = -0.45f,
                RoomFlat = 0.03f,
                RoomUrgent = -0.03f,
            };
        }

        internal float[] ToVector()
        {
            return new[] { PotentialFlat, PotentialUrgent, RoomFlat, RoomUrgent };
        }

        internal static Policy FromVector(float[] w)
        {
            if (w == null || w.Length != Size)
                throw new ArgumentException("a policy is " + Size + " numbers");

            return new Policy
            {
                PotentialFlat = w[0], PotentialUrgent = w[1],
                RoomFlat = w[2], RoomUrgent = w[3],
            };
        }

        // --- the features -------------------------------------------------------------

        internal struct Features
        {
            // All three in POINTS, so the weights below are ratios rather than
            // conversions between incomparable units. That is what lets a policy
            // fitted on a milestone wanting three thousand work on one wanting three
            // hundred thousand.
            public double Now;
            public double Potential;
            public double Room;
            public float Urgency;

            // Kept for explaining a choice on screen: "1,400 points over 4 weeks"
            // reads better than a rank.
            public double DeltaPerWeek;
            public int WeeksLeft;
        }

        internal static Features Measure(Rules rules, Map map, int tag, int x, int y,
                                         Situation situation, IList<int> available)
        {
            var features = default(Features);
            var weeks = Math.Max(1, situation.WeeksLeft);

            // Every building on the board scores again every single turn, so a
            // placement is worth its weekly delta times the weeks left to collect it.
            // This is the term the old valuation did not have at all.
            var delta = Evaluate.Delta(rules, map, tag, x, y);
            features.DeltaPerWeek = delta;
            features.WeeksLeft = weeks;
            features.Now = delta * weeks;

            // The best follow-up this placement leaves open, over the weeks that
            // would remain after making it.
            features.Potential = Evaluate.Potential(rules, map, tag, x, y, available)
                               * Math.Max(0, weeks - 1);

            // Priced in weeks of the run's own quota, so it stays a tiebreaker on a
            // board paying thousands and on one paying tens.
            features.Room = Room(map, x, y, 2) * situation.NeededPerWeek;

            features.Urgency = situation.Urgency;
            return features;
        }

        internal double Value(Features f)
        {
            var u = f.Urgency;
            return f.Now
                 + (PotentialFlat + PotentialUrgent * u) * f.Potential
                 + (RoomFlat + RoomUrgent * u) * f.Room;
        }

        private static int Room(Map map, int x, int y, int radius)
        {
            int free = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!Map.InRange(dx, dy, radius)) continue;
                    if (map.IsBuildable(x + dx, y + dy)) free++;
                }
            return free;
        }

        // --- reading and writing ------------------------------------------------------

        internal string ToJson()
        {
            var text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"_comment\": \"Fitted by tools/train against simulated milestones. ");
            text.Append("The points term is fixed at 1 and these are priced against it.\",\n");

            var names = Names;
            var values = ToVector();
            for (int i = 0; i < Size; i++)
            {
                text.Append("  \"").Append(names[i]).Append("\": ");
                text.Append(values[i].ToString("0.#####", CultureInfo.InvariantCulture));
                text.Append(i == Size - 1 ? "\n" : ",\n");
            }

            text.Append("}\n");
            return text.ToString();
        }

        internal static Policy FromJson(string json)
        {
            var document = JsonValue.Parse(json);
            var names = Names;
            var values = new float[Size];

            var fallback = Default().ToVector();
            for (int i = 0; i < Size; i++)
            {
                var found = document[names[i]];
                values[i] = found == null ? fallback[i] : found.AsFloat(fallback[i]);
            }

            return FromVector(values);
        }

        internal static readonly string[] Names =
        {
            "potentialFlat", "potentialUrgent", "roomFlat", "roomUrgent",
        };

        public override string ToString()
        {
            var values = ToVector();
            var text = new StringBuilder("now=1");
            for (int i = 0; i < Size; i++)
            {
                text.Append(' ').Append(Names[i]).Append('=');
                text.Append(values[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }

    // Where the run stands. Everything a placement decision needs that is not the
    // board itself.
    internal struct Situation
    {
        public long Score;
        public long Required;
        public int WeeksLeft;

        public long Remaining { get { return Required - Score; } }

        // What this week has to earn, if the rest are to be like it. The scale
        // everything else is measured against, and never zero - a milestone already
        // met still has to rank placements somehow.
        public double NeededPerWeek
        {
            get
            {
                if (WeeksLeft <= 0) return Math.Max(1, Remaining);
                return Math.Max(1.0, (double)Math.Max(1, Remaining) / WeeksLeft);
            }
        }

        // 0 when the milestone is already in hand, 1 when it is out of reach at the
        // current rate. Clamped, because beyond "badly behind" there is nothing more
        // a placement policy can do about it.
        public float Urgency;

        internal static Situation From(long score, long required, int weeksLeft, double lastWeek)
        {
            var situation = new Situation { Score = score, Required = required, WeeksLeft = weeksLeft };

            if (situation.Remaining <= 0) { situation.Urgency = 0f; return situation; }
            if (weeksLeft <= 0) { situation.Urgency = 1f; return situation; }

            // Week one has no previous week to estimate from. No information is not
            // the same as bad news: with nothing scored yet the board is empty, and
            // the right move on an empty board is to build the engine, which is what
            // urgency zero asks for.
            if (lastWeek <= 0) { situation.Urgency = 0f; return situation; }

            var rate = lastWeek;
            var pressure = situation.NeededPerWeek / rate;

            // 1.0 is exactly on pace. Two-times behind is where it saturates; past
            // that the answer is the same answer.
            situation.Urgency = (float)Math.Max(0.0, Math.Min(1.0, (pressure - 1.0) / 1.0));
            return situation;
        }
    }
}
