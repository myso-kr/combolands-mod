using System;
using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // Every building declares what it wants and what each one is worth. Reading those
    // declarations is what turns the helper from "sit near other buildings" into
    // advice worth following - and it is the only thing that works on turn one, when
    // the board holds nothing but terrain.
    public class TargetTests
    {
        private const int Trees = 50, Rocks = 60, Woodcutter = 70;
        private const int Nature = 5, Husbandry = 6;

        private static Snapshot Board(int size)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = Woodcutter, Range = 2, Categories = new[] { 1 } };
            return board;
        }

        private static void Wants(Snapshot board, int tag, int score)
        {
            board.TargetTagScores[tag] = score;
            if (score > board.MaxTargetScore) board.MaxTargetScore = score;
        }

        private static void WantsCategory(Snapshot board, int category, int score)
        {
            board.TargetCategoryScores[category] = score;
            if (score > board.MaxTargetScore) board.MaxTargetScore = score;
        }

        private static void Place(Snapshot board, int x, int y, int tag, params int[] categories)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = tag, Range = 0, Categories = categories });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void ADeclaredTagPaysItsDeclaredScore()
        {
            var board = Board(11);
            Wants(board, Trees, 30);
            Place(board, 5, 5, Trees, Nature);

            Assert.Equal(30, Value.Score(board, 5, 6).TargetScore);
            Assert.Equal(0, Value.Score(board, 0, 0).TargetScore);
        }

        [Fact]
        public void ACategoryMatchCountsWhenTheTagDoesNot()
        {
            var board = Board(11);
            WantsCategory(board, Nature, 12);
            Place(board, 5, 5, Trees, Nature);

            Assert.Equal(12, Value.Score(board, 5, 6).TargetScore);
        }

        [Fact]
        public void ATagMatchWinsOverACategoryMatchRatherThanAdding()
        {
            // The game's behaviours pay for a target once.
            var board = Board(11);
            Wants(board, Trees, 30);
            WantsCategory(board, Nature, 12);
            Place(board, 5, 5, Trees, Nature);

            Assert.Equal(30, Value.Score(board, 5, 6).TargetScore);
        }

        [Fact]
        public void TheBestCategoryWinsWhenSeveralMatch()
        {
            var board = Board(11);
            WantsCategory(board, Nature, 12);
            WantsCategory(board, Husbandry, 25);
            Place(board, 5, 5, Trees, Nature, Husbandry);

            Assert.Equal(25, Value.Score(board, 5, 6).TargetScore);
        }

        [Fact]
        public void NormalisingPutsCheapAndExpensiveBuildingsOnTheSameScale()
        {
            // A Woodcutter paying 30 a tree and a building paying 6 a tree are both
            // simply doing their best. Three targets should read as "three targets"
            // for either of them.
            var rich = Board(11);
            Wants(rich, Trees, 30);
            Place(rich, 5, 5, Trees, Nature);
            Place(rich, 4, 5, Trees, Nature);
            Place(rich, 6, 5, Trees, Nature);

            var poor = Board(11);
            Wants(poor, Trees, 6);
            Place(poor, 5, 5, Trees, Nature);
            Place(poor, 4, 5, Trees, Nature);
            Place(poor, 6, 5, Trees, Nature);

            Assert.Equal(3f, Value.Score(rich, 5, 6).TargetHits, 3);
            Assert.Equal(3f, Value.Score(poor, 5, 6).TargetHits, 3);
        }

        [Fact]
        public void TargetsDecideTheRankingOverBareAdjacency()
        {
            // Left cluster: three things it wants. Right cluster: four things it does
            // not. The generic adjacency terms must not outvote the declared ones.
            var board = Board(21);
            Wants(board, Trees, 20);

            Place(board, 4, 10, Trees, Nature);
            Place(board, 3, 10, Trees, Nature);
            Place(board, 4, 11, Trees, Nature);

            Place(board, 15, 10, Rocks);
            Place(board, 16, 10, Rocks);
            Place(board, 15, 11, Rocks);
            Place(board, 16, 11, Rocks);

            var best = Plan.Best(board, 1)[0];
            Assert.True(best.X < 10, $"expected the wanted cluster, got ({best.X},{best.Y})");
            Assert.True(best.Score.TargetScore > 0);
        }

        [Fact]
        public void OnABareBoardOpenGroundBeatsTheCorner()
        {
            // The regression that started this: with every synergy term at zero, the
            // old crowding penalty made the map edge the highest-scoring place there
            // is - the worst possible advice for a first placement.
            var board = Board(15);

            var middle = Value.Score(board, 7, 7);
            var corner = Value.Score(board, 0, 0);

            Assert.True(middle.Room > corner.Room);
            Assert.True(middle.Total > corner.Total,
                $"middle {middle.Total} should beat corner {corner.Total}");
        }

        [Fact]
        public void OnABareBoardTheTopPickIsNotOnTheEdge()
        {
            var board = Board(15);
            var best = Plan.Best(board, 1)[0];

            Assert.True(best.X > 0 && best.Y > 0 && best.X < 14 && best.Y < 14,
                $"({best.X},{best.Y}) is on the edge");
        }

        [Fact]
        public void WithNoDeclaredTargetsNothingDividesByZero()
        {
            var board = Board(9);
            Place(board, 4, 4, Trees, Nature);

            var score = Value.Score(board, 4, 5);
            Assert.Equal(0, score.TargetScore);
            Assert.Equal(0f, score.TargetHits);
            Assert.False(float.IsNaN(score.Total));
        }
    }
}
