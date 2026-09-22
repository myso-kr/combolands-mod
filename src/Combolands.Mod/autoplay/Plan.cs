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

        // Ranks every buildable tile and returns the best `count`.
        //
        // `shortlist` is deliberately larger than what gets shown: Overlay asks the
        // game whether each one is really legal, and the game's rules - terrain,
        // range restrictions, nesting - reject some. Ranking a few more than needed
        // means the shortlist survives that filtering without a second pass.
        internal static List<Candidate> Best(Snapshot board, int count)
        {
            var all = new List<Candidate>(256);

            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    if (!board.IsBuildable(x, y)) continue;
                    var score = Value.Score(board, x, y);
                    all.Add(new Candidate { X = x, Y = y, Score = score });
                }

            // Ties are broken by position so the same board always produces the same
            // list. A shortlist that reshuffles every frame is unreadable even when
            // every entry in it is correct.
            all.Sort(Compare);

            if (all.Count > count) all.RemoveRange(count, all.Count - count);
            return all;
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
