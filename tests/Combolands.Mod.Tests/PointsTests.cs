using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // The ranking is decided by POINTS, not by a normalised count of targets.
    //
    // It was normalised once, on the reasoning that a building paying 6 a target and
    // one paying 30 are both doing their best. That is a fairness argument, and
    // fairness is not the question "which of these three cards" asks. The board is
    // not random - what is on it and what each target pays are both decided - so the
    // points a placement earns right now are computable, and worth computing.
    public class PointsTests
    {
        private const int Trees = 50, Nature = 5;

        private static Snapshot Board(int size, int perTarget)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = 1, Range = 2, Categories = new[] { 1 } };
            board.TargetTagScores[Trees] = perTarget;
            board.MaxTargetScore = perTarget;
            return board;
        }

        private static void PlaceTree(Snapshot board, int x, int y)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = Trees, Range = 0, Categories = new[] { Nature } });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void ThirtyAPieceBeatsSixAPieceForTheSameCount()
        {
            var rich = Board(11, 30);
            PlaceTree(rich, 5, 5); PlaceTree(rich, 4, 5); PlaceTree(rich, 6, 5);

            var poor = Board(11, 6);
            PlaceTree(poor, 5, 5); PlaceTree(poor, 4, 5); PlaceTree(poor, 6, 5);

            var a = Value.Score(rich, 5, 6);
            var b = Value.Score(poor, 5, 6);

            Assert.Equal(90, a.TargetScore);
            Assert.Equal(18, b.TargetScore);
            Assert.True(a.Total > b.Total,
                "90 points must outrank 18 - normalising made these equal");
        }

        [Fact]
        public void ThreeExpensiveTargetsBeatTenCheapOnes()
        {
            // 3 x 30 = 90 against 10 x 6 = 60. The normalised reading had this
            // backwards, because it counted targets rather than points.
            var few = Board(15, 30);
            PlaceTree(few, 7, 7); PlaceTree(few, 6, 7); PlaceTree(few, 8, 7);

            var many = Board(15, 6);
            int placed = 0;
            for (int dy = -1; dy <= 1 && placed < 10; dy++)
                for (int dx = -2; dx <= 2 && placed < 10; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    PlaceTree(many, 7 + dx, 7 + dy);
                    placed++;
                }

            var a = Value.Score(few, 7, 8);
            var b = Value.Score(many, 7, 8);

            Assert.True(a.TargetScore > b.TargetScore * 0.9f,
                $"expensive {a.TargetScore} vs cheap {b.TargetScore}");
            Assert.True(a.Total > b.Total);
        }

        [Fact]
        public void PointsStillDecideOverBareAdjacency()
        {
            var board = Board(21, 20);
            PlaceTree(board, 4, 10); PlaceTree(board, 3, 10); PlaceTree(board, 4, 11);

            // A crowd of things it does not want.
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                    board.Buildings.Add(new Piece
                    {
                        X = 15 + dx, Y = 10 + dy, Tag = 99, Range = 2, Categories = new[] { 77 },
                    });

            var best = Plan.Best(board, 1)[0];
            Assert.True(best.X < 10, $"expected the wanted cluster, got ({best.X},{best.Y})");
        }

        [Fact]
        public void TheNormalisedCountSurvivesForTheLabel()
        {
            // "x3 targets" reads better on a tile than "90 points", so it is still
            // computed - it just no longer feeds the ranking.
            var board = Board(11, 30);
            PlaceTree(board, 5, 5); PlaceTree(board, 4, 5); PlaceTree(board, 6, 5);

            var score = Value.Score(board, 5, 6);
            Assert.Equal(3f, score.TargetHits, 3);
        }
    }
}
