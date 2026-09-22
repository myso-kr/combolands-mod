namespace Combolands.Mod.Autoplay
{
    // The placement rules this mod has to evaluate itself, and why it cannot just ask
    // the game.
    //
    // BuildingController.CanBuildBuildingAt is authoritative, and the helper still
    // calls it - for terrain, for building-over, for the can't-build border. But two
    // of its checks read the HELD building's current position rather than the tile
    // being asked about, and one of them is memoised per Building in a cache that
    // ignores the override tile:
    //
    //     BuildingExtensions._rangeCache is keyed by Building alone, and
    //     GetTilesInRange(overrideTile) returns the cached set on a hit.
    //
    // Probing thirty tiles in one frame therefore gets one answer thirty times. That
    // is how "other building of the same type in range" leaked through and the helper
    // suggested tiles the game would refuse.
    //
    // The fix is not to fight the cache. CanBuildBuildingAt's
    // `ignoreAllPlacementRestrictions` argument gates EXACTLY the same-type check and
    // nothing else, so the helper passes true - turning that one check off - and
    // evaluates it here instead, from the tile actually being asked about.
    internal static class Rules
    {
        // "Other {0} in range": a building with the restriction cannot be placed where
        // another of its own type falls inside ITS range, measured from the candidate
        // tile. The game exempts a tile adjacent to a Plaza.
        internal static bool SameTypeInRange(Snapshot board, int x, int y)
        {
            if (!board.CandidateRangeRestricted) return false;
            if (AdjacentTo(board, x, y, board.PlazaTag)) return false;

            var candidate = board.Candidate;
            candidate.X = x;
            candidate.Y = y;

            for (int i = 0; i < board.Buildings.Count; i++)
            {
                var other = board.Buildings[i];
                if (other.Tag != candidate.Tag) continue;
                if (candidate.Covers(other.X, other.Y)) return true;
            }
            return false;
        }

        // "Buildings cannot be placed within [Range] of [CorruptedObelisk]" - the
        // obelisk's range, not the candidate's.
        internal static bool NearCorruptedObelisk(Snapshot board, int x, int y)
        {
            if (board.CorruptedObeliskTag < 0) return false;

            for (int i = 0; i < board.Buildings.Count; i++)
            {
                var other = board.Buildings[i];
                if (other.Tag != board.CorruptedObeliskTag) continue;
                if (other.Covers(x, y)) return true;
            }
            return false;
        }

        internal static bool Allows(Snapshot board, int x, int y)
        {
            return !SameTypeInRange(board, x, y) && !NearCorruptedObelisk(board, x, y);
        }

        private static bool AdjacentTo(Snapshot board, int x, int y, int tag)
        {
            if (tag < 0) return false;
            for (int i = 0; i < board.Buildings.Count; i++)
            {
                var other = board.Buildings[i];
                if (other.Tag != tag) continue;
                if (Value.Touching(x, y, other.X, other.Y)) return true;
            }
            return false;
        }
    }
}
