using System;
using System.Collections.Generic;
using System.Linq;
using Combolands.Mod.Autoplay.Sim;

namespace Combolands.Train
{
    // The milestones a policy is fitted against.
    //
    // These are generated rather than taken from the game, and that is a decision
    // worth stating plainly. What the real game hands you - board, terrain, target,
    // weeks, which three cards - is one sample. Fitting six numbers to one sample is
    // how you get a policy that clears the milestone you trained on and misses the
    // next one.
    //
    // So the trainer samples a spread: boards of different sizes and terrain mixes,
    // targets from comfortable to nearly impossible, milestones from three weeks to
    // fifteen. A policy that clears most of that spread is a policy that is playing
    // the game rather than memorising a board.
    //
    // The features are scale-free by construction - everything is divided by what the
    // run needs per week - so a policy fitted across a range of targets transfers to
    // a target it has never seen.
    internal sealed class Scenario
    {
        internal Milestone Milestone;
        internal string Describe;

        // Buildings worth offering: the ones that score something. A pool of inert
        // scenery would make every policy look identical, because every choice would
        // be worth nothing.
        internal static int[] ScoringPool(Rules rules)
        {
            return rules.Pieces.Values
                .Where(p => p.Fidelity != Fidelity.Unknown)
                .Where(p => p.TagScores.Count > 0 || p.CategoryScores.Count > 0
                         || p.RarityScores.Count > 0 || p.TileTypeScores.Count > 0
                         || p.SelfScore != 0)
                .Select(p => p.Tag)
                .OrderBy(t => t)
                .ToArray();
        }

        // And the things that sit on a board being scored off. A milestone where
        // nothing is a target is a milestone where category scoring cannot be
        // measured at all, and category scoring is most of this game.
        internal static int[] TargetPool(Rules rules)
        {
            return rules.Pieces.Values
                .Where(p => p.Categories.Length > 0)
                .Select(p => p.Tag)
                .OrderBy(t => t)
                .ToArray();
        }

        internal static Scenario Sample(Rules rules, int[] pool, int[] targets, Random random)
        {
            // A board big enough for range-5 buildings to matter, small enough that a
            // full scan of it is quick. The real map is about this.
            var size = 16 + random.Next(10);
            var map = new Map(size, size);

            // Terrain in patches rather than per tile: a board of uniformly random
            // terrain has no shorelines, and half the buildings in this game want a
            // shoreline. Patches give the policy somewhere to be clever about.
            var types = rules.TileTypeNames.Keys.ToArray();
            if (types.Length == 0) types = new[] { 0 };

            var ground = types[random.Next(types.Length)];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    map.SetTile(x, y, ground);

            var patches = 2 + random.Next(4);
            for (int i = 0; i < patches; i++)
            {
                var type = types[random.Next(types.Length)];
                int cx = random.Next(size), cy = random.Next(size), r = 2 + random.Next(4);

                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Map.InRange(dx, dy, r) && map.Inside(cx + dx, cy + dy))
                            map.SetTile(cx + dx, cy + dy, type);
            }

            // Some tiles are simply not buildable. The real map has rocks and edges,
            // and a policy that has never had to route around one will route into one.
            var blocked = size * size / 20;
            for (int i = 0; i < blocked; i++)
            {
                int x = random.Next(size), y = random.Next(size);
                map.SetTile(x, y, map.TileType(x, y), blocked: true);
            }

            // A board that has already been played on for a while. Starting every
            // episode from bare ground would fit a policy to opening moves only.
            var existing = random.Next(size * size / 12);
            for (int i = 0; i < existing && targets.Length > 0; i++)
            {
                int x = random.Next(size), y = random.Next(size);
                var tag = targets[random.Next(targets.Length)];

                if (Evaluate.Legal(rules, map, tag, x, y)) map.Place(tag, x, y);
            }

            var weeks = 3 + random.Next(13);

            // The target is set from what this board can actually pay, so an episode
            // is neither free nor impossible: a multiple of the score a good week
            // would earn. Setting it from a constant would make the difficulty depend
            // entirely on which buildings happened to be drawn.
            // Centred where the answer depends on how well it is played.
            //
            // The first version sampled 0.55x to 1.25x of what a greedy player
            // reaches, and three quarters of those milestones cleared no matter what
            // policy played them - so the search had almost nothing to select on and
            // the distribution collapsed without improving. A milestone that any
            // policy clears and one that no policy clears teach the same amount:
            // nothing. These sit around the boundary, where play decides.
            var reachable = Reachable(rules, map, pool, weeks);
            var demand = 0.9 + random.NextDouble() * 0.7;       // 0.9x to 1.6x
            var required = (long)Math.Max(1, reachable * demand);

            return new Scenario
            {
                Milestone = new Milestone(rules, map, required, weeks, pool),
                Describe = string.Format("{0}x{0} board, {1} weeks, {2:N0} points, {3} placed",
                                         size, weeks, required, map.Count),
            };
        }

        // Roughly what a competent player would score here: place the best of a few
        // random offers each week, greedily, and see where that lands. Used only to
        // set the target, so it needs to be in the right order of magnitude rather
        // than right.
        private static double Reachable(Rules rules, Map map, int[] pool, int weeks)
        {
            if (pool.Length == 0) return 1;

            var probe = map.Clone();
            var random = new Random(12345);
            double total = 0;

            for (int week = 0; week < weeks; week++)
            {
                var tag = pool[random.Next(pool.Length)];

                double best = 0;
                int bx = -1, by = -1;

                for (int y = 0; y < probe.Height; y++)
                    for (int x = 0; x < probe.Width; x++)
                    {
                        if (!Evaluate.Legal(rules, probe, tag, x, y)) continue;

                        var gain = Evaluate.Delta(rules, probe, tag, x, y);
                        if (gain <= best) continue;

                        best = gain; bx = x; by = y;
                    }

                if (bx >= 0) probe.Place(tag, bx, by);
                total += Preview.Week(rules, probe);
            }

            return Math.Max(1, total);
        }
    }
}
