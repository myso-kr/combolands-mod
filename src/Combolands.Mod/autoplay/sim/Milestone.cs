using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay.Sim
{
    // A whole milestone, played out.
    //
    // The loop is the game's, and it is simpler than it looks from inside a run:
    // placing one building from the choice bar calls `GameController.EndTurn`
    // immediately, and end of turn scores EVERY building on the board
    // (`TriggerController.AddEndOfTurnTriggersToAll`). So one placement is one week,
    // and the board you finish week three with is the board that scores again in
    // weeks four through ten.
    //
    // That is the whole reason a milestone can be planned rather than reacted to, and
    // it is what the trainer optimises against: an episode here is one milestone, and
    // the reward is whether the target was actually cleared.
    //
    // Blueprints are the exception, and they are modelled: placing one does NOT end
    // the turn. `PlacingBuilding` jumps straight past `EndTurn` for a consumable, so
    // a blueprint is a free building - the same board, the same scoring, one fewer
    // week spent. A milestone with three blueprints in hand is three placements ahead
    // of one without, and a planner that valued them at zero would hold them.
    internal sealed class Milestone
    {
        internal readonly Rules Rules;
        internal readonly Map Map;
        internal readonly long Required;
        internal readonly int Weeks;

        // What the choice bar can offer. The real pool is weighted, unlocked and
        // rerollable; this is the set of tags the run can actually draw, and the
        // episode draws uniformly from it. A policy fitted against a uniform pool is
        // fitted against a harder, flatter world than the real one.
        internal readonly int[] Pool;

        internal int OffersPerWeek = 3;

        // Blueprint placements in hand. Each is a building placed without ending the
        // turn - the one way to get ahead of the week count - and the loop spends
        // them rather than hoarding, which is what Items.ShouldSpend decides in game.
        internal int Blueprints;

        internal Milestone(Rules rules, Map map, long required, int weeks, int[] pool)
        {
            if (rules == null) throw new ArgumentNullException("rules");
            if (map == null) throw new ArgumentNullException("map");

            Rules = rules;
            Map = map;
            Required = required;
            Weeks = weeks;
            Pool = pool ?? new int[0];
        }

        internal struct Outcome
        {
            public bool Cleared;
            public long Score;
            public long Required;
            public int WeeksUsed;
            public int Placements;

            // Margin as a fraction of the target: +0.2 is twenty percent clear, -0.1
            // is ten percent short. The trainer needs this rather than the bare
            // Cleared flag - "missed by one percent" and "missed by eighty" should
            // not be the same reward, or there is no gradient to climb.
            public double Margin { get { return Required <= 0 ? 0 : (double)(Score - Required) / Required; } }

            public override string ToString()
            {
                return string.Format("{0} {1:N0}/{2:N0} in {3} weeks ({4:+0.0%;-0.0%;0%})",
                    Cleared ? "cleared" : "MISSED", Score, Required, WeeksUsed, Margin);
            }
        }

        // Play it. Deterministic for a given seed, which is what makes a training run
        // reproducible and a regression explicable.
        internal Outcome Play(Policy policy, Random random)
        {
            var outcome = new Outcome { Required = Required };

            var offers = new List<int>(OffersPerWeek);
            double lastWeek = 0;
            double score = 0;

            for (int week = 0; week < Weeks; week++)
            {
                outcome.WeeksUsed = week + 1;

                Draw(offers, random);

                // Free placements first. A blueprint spent before the week's real
                // building is a blueprint the real building can be placed next to.
                while (Blueprints > 0)
                {
                    int bt, bx, by;
                    if (!Choose(policy, offers, (long)score, week, lastWeek, out bt, out bx, out by)) break;

                    Map.Place(bt, bx, by);
                    outcome.Placements++;
                    Blueprints--;
                }

                int tag, x, y;
                if (Choose(policy, offers, (long)score, week, lastWeek, out tag, out x, out y))
                {
                    Map.Place(tag, x, y);
                    outcome.Placements++;
                }

                // End of turn: every building on the board scores again.
                lastWeek = Preview.Week(Rules, Map);
                score += lastWeek;
                outcome.Score = (long)Math.Round(score);

                if (outcome.Score >= Required)
                {
                    outcome.Cleared = true;
                    break;
                }
            }

            return outcome;
        }

        private void Draw(List<int> into, Random random)
        {
            into.Clear();
            if (Pool == null || Pool.Length == 0) return;

            // Without replacement where the pool allows it, because the real bar
            // never offers the same building twice in one row.
            var taken = new HashSet<int>();
            for (int i = 0; i < OffersPerWeek && taken.Count < Pool.Length; i++)
            {
                int tag;
                do { tag = Pool[random.Next(Pool.Length)]; } while (!taken.Add(tag));
                into.Add(tag);
            }
        }

        // The best legal placement of any offered building, by the policy's own
        // reckoning. This is also exactly what the planner does in game, which is the
        // point: the thing being trained and the thing being run are one method.
        internal bool Choose(Policy policy, IList<int> offers, long score, int week, double lastWeek,
                             out int tag, out int x, out int y)
        {
            tag = 0; x = 0; y = 0;

            var situation = Situation.From(score, Required, Weeks - week, lastWeek);
            var best = double.NegativeInfinity;
            var found = false;

            for (int i = 0; i < offers.Count; i++)
            {
                var candidate = offers[i];

                // The follow-ups this placement could set up are the OTHER things on
                // offer. Using the whole pool would credit a placement for a building
                // the run may never see.
                var rest = Others(offers, i);

                for (int ty = 0; ty < Map.Height; ty++)
                    for (int tx = 0; tx < Map.Width; tx++)
                    {
                        if (!Evaluate.Legal(Rules, Map, candidate, tx, ty)) continue;

                        var features = Policy.Measure(Rules, Map, candidate, tx, ty, situation, rest);
                        var value = policy.Value(features);

                        if (value <= best) continue;

                        best = value;
                        tag = candidate; x = tx; y = ty;
                        found = true;
                    }
            }

            return found;
        }

        private static List<int> Others(IList<int> offers, int except)
        {
            var rest = new List<int>(offers.Count - 1);
            for (int i = 0; i < offers.Count; i++)
                if (i != except) rest.Add(offers[i]);
            return rest;
        }
    }
}
