using System.Collections.Generic;
using Combolands.Mod.Autoplay.Sim;
using Xunit;

namespace Combolands.Mod.Tests
{
    // `_BuildingBehaviour.GetScorePreview`, pinned.
    //
    // Everything the simulator concludes rests on this one function being the game's
    // and not an approximation of it, so the cases below are the ones where a
    // plausible-looking reimplementation would differ: which neighbourhood, which
    // target wins when several match, and whether a tile can pay twice.
    public class PreviewTests
    {
        private const int Woodcutter = 70, Tree = 50, Rock = 60, House = 80;
        private const int Nature = 5, Industry = 6, Stone = 7;
        private const int Grass = 1, Water = 2;

        private static Rules Ruleset(params Piece[] pieces)
        {
            var rules = new Rules();
            foreach (var piece in pieces) rules.Pieces[piece.Tag] = piece;
            return rules;
        }

        private static Map Board(int size = 9)
        {
            var map = new Map(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    map.SetTile(x, y, Grass);
            return map;
        }

        private static Piece Scorer(int tag, Reach reach, int range,
                                    Dictionary<int, int> tags = null,
                                    Dictionary<int, int> categories = null)
        {
            return new Piece
            {
                Tag = tag,
                Reach = reach,
                Range = range,
                TagScores = tags ?? new Dictionary<int, int>(),
                CategoryScores = categories ?? new Dictionary<int, int>(),
            };
        }

        private static Piece Target(int tag, params int[] categories)
        {
            return new Piece { Tag = tag, Categories = categories, Reach = Reach.None };
        }

        // --- which neighbourhood ------------------------------------------------------

        // The one the old heuristic got wrong, and the reason the simulator exists.
        // ScorePreviewMode is Adjacent OR InRange, never both: an adjacency scorer
        // cannot see a target two tiles away, however long its range is.
        [Fact]
        public void An_adjacent_scorer_does_not_see_into_its_range()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.Adjacent, range: 3, tags: new Dictionary<int, int> { { Tree, 30 } }),
                Target(Tree, Nature));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);
            map.Place(Tree, 4, 6);                       // two away: inside range 3, not adjacent

            Assert.Equal(0, Preview.Raw(rules, map, woodcutter));

            map.Place(Tree, 4, 5);                       // adjacent
            Assert.Equal(30, Preview.Raw(rules, map, woodcutter));
        }

        [Fact]
        public void A_range_scorer_uses_the_games_filled_circle()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.InRange, range: 2, tags: new Dictionary<int, int> { { Tree, 10 } }),
                Target(Tree, Nature));

            // dx*dx + dy*dy < r*r + r, so at r=2 the limit is 6: (2,1) is 5 and counts,
            // (2,2) is 8 and does not.
            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);

            map.Place(Tree, 6, 5);
            Assert.Equal(10, Preview.Raw(rules, map, woodcutter));

            map.Place(Tree, 6, 6);
            Assert.Equal(10, Preview.Raw(rules, map, woodcutter));
        }

        [Fact]
        public void A_self_only_scorer_pays_for_its_own_tile_and_nothing_else()
        {
            var piece = Scorer(House, Reach.SelfOnly, range: 0);
            piece.SelfScore = 45;

            var rules = Ruleset(piece, Target(Tree, Nature));

            var map = Board();
            var house = map.Place(House, 4, 4);
            map.Place(Tree, 4, 5);

            Assert.Equal(45, Preview.Raw(rules, map, house));
        }

        // --- which target wins --------------------------------------------------------

        // The game tests tag, then category, then rarity, and each only if the one
        // before it failed. A building that is both a named target and a member of a
        // targeted category pays once, at the tag rate.
        [Fact]
        public void A_tag_match_beats_a_category_match()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.Adjacent, 1,
                       tags: new Dictionary<int, int> { { Tree, 30 } },
                       categories: new Dictionary<int, int> { { Nature, 5 } }),
                Target(Tree, Nature));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);
            map.Place(Tree, 4, 5);

            Assert.Equal(30, Preview.Raw(rules, map, woodcutter));
        }

        // AddScore refuses a tile it has already written, so the game's loop over the
        // target's categories cannot pay twice for one building.
        [Fact]
        public void A_target_in_two_scored_categories_still_pays_once()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.Adjacent, 1,
                       categories: new Dictionary<int, int> { { Nature, 7 }, { Industry, 11 } }),
                Target(Tree, Nature, Industry));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);
            map.Place(Tree, 4, 5);

            var raw = Preview.Raw(rules, map, woodcutter);
            Assert.True(raw == 7 || raw == 11, "paid " + raw + " - a tile must pay once");
            Assert.NotEqual(18, raw);
        }

        [Fact]
        public void A_rarity_match_applies_only_when_tag_and_category_both_miss()
        {
            var scorer = Scorer(Woodcutter, Reach.Adjacent, 1,
                                categories: new Dictionary<int, int> { { Nature, 4 } });
            scorer.RarityScores[3] = 25;

            var rules = Ruleset(scorer, Target(Tree, Nature), new Piece { Tag = Rock, Rarity = 3 });

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);

            map.Place(Rock, 4, 5);                       // no tag, no category: rarity pays
            Assert.Equal(25, Preview.Raw(rules, map, woodcutter));

            map.Place(Tree, 5, 4);                       // category pays, rarity is not consulted
            Assert.Equal(29, Preview.Raw(rules, map, woodcutter));
        }

        [Fact]
        public void An_empty_tile_pays_only_for_its_terrain()
        {
            var scorer = Scorer(Woodcutter, Reach.Adjacent, 1);
            scorer.TileTypeScores[Water] = 12;

            var rules = Ruleset(scorer, Target(Tree, Nature));

            var map = Board();
            map.SetTile(4, 5, Water);
            map.SetTile(5, 4, Water);
            var woodcutter = map.Place(Woodcutter, 4, 4);

            Assert.Equal(24, Preview.Raw(rules, map, woodcutter));

            // Occupied, so it is a building question now, not a terrain one.
            map.Place(Tree, 4, 5);
            Assert.Equal(12, Preview.Raw(rules, map, woodcutter));
        }

        [Fact]
        public void A_zero_score_is_not_a_payment()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.Adjacent, 1,
                       tags: new Dictionary<int, int> { { Tree, 0 } },
                       categories: new Dictionary<int, int> { { Nature, 9 } }),
                Target(Tree, Nature));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);
            map.Place(Tree, 4, 5);

            // The tag matched but paid nothing, and AddScore ignores a zero - so the
            // tile is still unwritten and the category branch is never reached,
            // because the game's `else if` chain already took the tag branch.
            Assert.Equal(0, Preview.Raw(rules, map, woodcutter));
        }

        // --- the multiplier -----------------------------------------------------------

        [Fact]
        public void The_multiplier_lands_on_the_total_and_is_rounded()
        {
            var rules = Ruleset(
                Scorer(Woodcutter, Reach.Adjacent, 1, tags: new Dictionary<int, int> { { Tree, 25 } }),
                Target(Tree, Nature));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4, multiplier: 1.5f);
            map.Place(Tree, 4, 5);
            map.Place(Tree, 5, 4);

            Assert.Equal(50, Preview.Raw(rules, map, woodcutter));
            Assert.Equal(75, Preview.Scored(rules, map, woodcutter));

            map.SetMultiplier(woodcutter, 1.33f);
            Assert.Equal(67, Preview.Scored(rules, map, woodcutter));   // 66.5, away from zero
        }

        // --- a building nobody understands --------------------------------------------

        // Scoring an unknown as zero is the safe direction: the planner under-rates it
        // rather than building a milestone plan around a building it cannot model.
        [Fact]
        public void An_unknown_piece_scores_nothing_rather_than_guessing()
        {
            var scorer = Scorer(Woodcutter, Reach.Adjacent, 1,
                                tags: new Dictionary<int, int> { { Tree, 30 } });
            scorer.Fidelity = Fidelity.Unknown;

            var rules = Ruleset(scorer, Target(Tree, Nature));

            var map = Board();
            var woodcutter = map.Place(Woodcutter, 4, 4);
            map.Place(Tree, 4, 5);

            Assert.Equal(0, Preview.Raw(rules, map, woodcutter));
        }
    }
}
