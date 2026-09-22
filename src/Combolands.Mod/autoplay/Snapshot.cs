using System.Collections.Generic;

namespace Combolands.Mod.Autoplay
{
    // A board, as plain data. No Unity types, no game types, no reflection.
    //
    // This is the seam that makes the valuation testable. Board.cs reads the live
    // game into one of these; Value.cs and Plan.cs never see anything else. A fixture
    // written by hand in a test is indistinguishable from a real board.
    //
    // Tags and categories are ints - the game's enum values - because naming them
    // here would mean referencing Assembly-CSharp, and the valuation does not care
    // what "Husbandry" means, only whether two pieces share it.

    internal struct Piece
    {
        public int X;
        public int Y;
        public int Range;
        public int Tag;
        public int[] Categories;

        // The game's own range shape, from Grid.GetFilledCircle:
        //
        //     dx*dx + dy*dy < r*r + r
        //
        // A Euclidean disc with a half-step of slack, which is why range 1 covers the
        // four orthogonal neighbours but not the diagonals, and range 2 covers all
        // eight. Getting this wrong would make every highlight subtly untrue, so it
        // is reproduced exactly rather than approximated with a square.
        public bool Covers(int x, int y)
        {
            if (Range <= 0) return false;
            int dx = x - X, dy = y - Y;
            return dx * dx + dy * dy < Range * Range + Range;
        }

        public bool SharesCategoryWith(Piece other)
        {
            if (Categories == null || other.Categories == null) return false;
            for (int i = 0; i < Categories.Length; i++)
                for (int j = 0; j < other.Categories.Length; j++)
                    if (Categories[i] == other.Categories[j]) return true;
            return false;
        }
    }

    internal sealed class Snapshot
    {
        public int Width;
        public int Height;

        // Indexed y * Width + x. Buildable means the tile could hold a building as
        // far as the cheap checks go - empty, on the map, not marked can't-build. It
        // is NOT the game's full placement rule; Plan asks the game about the few
        // tiles it actually wants to show.
        public bool[] Buildable;

        public List<Piece> Buildings = new List<Piece>();

        // The piece the player is holding. Its X and Y are meaningless - the whole
        // point is to try it everywhere.
        public Piece Candidate;

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public bool IsBuildable(int x, int y)
        {
            return InBounds(x, y) && Buildable[y * Width + x];
        }
    }
}
