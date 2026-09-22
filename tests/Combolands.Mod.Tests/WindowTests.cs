using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // A suggestion off the edge of the screen is invisible - highlights are drawn in
    // world space - so the shortlist just looks short. The window narrows where a
    // piece may be PLACED; it must not narrow what the valuation can see, because a
    // building just off screen is often exactly what makes an on-screen tile good.
    public class WindowTests
    {
        private static Snapshot Board(int size)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = 1, Range = 2, Categories = new[] { 7 } };
            return board;
        }

        private static void Place(Snapshot board, int x, int y, int tag, params int[] categories)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = tag, Range = 2, Categories = categories });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void EverySuggestionFallsInsideTheWindow()
        {
            var board = Board(30);
            board.SearchWithin(10, 10, 14, 14);

            var best = Plan.Best(board, 8, 0);
            Assert.NotEmpty(best);

            foreach (var candidate in best)
            {
                Assert.InRange(candidate.X, 10, 14);
                Assert.InRange(candidate.Y, 10, 14);
            }
        }

        [Fact]
        public void ABuildingOUTSIDETheWindowStillCounts()
        {
            // (9,12) is one step outside the window and in range of (11,12). If the
            // window narrowed the valuation rather than the placement, these two
            // boards would score identically - and the helper would recommend tiles
            // next to things it had decided not to look at.
            var withNeighbour = Board(30);
            withNeighbour.SearchWithin(10, 10, 14, 14);
            Place(withNeighbour, 9, 12, 2, 7);

            var bare = Board(30);
            bare.SearchWithin(10, 10, 14, 14);

            var a = Value.Score(withNeighbour, 10, 12);
            var b = Value.Score(bare, 10, 12);

            Assert.True(a.Adjacent > 0);
            Assert.Equal(0, b.Adjacent);
            Assert.True(a.Total > b.Total);
        }

        [Fact]
        public void AWindowLargerThanTheBoardIsClampedRatherThanRanging()
        {
            var board = Board(6);
            board.SearchWithin(-40, -40, 9999, 9999);

            Assert.Equal(0, board.SearchMinX);
            Assert.Equal(0, board.SearchMinY);
            Assert.Equal(5, board.SearchMaxX);
            Assert.Equal(5, board.SearchMaxY);

            Assert.Equal(36, Plan.Best(board, 100, 0).Count);
        }

        [Fact]
        public void TheDefaultWindowIsTheWholeBoard()
        {
            // Tests written before the window existed must keep meaning what they did.
            var board = Board(7);
            Assert.Equal(49, Plan.Best(board, 100, 0).Count);
        }

        [Fact]
        public void ASingleTileWindowStillProducesASuggestion()
        {
            var board = Board(30);
            board.SearchWithin(7, 7, 7, 7);

            var best = Plan.Best(board, 8, 3);
            Assert.Single(best);
            Assert.Equal(7, best[0].X);
            Assert.Equal(7, best[0].Y);
        }

        [Fact]
        public void AWindowOverNothingBuildableReturnsNothingRatherThanThrowing()
        {
            var board = Board(30);
            board.SearchWithin(3, 3, 4, 4);
            for (int y = 3; y <= 4; y++)
                for (int x = 3; x <= 4; x++)
                    board.Buildable[y * board.Width + x] = false;

            Assert.Empty(Plan.Best(board, 8, 3));
        }
    }
}
