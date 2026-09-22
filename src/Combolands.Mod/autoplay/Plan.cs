using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay
{
    // The ranked shortlist. Pure, like Value - it takes a Snapshot and hands back
    // coordinates, and knows nothing about how they will be drawn or acted on.
    internal static class Plan
    {
        internal struct Candidate
        {
            public int X;
            public int Y;
            public Value.Breakdown Score;
        }

        // Ranks every legal tile and returns the best `count`, spread out.
        //
        // `separation` is not cosmetic. Every term in Value is an absolute count of
        // nearby pieces, so a tile beside a building always outscores open ground and
        // the top five land inside one cluster - five highlights that are really one
        // suggestion, with genuinely different options never shown. Requiring picks to
        // be `separation` tiles apart turns the shortlist back into a set of choices.
        //
        // It costs ranking fidelity on purpose: #2 is no longer the second-best tile,
        // it is the best tile that is somewhere else. That is the question a player
        // holding a building is actually asking.
        internal static List<Candidate> Best(Snapshot board, int count, int separation = 0)
        {
            var all = new List<Candidate>(256);
            var simulated = board.Simulated;

            int minX = board.SearchMinX < 0 ? 0 : board.SearchMinX;
            int minY = board.SearchMinY < 0 ? 0 : board.SearchMinY;
            int maxX = board.SearchMaxX > board.Width - 1 ? board.Width - 1 : board.SearchMaxX;
            int maxY = board.SearchMaxY > board.Height - 1 ? board.Height - 1 : board.SearchMaxY;

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!board.IsBuildable(x, y)) continue;

                    // Rejected before scoring, not filtered after: a forbidden tile
                    // should never reach the shortlist at all, or a board where most
                    // good tiles are illegal would show five bad ones.
                    if (!Rules.Allows(board, x, y)) continue;

                    var score = simulated ? Value.Quick(board, x, y) : Value.Score(board, x, y);
                    all.Add(new Candidate { X = x, Y = y, Score = score });
                }

            // Two passes, when the simulator is in use.
            //
            // Scoring a tile costs a pass over the buildings that can see it. Working
            // out what a tile SETS UP costs that again for every follow-up on the bar
            // and every tile near it - about fifty times as much - and on a full board
            // that is millions of operations for a shortlist of eight.
            //
            // So the lookahead is run only on the tiles that could plausibly win. A
            // tile the points term already places outside the top few dozen is not
            // going to be rescued by what it sets up, and the two passes agree on the
            // answer while the second one costs a fiftieth of what one pass would.
            if (simulated)
            {
                all.Sort(Compare);

                var deep = Math.Max(count * 4, 48);
                if (deep > all.Count) deep = all.Count;

                for (int i = 0; i < deep; i++)
                {
                    var candidate = all[i];
                    candidate.Score = Value.Score(board, candidate.X, candidate.Y);
                    all[i] = candidate;
                }

                if (all.Count > deep) all.RemoveRange(deep, all.Count - deep);
            }

            // Ties are broken by position so the same board always produces the same
            // list. A shortlist that reshuffles every frame is unreadable even when
            // every entry in it is correct.
            all.Sort(Compare);

            if (separation <= 0)
            {
                if (all.Count > count) all.RemoveRange(count, all.Count - count);
                return all;
            }

            var picked = new List<Candidate>(count);
            Take(all, picked, count, separation);

            // If the board is too cramped to find `count` tiles that far apart, fill
            // the rest from the same ranking without the constraint. Showing four
            // suggestions because the fifth was crowded out would look like a bug.
            if (picked.Count < count) Take(all, picked, count, 0);
            return picked;
        }

        private static void Take(List<Candidate> ranked, List<Candidate> picked, int count, int separation)
        {
            for (int i = 0; i < ranked.Count && picked.Count < count; i++)
            {
                var candidate = ranked[i];
                if (TooClose(picked, candidate, separation)) continue;
                picked.Add(candidate);
            }
        }

        private static bool TooClose(List<Candidate> picked, Candidate candidate, int separation)
        {
            for (int i = 0; i < picked.Count; i++)
            {
                int dx = Math.Abs(picked[i].X - candidate.X);
                int dy = Math.Abs(picked[i].Y - candidate.Y);
                // Chebyshev: a square of side 2*separation+1 around each pick. Cheaper
                // than Euclidean and, for "is this visibly somewhere else", the same
                // answer.
                if (dx <= separation && dy <= separation) return true;
            }
            return false;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int byScore = b.Score.Total.CompareTo(a.Score.Total);
            if (byScore != 0) return byScore;
            int byY = a.Y.CompareTo(b.Y);
            if (byY != 0) return byY;
            return a.X.CompareTo(b.X);
        }
    }
}
