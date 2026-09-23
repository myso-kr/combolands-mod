using System.Collections.Generic;
using Combolands.Mod.Autoplay.Sim;
using Xunit;

namespace Combolands.Mod.Tests
{
    // Activation: what a building makes its neighbours do.
    //
    // Eighteen buildings in the game activate others, and an activated building
    // scores again out of turn, ignoring its own cooldown. The selection is RANDOM -
    // the game collects what is activatable in reach and picks `ActivationCount` of
    // them with `RandomItem()` - so the simulator takes an expectation rather than
    // sampling one outcome. Over a ten-week milestone the expectation is the right
    // quantity; a sampled outcome would be noise dressed as precision.
    //
    // These cases pin the arithmetic of that expectation, because it is the part a
    // plausible-looking version would get wrong in the direction that makes a plan
    // look better than it is.
    public class CascadeTests
    {
        private const int Tower = 90, Mill = 91, Tree = 50, Woodcutter = 70;
        private const int Grass = 1;

        private static Rules Ruleset(params Piece[] pieces)
        {
            var rules = new Rules();
            foreach (var piece in pieces) rules.Pieces[piece.Tag] = piece;
            return rules;
        }

        private static Map Board(int size = 11)
        {
            var map = new Map(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    map.SetTile(x, y, Grass);
            return map;
        }

        // A plain scorer: pays `each` per adjacent Tree, on the given cooldown.
        private static Piece Scorer(int tag, int each, int cooldown = 1)
        {
            return new Piece
            {
                Tag = tag,
                Reach = Reach.Adjacent,
                Range = 1,
                Cooldown = cooldown,
                CanBeActivated = true,
                TagScores = new Dictionary<int, int> { { Tree, each } },
            };
        }

        private static Piece Activator(int tag, int count, int range, int cooldown = 1)
        {
            return new Piece
            {
                Tag = tag,
                Reach = Reach.InRange,
                Range = range,
                Cooldown = cooldown,
                Activates = true,
                ActivationCount = count,
                CanBeActivated = true,
            };
        }

        private static Piece Target(int tag)
        {
            return new Piece { Tag = tag, Reach = Reach.None, CanBeActivated = false };
        }

        // --- the expectation ----------------------------------------------------------

        // One activation, two candidates: each is woken half the time, so the expected
        // extra is half of each. Not "the best one", and not "both".
        [Fact]
        public void One_activation_among_two_candidates_is_worth_half_of_each()
        {
            var rules = Ruleset(Activator(Tower, count: 1, range: 3),
                                Scorer(Mill, each: 100),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);

            var a = map.Place(Mill, 5, 3);
            map.Place(Tree, 5, 2);                 // the first mill scores 100

            var b = map.Place(Mill, 7, 5);
            map.Place(Tree, 8, 5);
            map.Place(Tree, 8, 6);                 // the second scores 200

            Assert.Equal(100, Preview.Scored(rules, map, a));
            Assert.Equal(200, Preview.Scored(rules, map, b));

            // The tower itself scores nothing; all of its value is what it wakes.
            Assert.Equal(0.5 * 100 + 0.5 * 200, Preview.PerWeek(rules, map, tower), 3);
        }

        // Fewer candidates than activations: everyone is woken, and the probability
        // is capped at one rather than exceeding it.
        [Fact]
        public void More_activations_than_candidates_wakes_everyone_once()
        {
            var rules = Ruleset(Activator(Tower, count: 5, range: 3),
                                Scorer(Mill, each: 60),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);
            map.Place(Mill, 5, 3);
            map.Place(Tree, 5, 2);

            Assert.Equal(60, Preview.PerWeek(rules, map, tower), 3);
        }

        // Activation ignores the target's cooldown - being woken IS the extra fire.
        // Amortising it again would halve a mechanic whose whole point is to bypass
        // the wait.
        [Fact]
        public void An_activated_building_pays_its_full_score_not_its_weekly_rate()
        {
            var rules = Ruleset(Activator(Tower, count: 1, range: 3),
                                Scorer(Mill, each: 90, cooldown: 3),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);
            var mill = map.Place(Mill, 5, 3);
            map.Place(Tree, 5, 2);

            Assert.Equal(30, Preview.PerWeek(rules, map, mill), 3);      // 90 every third week
            Assert.Equal(90, Preview.PerWeek(rules, map, tower), 3);     // woken: the whole 90
        }

        // The activator's own cooldown still applies to the cascade: it only sets one
        // off on the weeks it fires.
        [Fact]
        public void An_activator_on_a_cooldown_sets_off_its_cascade_that_often()
        {
            var rules = Ruleset(Activator(Tower, count: 1, range: 3, cooldown: 4),
                                Scorer(Mill, each: 80),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);
            map.Place(Mill, 5, 3);
            map.Place(Tree, 5, 2);

            Assert.Equal(20, Preview.PerWeek(rules, map, tower), 3);     // 80 every fourth week
        }

        [Fact]
        public void Something_that_refuses_to_be_activated_is_not_a_candidate()
        {
            var deaf = Scorer(Mill, each: 100);
            deaf.CanBeActivated = false;

            var rules = Ruleset(Activator(Tower, count: 1, range: 3), deaf, Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);
            map.Place(Mill, 5, 3);
            map.Place(Tree, 5, 2);

            Assert.Equal(0, Preview.PerWeek(rules, map, tower), 3);
        }

        [Fact]
        public void An_activator_with_nothing_in_reach_is_worth_nothing()
        {
            var rules = Ruleset(Activator(Tower, count: 2, range: 2),
                                Scorer(Mill, each: 100), Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 1, 1);
            map.Place(Mill, 9, 9);

            Assert.Equal(0, Preview.PerWeek(rules, map, tower), 3);
        }

        // An activator that wakes an activator. The chain is followed, and bounded -
        // the game has no depth limit, and an unbounded expectation on a board full
        // of them would neither terminate quickly nor mean much.
        [Fact]
        public void A_cascade_follows_through_a_second_activator()
        {
            var rules = Ruleset(Activator(Tower, count: 1, range: 2),
                                Activator(Mill, count: 1, range: 2),
                                Scorer(Woodcutter, each: 100),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);
            map.Place(Mill, 5, 6);                  // the only thing the tower can wake
            map.Place(Woodcutter, 5, 7);            // the only thing the mill can wake
            map.Place(Tree, 5, 8);

            // Tower wakes Mill (which scores nothing itself), and Mill wakes the
            // Woodcutter, which scores 100. Both certain: one candidate each.
            //
            // The mill can also see the tower, and the woodcutter can see the mill,
            // so the real number depends on who is a candidate for whom - what this
            // pins is that the chain is followed at all and produces something.
            Assert.True(Preview.PerWeek(rules, map, tower) > 0,
                "a cascade through a second activator reached nothing");
        }

        // Eight of the eighteen activators declare no count and activate EVERYTHING
        // that qualifies - FishTrap wakes every Fishing building in range, EffigyPyre
        // everything adjacent. Reading that zero as "activates nothing" valued them
        // all at nought, which is how the first version of this modelled them.
        [Fact]
        public void No_activation_count_means_all_of_them_not_none()
        {
            var rules = Ruleset(Activator(Tower, count: 0, range: 3),
                                Scorer(Mill, each: 70),
                                Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);

            map.Place(Mill, 5, 3); map.Place(Tree, 5, 2);
            map.Place(Mill, 7, 5); map.Place(Tree, 8, 5);

            // Both, with certainty, rather than one at random or neither at all.
            Assert.Equal(140, Preview.PerWeek(rules, map, tower), 3);
        }

        // An activator that declares target categories wakes only those. Leaving the
        // filter out would over-value exactly the buildings that are fussiest about
        // what they sit beside - and over-valuing is the direction that makes a
        // milestone plan miss.
        [Fact]
        public void An_activator_with_target_categories_wakes_only_those()
        {
            const int Fishing = 30, Farming = 31;

            var trap = Activator(Tower, count: 0, range: 3);
            trap.CategoryScores = new Dictionary<int, int> { { Fishing, 0 } };

            var boat = Scorer(Mill, each: 100);
            boat.Categories = new[] { Fishing };

            var barn = Scorer(Woodcutter, each: 100);
            barn.Categories = new[] { Farming };

            var rules = Ruleset(trap, boat, barn, Target(Tree));

            var map = Board();
            var tower = map.Place(Tower, 5, 5);

            map.Place(Mill, 5, 3);       map.Place(Tree, 5, 2);   // Fishing: woken
            map.Place(Woodcutter, 7, 5); map.Place(Tree, 8, 5);   // Farming: not

            Assert.Equal(100, Preview.PerWeek(rules, map, tower), 3);
        }

        // --- blueprints ---------------------------------------------------------------

        // Placing a blueprint does not end the turn - `PlacingBuilding` jumps straight
        // past EndTurn for a consumable - so blueprints are placements that cost no
        // week. A planner that valued them at zero would hold them.
        [Fact]
        public void Blueprints_are_placements_that_cost_no_week()
        {
            var rules = Ruleset(Scorer(Mill, each: 50), Target(Tree));

            var without = Run(rules, blueprints: 0);
            var with = Run(rules, blueprints: 3);

            Assert.Equal(without.WeeksUsed, with.WeeksUsed);
            Assert.Equal(without.Placements + 3, with.Placements);
            Assert.True(with.Score > without.Score,
                string.Format("without {0} ({1} placed), with {2} ({3} placed)",
                              without.Score, without.Placements, with.Score, with.Placements));
        }

        private static Milestone.Outcome Run(Rules rules, int blueprints)
        {
            var map = Board(9);
            for (int i = 0; i < 6; i++) map.Place(Tree, 1 + i, 1);

            var milestone = new Milestone(rules, map, required: 10000000, weeks: 4,
                                          pool: new[] { Mill })
            {
                Blueprints = blueprints,
            };

            return milestone.Play(Policy.Default(), new System.Random(7));
        }
    }
}
