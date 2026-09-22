using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay.Sim
{
    // `_BuildingBehaviour.GetScorePreview`, reproduced.
    //
    // This is the function the whole simulator rests on, and it is worth being exact
    // about what it is. The game's own copy is pure: given a building on a board it
    // returns which tiles it scores off and for how much, touching nothing. `UI.
    // MaxPossibleScores` calls it for every building, multiplies each sum by that
    // building's `Multiplier`, and shows you the result. That display IS the week's
    // score, because `ScoreController.ScorePoints` ends at
    // `round(sum * originMultiplier)` through the same data.
    //
    // So a week can be computed rather than watched, and this is the computation.
    //
    // The order of the tests below is the game's and matters: a tag match PRECLUDES a
    // category match, which precludes a rarity match. A building that is both a
    // target tag and a target category pays once, at the tag rate. Getting that
    // backwards would overpay every specialised building on the board.
    //
    // What the old heuristic got wrong, and why this file exists: it counted a target
    // that was in range OR adjacent, for every building. The game picks exactly one
    // of those per building - `_scorePreviewMode` - so every adjacency-scoring
    // building was being credited with its whole range. On a dense board that is off
    // by a factor of five or more, always upward, which is precisely the error that
    // makes a milestone plan miss.
    internal static class Preview
    {
        // What one building scores in one week, before its multiplier.
        //
        // `into` collects which tiles paid, for the overlay and for tests. Pass null
        // when only the total is wanted, which is the planner's usual case.
        internal static long Raw(Rules rules, Map map, int index, Dictionary<long, int> into = null)
        {
            var placed = map.At(index);
            var piece = rules.Get(placed.Tag);

            // A building the dump could not read scores nothing here. That is the
            // safe direction: the planner under-rates it rather than building a
            // milestone around something it does not understand.
            if (piece == null || piece.Fidelity == Fidelity.Unknown) return 0;

            long total = 0;
            var paid = into ?? Scratch;
            paid.Clear();

            switch (piece.Reach)
            {
                case Reach.SelfOnly:
                    // Its own tile is the whole neighbourhood.
                    Add(paid, placed.X, placed.Y, piece.SelfScore, ref total);
                    break;

                case Reach.Adjacent:
                    for (int i = 0; i < Map.AdjacentDX.Length; i++)
                        Score(rules, map, piece, placed.X + Map.AdjacentDX[i],
                              placed.Y + Map.AdjacentDY[i], paid, ref total);
                    break;

                case Reach.InRange:
                    var r = piece.Range;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (!Map.InRange(dx, dy, r)) continue;
                            Score(rules, map, piece, placed.X + dx, placed.Y + dy, paid, ref total);
                        }
                    break;
            }

            return total;
        }

        // And with the multiplier on, which is what actually lands on the tally:
        // ScoreController.ScorePoints -> PointsScorer.ExecuteScoring ends at
        // round(num * originMultiplier).
        internal static long Scored(Rules rules, Map map, int index)
        {
            var raw = Raw(rules, map, index);
            if (raw == 0) return 0;

            return (long)Math.Round(raw * (double)map.At(index).Multiplier, MidpointRounding.AwayFromZero);
        }

        // What it is worth PER WEEK, which is not the same thing.
        //
        // A building on a cooldown pays its full score once every N turns, so over a
        // milestone it earns a fraction of what one payout looks like. Planning on
        // the payout rather than the rate is how a board full of slow buildings
        // arrives at the last week short - and it is a factor of three or four on
        // some of the strongest-looking pieces in the game.
        //
        // Fractional, deliberately: rounding a cooldown-3 building down to zero would
        // be a worse lie than the fraction.
        internal static double PerWeek(Rules rules, Map map, int index)
        {
            var scored = Scored(rules, map, index);
            if (scored == 0) return 0;

            var piece = rules.Get(map.At(index).Tag);
            var cooldown = piece == null ? 1 : Math.Max(1, piece.Cooldown);
            return (double)scored / cooldown;
        }

        // Every building on the board, which is one week.
        internal static double Week(Rules rules, Map map)
        {
            double total = 0;
            for (int i = 0; i < map.Count; i++) total += PerWeek(rules, map, i);
            return total;
        }

        // --- one tile -----------------------------------------------------------------

        private static void Score(Rules rules, Map map, Piece piece, int x, int y,
                                  Dictionary<long, int> paid, ref long total)
        {
            if (!map.Inside(x, y)) return;

            var occupant = map.OccupantAt(x, y);

            if (occupant < 0)
            {
                // An empty tile pays only for its terrain.
                int score;
                if (piece.TileTypeScores.TryGetValue(map.TileType(x, y), out score))
                    Add(paid, x, y, score, ref total);
                return;
            }

            var other = rules.Get(map.At(occupant).Tag);
            if (other == null) return;

            // The game's order. Each test is reached only if the ones above it failed,
            // so a building pays once at the most specific rate that applies.
            int found;

            if (piece.TagScores.TryGetValue(other.Tag, out found))
            {
                Add(paid, x, y, found, ref total);
                return;
            }

            if (piece.CategoryScores.Count > 0 && other.Categories.Length > 0)
            {
                // The TARGET's categories, in its own order, and the first one with a
                // non-zero score wins - `AddScore` refuses a tile it has already
                // written, so the loop in the game cannot pay twice for one tile.
                for (int i = 0; i < other.Categories.Length; i++)
                {
                    if (!piece.CategoryScores.TryGetValue(other.Categories[i], out found)) continue;
                    if (found == 0) continue;

                    Add(paid, x, y, found, ref total);
                    return;
                }
            }

            if (piece.RarityScores.TryGetValue(other.Rarity, out found))
                Add(paid, x, y, found, ref total);
        }

        // AddScore: a zero is not a payment, and a tile pays at most once.
        private static void Add(Dictionary<long, int> paid, int x, int y, int score, ref long total)
        {
            if (score == 0) return;

            var key = ((long)x << 32) | (uint)y;
            if (paid.ContainsKey(key)) return;

            paid[key] = score;
            total += score;
        }

        // The planner calls Raw tens of thousands of times a plan and throws the tile
        // breakdown away every time. One dictionary, reused, rather than one per call.
        // Not thread-safe, and the planner is not threaded.
        [ThreadStatic] private static Dictionary<long, int> _scratch;

        private static Dictionary<long, int> Scratch
        {
            get { return _scratch ?? (_scratch = new Dictionary<long, int>(64)); }
        }
    }
}
