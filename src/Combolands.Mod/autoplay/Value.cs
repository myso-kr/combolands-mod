namespace Combolands.Mod.Autoplay
{
    // What one placement on one tile is worth.
    //
    // This is a HEURISTIC and not a simulation. Combolands scores through cascading
    // triggers that mutate live state as they run, so there is no way to ask "what
    // would this placement score" without committing to it.
    //
    // But it is not a guess either, and that is the difference from where this
    // started. Every building DECLARES what it is looking for and how much each one
    // is worth - `GamePiece.TargetTags` with `GetScoreForTag`, and
    // `Values.TargetCategories` with `GetScoreForTargetCategory`. A Woodcutter says
    // "Trees, 30 points each". The valuation reads those declarations and counts what
    // the candidate would actually find nearby.
    //
    // The generic terms below - reach, adjacency, shared categories - are what is left
    // for buildings whose effect is not expressible as "score per target nearby", and
    // they are tiebreakers rather than the main signal.
    internal static class Value
    {
        // Declared points, RAW. This is the term that decides the ranking.
        //
        // It used to be normalised by the largest score the candidate declares, on the
        // reasoning that a building paying 6 a target and one paying 30 are both doing
        // their best. That is a fairness argument, and fairness is not what "which of
        // these three cards" is asking. Normalised, 30x3 = 90 points and 6x3 = 18
        // points both read as "x3" and rank equal - which is simply wrong.
        //
        // The board is not random. What is on it, what each piece targets and what
        // each target pays are all decided, so the points a placement would earn right
        // now are computable, and computing them beats approximating them. The scale
        // is small because raw scores run to the tens and hundreds while every other
        // term here counts pieces.
        internal const float WeightTargetPoints = 0.05f;

        internal const float WeightCovers = 0.35f;     // pieces this one reaches
        internal const float WeightCoveredBy = 0.45f;  // pieces that reach this one
        internal const float WeightAdjacent = 0.30f;   // pieces touching it
        internal const float WeightShared = 0.60f;     // per pair that shares a category
        internal const float WeightSameTag = -1.0f;    // per neighbour of the same type

        // Room to grow, and POSITIVE.
        //
        // This was a penalty on open neighbours, which on an empty board made the map
        // edge the highest-scoring place there is - every synergy term is zero, so the
        // only signal left pushed every suggestion into a corner. Exactly backwards
        // for a first placement. Keeping suggestions apart is the shortlist's job
        // (Plan's separation), not this one's; this one says "you will have somewhere
        // to build the rest of the cluster".
        internal const float WeightRoom = 0.05f;

        internal struct Breakdown
        {
            public int Covers;
            public int CoveredBy;
            public int Adjacent;
            public int Shared;
            public int SameTag;
            public int TargetScore;     // raw, as the game declares it
            public float TargetHits;    // normalised - "how many useful targets"
            public int Room;
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

                // Reach OR adjacency: the game's two notions of proximity, and which
                // one a given building uses is buried in its behaviour. A target that
                // satisfies either is one the candidate plausibly interacts with.
                if (!covers && !adjacent) continue;

                result.TargetScore += board.DeclaredScoreFor(other);

                if (other.Tag == candidate.Tag)
                {
                    // Two of the same building are the one pair usually worth LESS.
                    // They share every category by construction, so affinity would
                    // rank them highest - but the game's own text keeps excluding
                    // them: "Does not affect other [Fisher]".
                    result.SameTag++;
                }
                else if (candidate.SharesCategoryWith(other))
                {
                    result.Shared++;
                }
            }

            // Kept for the label and the panel: "x3 targets" reads better on a tile
            // than "90 points". It no longer feeds the ranking.
            if (board.MaxTargetScore > 0)
                result.TargetHits = (float)result.TargetScore / board.MaxTargetScore;

            result.Room = BuildableWithin(board, x, y, RoomRadius(candidate));

            // The milestone decides which half of this matters. With weeks to spare,
            // a tile that sets up future synergies beats one that pays now; with two
            // weeks left and a target to hit, an engine that pays off in four is
            // worth nothing. Pace supplies the dial - see Pace.cs.
            var now = board.Pace.ScoreNowWeight;
            var later = board.Pace.FutureWeight;

            result.Total = WeightTargetPoints * result.TargetScore * now
                         + WeightCovers * result.Covers * later
                         + WeightCoveredBy * result.CoveredBy * later
                         + WeightAdjacent * result.Adjacent
                         + WeightShared * result.Shared
                         + WeightSameTag * result.SameTag
                         + WeightRoom * result.Room * later;
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

        // A building with range 0 still wants elbow room for whatever goes next to it,
        // so the floor is 2 rather than the candidate's own range.
        private static int RoomRadius(Piece candidate)
        {
            return candidate.Range < 2 ? 2 : candidate.Range;
        }

        private static int BuildableWithin(Snapshot board, int x, int y, int radius)
        {
            int free = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (dx * dx + dy * dy >= radius * radius + radius) continue;
                    if (board.IsBuildable(x + dx, y + dy)) free++;
                }
            return free;
        }
    }
}
