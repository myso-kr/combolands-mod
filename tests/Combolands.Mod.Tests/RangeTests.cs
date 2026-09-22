using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // The range shape is reproduced from Grid.GetFilledCircle:
    //
    //     dx*dx + dy*dy < r*r + r
    //
    // Every highlight the helper draws depends on it, and a square approximation
    // would be wrong for exactly the diagonals - the cases a player would notice
    // first. These pin the shape rather than the formula, so a rewrite that keeps the
    // behaviour passes and one that quietly switches to Chebyshev does not.
    public class RangeTests
    {
        private static Piece At(int x, int y, int range)
        {
            return new Piece { X = x, Y = y, Range = range };
        }

        [Fact]
        public void RangeZeroCoversNothing()
        {
            var piece = At(5, 5, 0);
            Assert.False(piece.Covers(5, 5));
            Assert.False(piece.Covers(5, 6));
        }

        [Fact]
        public void RangeOneIsAPlusNotASquare()
        {
            var piece = At(5, 5, 1);

            // 0 + 1 = 1 < 1 + 1. Orthogonal neighbours are in.
            Assert.True(piece.Covers(5, 6));
            Assert.True(piece.Covers(5, 4));
            Assert.True(piece.Covers(6, 5));
            Assert.True(piece.Covers(4, 5));

            // 1 + 1 = 2, which is not < 2. Diagonals are out - this is the case a
            // Chebyshev square would get wrong.
            Assert.False(piece.Covers(6, 6));
            Assert.False(piece.Covers(4, 4));
        }

        [Fact]
        public void RangeTwoReachesDiagonalsButNotTheCorners()
        {
            var piece = At(0, 0, 2);

            Assert.True(piece.Covers(1, 1));    // 2 < 6
            Assert.True(piece.Covers(2, 0));    // 4 < 6
            Assert.True(piece.Covers(2, 1));    // 5 < 6
            Assert.False(piece.Covers(2, 2));   // 8 is not < 6
        }

        [Fact]
        public void TheOriginCountsAsCovered()
        {
            Assert.True(At(3, 3, 1).Covers(3, 3));
        }

        [Fact]
        public void AdjacencyIsEightWayAndExcludesSelf()
        {
            Assert.True(Value.Touching(2, 2, 3, 3));
            Assert.True(Value.Touching(2, 2, 2, 3));
            Assert.False(Value.Touching(2, 2, 2, 2));
            Assert.False(Value.Touching(2, 2, 4, 2));
        }

        [Fact]
        public void SharedCategoriesAreOrderIndependent()
        {
            var a = new Piece { Categories = new[] { 3, 7 } };
            var b = new Piece { Categories = new[] { 7, 9 } };
            var c = new Piece { Categories = new[] { 1, 2 } };

            Assert.True(a.SharesCategoryWith(b));
            Assert.True(b.SharesCategoryWith(a));
            Assert.False(a.SharesCategoryWith(c));
        }

        [Fact]
        public void APieceWithNoCategoriesSharesNothing()
        {
            var bare = new Piece { Categories = null };
            var other = new Piece { Categories = new[] { 1 } };
            Assert.False(bare.SharesCategoryWith(other));
            Assert.False(other.SharesCategoryWith(bare));
        }
    }
}
