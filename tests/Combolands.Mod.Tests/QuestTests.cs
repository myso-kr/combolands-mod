using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // A council request is a commitment for a whole milestone, and the only kind the
    // valuation can be steered toward is "score N points with category X". What it
    // must do is tip the choice BETWEEN buildings; what it must not do is disturb the
    // choice of tile, where it carries no information at all.
    public class QuestTests
    {
        private const int Nature = 5, Industry = 6;

        private static Snapshot Board(int questCategory, params int[] candidateCategories)
        {
            const int size = 9;
            var board = new Snapshot
            {
                Width = size,
                Height = size,
                Buildable = new bool[size * size],
                QuestCategory = questCategory,
            };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = 70, Range = 2, Categories = candidateCategories };
            return board;
        }

        [Fact]
        public void A_building_of_the_requested_category_is_worth_more()
        {
            var wanted = Value.Score(Board(Nature, Nature), 4, 4);
            var other = Value.Score(Board(Nature, Industry), 4, 4);

            Assert.True(wanted.ServesQuest);
            Assert.False(other.ServesQuest);
            Assert.Equal(Value.WeightQuestCategory, wanted.Total - other.Total, 3);
        }

        [Fact]
        public void One_of_several_categories_is_enough()
        {
            Assert.True(Value.Score(Board(Industry, Nature, Industry), 4, 4).ServesQuest);
        }

        [Fact]
        public void No_active_request_changes_nothing()
        {
            var none = Value.Score(Board(-1, Nature), 4, 4);
            var other = Value.Score(Board(Industry, Nature), 4, 4);

            Assert.False(none.ServesQuest);
            Assert.Equal(other.Total, none.Total, 3);
        }

        // The bonus is constant across the board by construction, so it cannot make a
        // worse tile look better than a good one. If this ever fails, the term has
        // grown a dependence on position and the shortlist is being steered by
        // something that knows nothing about tiles.
        [Fact]
        public void It_does_not_reorder_tiles()
        {
            var without = Plan.Best(Board(-1, Nature), 5, 0);
            var with = Plan.Best(Board(Nature, Nature), 5, 0);

            Assert.Equal(without.Count, with.Count);
            for (int i = 0; i < without.Count; i++)
            {
                Assert.Equal(without[i].X, with[i].X);
                Assert.Equal(without[i].Y, with[i].Y);
            }
        }
    }
}
