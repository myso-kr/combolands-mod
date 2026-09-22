using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay.Sim
{
    // What a placement is worth, in points, exactly.
    //
    // A placement changes two things: what the new building scores, and what every
    // building that can see the new tile scores. The second half is the one a
    // proximity heuristic can only gesture at - putting a Tree beside a Woodcutter is
    // worth whatever the Woodcutter pays for trees, times the Woodcutter's
    // multiplier, and that is a number, not a feeling.
    //
    // Computed incrementally rather than by rescoring the board. Only buildings whose
    // reach contains the new tile can change, and finding them costs a pass over the
    // building list instead of a pass over every building's whole neighbourhood.
    internal static class Evaluate
    {
        // The change in one week's score if `tag` were built at (x, y).
        //
        // The board is left exactly as it was found.
        internal static double Delta(Rules rules, Map map, int tag, int x, int y, float multiplier = 1f)
        {
            if (!map.IsEmpty(x, y)) return 0;

            double before = AffectedTotal(rules, map, x, y);

            var index = map.Place(tag, x, y, multiplier);
            double after = AffectedTotal(rules, map, x, y) + Preview.PerWeek(rules, map, index);
            map.UndoLastPlacement();

            return after - before;
        }

        // Every building already on the board that can see (x, y), summed.
        //
        // A building can only be affected by a change at (x, y) if (x, y) is inside
        // its own reach - which is the same test its own scoring uses, run backwards.
        private static double AffectedTotal(Rules rules, Map map, int x, int y)
        {
            double total = 0;

            for (int i = 0; i < map.Count; i++)
            {
                if (!Sees(rules, map, i, x, y)) continue;
                total += Preview.PerWeek(rules, map, i);
            }

            return total;
        }

        internal static bool Sees(Rules rules, Map map, int index, int x, int y)
        {
            var placed = map.At(index);
            var piece = rules.Get(placed.Tag);
            if (piece == null) return false;

            int dx = x - placed.X, dy = y - placed.Y;

            switch (piece.Reach)
            {
                case Reach.SelfOnly: return dx == 0 && dy == 0;
                case Reach.Adjacent: return dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1 && !(dx == 0 && dy == 0);
                case Reach.InRange:  return Map.InRange(dx, dy, piece.Range);
                default: return false;
            }
        }

        // --- what a placement sets up -------------------------------------------------

        // Points are what a tile pays THIS week. A milestone is won over several, and
        // a tile that pays nothing now but completes a cluster next week can be the
        // better one - which is what the old heuristic was reaching for with its
        // adjacency and room terms, without ever being able to say how much.
        //
        // This says how much: the best single follow-up placement the board would
        // allow afterwards, from the pieces the run can actually draw. It is one ply,
        // not a search - a second ply multiplies the cost by the board and buys less
        // than tuning the weight on this one, which is what the trainer does.
        internal static double Potential(Rules rules, Map map, int tag, int x, int y,
                                         IList<int> available, int sampleRadius = 3)
        {
            if (available == null || available.Count == 0) return 0;
            if (!map.IsEmpty(x, y)) return 0;

            map.Place(tag, x, y);

            double best = 0;
            for (int i = 0; i < available.Count; i++)
            {
                var follow = available[i];

                for (int dy = -sampleRadius; dy <= sampleRadius; dy++)
                    for (int dx = -sampleRadius; dx <= sampleRadius; dx++)
                    {
                        int fx = x + dx, fy = y + dy;
                        if (!map.IsBuildable(fx, fy)) continue;
                        if (!Legal(rules, map, follow, fx, fy)) continue;

                        var gain = Delta(rules, map, follow, fx, fy);
                        if (gain > best) best = gain;
                    }
            }

            map.UndoLastPlacement();
            return best;
        }

        // --- the one placement rule the simulator needs -------------------------------

        // Two of the same building may not sit in each other's range, for the
        // buildings that declare it. This is the rule autoplay kept tripping over in
        // game, and it is cheap to respect here - a plan that proposes an illegal
        // tile is a plan the game will refuse, which costs a whole turn.
        //
        // The rest of the game's placement rules need an instance to evaluate and are
        // enforced by the game itself at the moment of placing. See
        // docs/ANCHORS.md rows P13 and P14.
        internal static bool Legal(Rules rules, Map map, int tag, int x, int y)
        {
            if (!map.IsBuildable(x, y)) return false;

            var piece = rules.Get(tag);
            if (piece == null) return false;

            if (piece.TileTypes.Length > 0)
            {
                var type = map.TileType(x, y);
                bool allowed = false;
                for (int i = 0; i < piece.TileTypes.Length; i++)
                    if (piece.TileTypes[i] == type) { allowed = true; break; }
                if (!allowed) return false;
            }

            if (!piece.SameTypeRestricted) return true;

            var r = piece.Range;
            for (int i = 0; i < map.Count; i++)
            {
                var other = map.At(i);
                if (other.Tag != tag) continue;

                int dx = other.X - x, dy = other.Y - y;
                if (Map.InRange(dx, dy, r)) return false;

                // Symmetric: the existing one must not reach the new one either, and
                // ranges can differ once a building has been upgraded.
                var otherPiece = rules.Get(other.Tag);
                if (otherPiece != null && Map.InRange(dx, dy, otherPiece.Range)) return false;
            }

            return true;
        }
    }
}
