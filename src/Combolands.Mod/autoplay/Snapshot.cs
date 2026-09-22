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

        // TileType per tile, -1 where not read. Only needed for scouting: when the
        // player is actually holding a building the game's own CanBuildBuildingAt
        // answers the terrain question exactly.
        public int[] TileTypes;

        // Which TileTypes the candidate may sit on, or null for "do not check".
        public int[] CandidateTileTypes;

        public List<Piece> Buildings = new List<Piece>();

        // The piece the player is holding. Its X and Y are meaningless - the whole
        // point is to try it everywhere.
        public Piece Candidate;

        // Whether the candidate carries the "no other of my type in range" rule.
        // Read once per refresh from the game's own HasRangePlacementRestriction,
        // which depends on equipped heirlooms rather than on the tile.
        public bool CandidateRangeRestricted;

        // GameTag values, resolved by NAME so renumbering the enum cannot silently
        // change which building the rules are about. -1 when not found.
        public int PlazaTag = -1;
        public int CorruptedObeliskTag = -1;

        // What the candidate says it is looking for, and what each one is worth.
        //
        // Read straight off the game: GamePiece.TargetTags with GetScoreForTag, and
        // Values.TargetCategories with GetScoreForTargetCategory. This is the closest
        // the helper gets to the real scoring rule - a Woodcutter genuinely does
        // declare "Trees, 30 each" - and it is what makes a suggestion on turn one
        // worth anything, when there is nothing else on the board but terrain.
        public Dictionary<int, int> TargetTagScores = new Dictionary<int, int>();
        public Dictionary<int, int> TargetCategoryScores = new Dictionary<int, int>();

        // The largest single declared score, used to normalise. Buildings pay wildly
        // different amounts per target, and without this a Woodcutter (30 a tree)
        // would look five times better placed than a building paying 6 - when both
        // are simply doing their best.
        public int MaxTargetScore;

        // How much hurry the run is in. Defaults to "unknown", which behaves as
        // "play normally" - the tests and the scouting path both rely on that.
        public Pace Pace = Pace.Unknown;

        // What this candidate would earn from `other` being nearby. A tag match wins
        // over a category match rather than adding to it: the game's behaviours pay
        // for a target once.
        public int DeclaredScoreFor(Piece other)
        {
            int score;
            if (TargetTagScores != null && TargetTagScores.TryGetValue(other.Tag, out score))
                return score;

            if (TargetCategoryScores == null || other.Categories == null) return 0;

            int best = 0;
            for (int i = 0; i < other.Categories.Length; i++)
                if (TargetCategoryScores.TryGetValue(other.Categories[i], out score) && score > best)
                    best = score;
            return best;
        }

        // Which tiles are worth CONSIDERING, as opposed to which are on the board.
        //
        // A suggestion the player cannot see is not a suggestion: highlights are drawn
        // in world space, so an off-screen one just makes the shortlist look short.
        // Buildings outside the window still count - one off-screen can easily be in
        // range of a tile on it - so this narrows where we place, never what we see.
        //
        // Defaults cover everything, which is what the tests use.
        public int SearchMinX;
        public int SearchMinY;
        public int SearchMaxX = int.MaxValue;
        public int SearchMaxY = int.MaxValue;

        public void SearchWithin(int minX, int minY, int maxX, int maxY)
        {
            SearchMinX = minX < 0 ? 0 : minX;
            SearchMinY = minY < 0 ? 0 : minY;
            SearchMaxX = maxX > Width - 1 ? Width - 1 : maxX;
            SearchMaxY = maxY > Height - 1 ? Height - 1 : maxY;
        }

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
