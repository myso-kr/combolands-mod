namespace Combolands.Mod.Autoplay
{
    // What one placement on one tile is worth.
    //
    // This is a HEURISTIC and not a simulation, and the difference matters. Combolands
    // scores through cascading triggers - TriggerController, TriggerQueue,
    // BehavioursController - and those mutate live state as they run. There is no way
    // to ask "what would this placement score" without committing to it, so nothing
    // here can be exact.
    //
    // What it counts instead is the shape almost every building in the game rewards:
    // how many pieces this one would reach, how many would reach it, how many sit
    // next to it, and how often those pairs share a category. A building that says
    // "for each [Husbandry] in range" is invisible to this, but a tile that sits
    // among a cluster of buildings it shares categories with is where such a building
    // wants to be anyway.
    //
    // Stage 1 of the play helper exists to put these numbers on screen so a human can
    // disagree with them. The weights are a starting guess, not a result.
    internal static class Value
    {
        // Named, in one place, and deliberately small in number. Every weight here is
        // something a player can be asked about: "should being in range of things
        // matter more than being next to them?"
        internal const float WeightCovers = 1.0f;      // pieces this one reaches
        internal const float WeightCoveredBy = 1.2f;   // pieces that reach this one
        internal const float WeightAdjacent = 0.8f;    // pieces touching it
        internal const float WeightShared = 1.5f;      // per pair that shares a category
        internal const float WeightCrowding = -0.15f;  // per buildable neighbour lost

        internal struct Breakdown
        {
            public int Covers;
            public int CoveredBy;
            public int Adjacent;
            public int Shared;
            public int LostSpace;
            public float Total;
        }

        internal static Breakdown Score(Snapshot board, int x, int y)
        {
            var result = default(Breakdown);
            var candidate = board.Candidate;
            candidate.X = x;
            candidate.Y = y;

            for (int i = 0; i < board.Buildings.Count; i++)
            {
                var other = board.Buildings[i];
                bool covers = candidate.Covers(other.X, other.Y);
                bool coveredBy = other.Covers(x, y);
                bool adjacent = Touching(x, y, other.X, other.Y);

                if (covers) result.Covers++;
                if (coveredBy) result.CoveredBy++;
                if (adjacent) result.Adjacent++;

                // A shared category only counts where the two can actually interact.
                // Two Husbandry buildings on opposite corners of the map share a
                // category and nothing else.
                if ((covers || coveredBy || adjacent) && candidate.SharesCategoryWith(other))
                    result.Shared++;
            }

            // Filling a tile costs the board the space around it. Without this the
            // valuation piles everything into one corner, because the densest spot is
            // always the one next to what is already dense.
            result.LostSpace = FreeNeighbours(board, x, y);

            result.Total = WeightCovers * result.Covers
                         + WeightCoveredBy * result.CoveredBy
                         + WeightAdjacent * result.Adjacent
                         + WeightShared * result.Shared
                         + WeightCrowding * result.LostSpace;
            return result;
        }

        // Eight-neighbourhood. The game calls this "adjacent" and includes diagonals -
        // Grid.Get8NeighboursEnumerable is what its adjacency effects walk.
        internal static bool Touching(int ax, int ay, int bx, int by)
        {
            int dx = ax - bx, dy = ay - by;
            if (dx == 0 && dy == 0) return false;
            return dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1;
        }

        private static int FreeNeighbours(Snapshot board, int x, int y)
        {
            int free = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (board.IsBuildable(x + dx, y + dy)) free++;
                }
            return free;
        }
    }
}
