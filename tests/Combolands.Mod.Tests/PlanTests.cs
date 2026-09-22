using System.Collections.Generic;
using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // Boards written by hand. This is the only way to state a case precisely, and
    // the whole reason Snapshot carries no Unity types.
    public class PlanTests
    {
        // Tags matter now: two pieces of the SAME tag are scored as a same-type pair
        // rather than as category affinity, so a fixture that leaves every tag at the
        // default 0 is testing the wrong thing. The candidate is tag 1 and placed
        // buildings are tag 2 unless a test says otherwise.
        private const int CandidateTag = 1, OtherTag = 2;

        private static Snapshot Empty(int w, int h, params int[] candidateCategories)
        {
            var board = new Snapshot { Width = w, Height = h, Buildable = new bool[w * h] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = CandidateTag, Range = 2, Categories = candidateCategories };
            return board;
        }

        private static void Place(Snapshot board, int x, int y, int range, params int[] categories)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = OtherTag, Range = range, Categories = categories });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void AnEmptyBoardRanksEveryTileAndPicksAnInteriorOne()
        {
            var board = Empty(7, 7);
            var best = Plan.Best(board, 3);

            Assert.Equal(3, best.Count);

            // With nothing to synergise with, the only term left is room to grow, so
            // the pick is open ground rather than the map edge. It used to be the
            // corner - see TargetTests.OnABareBoardTheTopPickIsNotOnTheEdge for the
            // regression that fixed.
            Assert.Equal(0, best[0].Score.Covers);
            Assert.Equal(0, best[0].Score.Adjacent);
            Assert.True(best[0].Score.Room > 0);
        }

        [Fact]
        public void ATileBesideASharedCategoryClusterBeatsOpenGround()
        {
            var board = Empty(11, 11, 1, 2);
            Place(board, 5, 5, 2, 1);
            Place(board, 6, 5, 2, 1);
            Place(board, 5, 6, 2, 2);

            var best = Plan.Best(board, 1);
            var top = best[0];

            Assert.True(top.Score.Shared >= 2,
                "the best tile should share a category with most of the cluster");
            Assert.True(Value.Touching(top.X, top.Y, 5, 5)
                     || Value.Touching(top.X, top.Y, 6, 5)
                     || Value.Touching(top.X, top.Y, 5, 6),
                "the best tile should touch the cluster");
        }

        [Fact]
        public void SharingACategoryAcrossTheMapIsWorthNothing()
        {
            // Two boards, identical except that the far building shares a category.
            // If distance were ignored the scores would differ; they must not.
            var near = Empty(21, 21, 1);
            Place(near, 20, 20, 1, 1);

            var none = Empty(21, 21, 1);
            Place(none, 20, 20, 1, 9);

            var a = Value.Score(near, 0, 0);
            var b = Value.Score(none, 0, 0);

            Assert.Equal(0, a.Shared);
            Assert.Equal(b.Total, a.Total, 4);
        }

        [Fact]
        public void OccupiedTilesAreNeverSuggested()
        {
            var board = Empty(5, 5, 1);
            Place(board, 2, 2, 1, 1);

            foreach (var candidate in Plan.Best(board, 25))
                Assert.False(candidate.X == 2 && candidate.Y == 2);
        }

        [Fact]
        public void TheOrderIsStableForTheSameBoard()
        {
            var board = Empty(9, 9, 1, 2);
            Place(board, 4, 4, 2, 1);
            Place(board, 3, 5, 2, 2);

            var first = Plan.Best(board, 6);
            var second = Plan.Best(board, 6);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].X, second[i].X);
                Assert.Equal(first[i].Y, second[i].Y);
            }
        }

        [Fact]
        public void AskingForMoreThanFitsReturnsWhatThereIs()
        {
            var board = Empty(2, 2, 1);
            Place(board, 0, 0, 1, 1);
            Place(board, 1, 1, 1, 1);

            var best = Plan.Best(board, 10);
            Assert.Equal(2, best.Count);
        }

        [Fact]
        public void ReachAndReachedByAreCountedSeparately()
        {
            // The candidate has range 2; the neighbour has range 0 and so reaches
            // nothing back. A valuation that conflated the two would score these the
            // same, and they are not the same: one building targeting another is not
            // the other targeting it.
            var board = Empty(9, 9);
            board.Candidate = new Piece { Tag = CandidateTag, Range = 2, Categories = new int[0] };
            Place(board, 4, 4, 0);

            var score = Value.Score(board, 4, 5);
            Assert.Equal(1, score.Covers);
            Assert.Equal(0, score.CoveredBy);
        }
    }
}
