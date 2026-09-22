using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // The two rules the helper has to evaluate itself.
    //
    // CanBuildBuildingAt is authoritative, but two of its checks read the held
    // building's CURRENT position rather than the tile being asked about, and the
    // same-type one is memoised in a cache keyed by Building that ignores the
    // override tile. Probing thirty tiles in a frame gets one answer thirty times,
    // which is how the helper came to suggest tiles the game would refuse.
    public class RulesTests
    {
        private const int Farm = 11, Plaza = 214, Obelisk = 403;

        private static Snapshot Board(int size, int candidateTag, int candidateRange)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = candidateTag, Range = candidateRange, Categories = new[] { 1 } };
            board.CandidateRangeRestricted = true;
            board.PlazaTag = Plaza;
            board.CorruptedObeliskTag = Obelisk;
            return board;
        }

        private static void Place(Snapshot board, int x, int y, int tag, int range)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = tag, Range = range, Categories = new[] { 1 } });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void ATileWithinOwnRangeOfTheSameTypeIsRefused()
        {
            var board = Board(11, Farm, 2);
            Place(board, 5, 5, Farm, 2);

            // (5,6) is one step away: 0 + 1 < 4 + 2, so the candidate's range covers
            // the existing Farm.
            Assert.True(Rules.SameTypeInRange(board, 5, 6));
            Assert.False(Rules.Allows(board, 5, 6));
        }

        [Fact]
        public void TheRuleUsesTheCANDIDATESRangeFromTheProbedTile()
        {
            // The existing building has range 0 and reaches nothing. What matters is
            // whether the candidate, placed here, would reach IT.
            var board = Board(11, Farm, 2);
            Place(board, 5, 5, Farm, 0);

            Assert.True(Rules.SameTypeInRange(board, 6, 5));    // in the candidate's range
            Assert.False(Rules.SameTypeInRange(board, 9, 9));   // far enough away
        }

        [Fact]
        public void ADifferentTypeInRangeIsFine()
        {
            var board = Board(11, Farm, 2);
            Place(board, 5, 5, Farm + 1, 2);

            Assert.False(Rules.SameTypeInRange(board, 5, 6));
            Assert.True(Rules.Allows(board, 5, 6));
        }

        [Fact]
        public void APieceWithoutTheRestrictionIgnoresTheRuleEntirely()
        {
            var board = Board(11, Farm, 2);
            board.CandidateRangeRestricted = false;
            Place(board, 5, 5, Farm, 2);

            Assert.False(Rules.SameTypeInRange(board, 5, 6));
        }

        [Fact]
        public void APlazaNextDoorExemptsTheTile()
        {
            var board = Board(11, Farm, 2);
            Place(board, 5, 5, Farm, 2);
            Place(board, 4, 7, Plaza, 1);

            // (5,6) touches the Plaza at (4,7) even though the Farm is still in range.
            Assert.True(Value.Touching(5, 6, 4, 7));
            Assert.False(Rules.SameTypeInRange(board, 5, 6));
        }

        [Fact]
        public void ACorruptedObeliskForbidsItsOwnRange()
        {
            var board = Board(11, Farm, 1);
            Place(board, 5, 5, Obelisk, 2);

            Assert.True(Rules.NearCorruptedObelisk(board, 6, 6));    // 2 < 6
            Assert.False(Rules.NearCorruptedObelisk(board, 9, 9));
            Assert.False(Rules.Allows(board, 6, 6));
        }

        [Fact]
        public void AnUnresolvedTagTurnsItsRuleOffRatherThanMatchingTagZero()
        {
            var board = Board(11, Farm, 1);
            board.CorruptedObeliskTag = -1;
            Place(board, 5, 5, 0, 5);      // tag 0 is GameTag.None

            Assert.False(Rules.NearCorruptedObelisk(board, 5, 6));
        }

        [Fact]
        public void ForbiddenTilesNeverReachTheShortlist()
        {
            var board = Board(9, Farm, 2);
            Place(board, 4, 4, Farm, 2);

            foreach (var candidate in Plan.Best(board, 81))
                Assert.False(Rules.SameTypeInRange(board, candidate.X, candidate.Y));
        }
    }
}
