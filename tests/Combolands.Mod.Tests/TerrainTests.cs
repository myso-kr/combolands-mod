using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // Terrain, for a building that is only being SCOUTED.
    //
    // When the player holds the piece the game's own CanBuildBuildingAt answers this
    // and knows more than we do - including the nineteen behaviours that override
    // CanBeBuiltOn with a rule of their own. There is no instance to ask about a card
    // in the choice bar, so the tile type is checked here instead. Pointing at ocean
    // would be worse than saying nothing.
    public class TerrainTests
    {
        private const int Grass = 0, Ocean = 1, Sand = 2;

        private static Snapshot Board(int size, params int[] allowed)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            board.TileTypes = new int[size * size];
            for (int i = 0; i < board.Buildable.Length; i++)
            {
                board.Buildable[i] = true;
                board.TileTypes[i] = Grass;
            }
            board.Candidate = new Piece { Tag = 1, Range = 2, Categories = new[] { 7 } };
            board.CandidateTileTypes = allowed.Length == 0 ? null : allowed;
            return board;
        }

        private static void SetType(Snapshot board, int x, int y, int type)
        {
            board.TileTypes[y * board.Width + x] = type;
        }

        [Fact]
        public void ATileOfTheWrongTypeIsRefused()
        {
            var board = Board(9, Grass);
            SetType(board, 4, 4, Ocean);

            Assert.True(Rules.WrongTerrain(board, 4, 4));
            Assert.False(Rules.Allows(board, 4, 4));
            Assert.False(Rules.WrongTerrain(board, 3, 3));
        }

        [Fact]
        public void SeveralAllowedTypesAllPass()
        {
            var board = Board(9, Grass, Sand);
            SetType(board, 4, 4, Sand);
            SetType(board, 5, 5, Ocean);

            Assert.False(Rules.WrongTerrain(board, 4, 4));
            Assert.True(Rules.WrongTerrain(board, 5, 5));
        }

        [Fact]
        public void NoDeclaredTypesMeansTheCheckIsOff()
        {
            // Which is the case when the player is holding the piece: the game is
            // asked instead, and it is the better authority.
            var board = Board(9);
            SetType(board, 4, 4, Ocean);

            Assert.Null(board.CandidateTileTypes);
            Assert.False(Rules.WrongTerrain(board, 4, 4));
        }

        [Fact]
        public void AnUnreadTileIsNotGuessedAt()
        {
            // Outside the window the type is -1. Refusing those would quietly shrink
            // the search to whatever happened to be read.
            var board = Board(9, Grass);
            board.TileTypes[4 * 9 + 4] = -1;

            Assert.False(Rules.WrongTerrain(board, 4, 4));
        }

        [Fact]
        public void WrongTerrainNeverReachesTheShortlist()
        {
            var board = Board(9, Grass);
            for (int x = 0; x < 9; x++) SetType(board, x, 0, Ocean);

            foreach (var candidate in Plan.Best(board, 81))
                Assert.NotEqual(0, candidate.Y);
        }
    }
}
