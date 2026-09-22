using System;
using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // Every term in Value is an absolute count of nearby pieces, so a tile beside a
    // building always outscores open ground and the raw top five land inside one
    // cluster - five highlights that are really one suggestion, with genuinely
    // different options never shown at all.
    //
    // Separation is the fix, and it costs ranking fidelity on purpose: #2 is no
    // longer the second-best tile, it is the best tile that is somewhere else.
    public class SpreadTests
    {
        private static Snapshot Board(int size)
        {
            var board = new Snapshot { Width = size, Height = size, Buildable = new bool[size * size] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = 1, Range = 2, Categories = new[] { 7 } };
            return board;
        }

        private static void Place(Snapshot board, int x, int y)
        {
            board.Buildings.Add(new Piece { X = x, Y = y, Tag = 2, Range = 2, Categories = new[] { 7 } });
            board.Buildable[y * board.Width + x] = false;
        }

        [Fact]
        public void WithoutSeparationTheTopPicksCrowdTogether()
        {
            var board = Board(21);
            Place(board, 10, 10);

            var raw = Plan.Best(board, 5, 0);

            // They all hug the single building, which is the behaviour being fixed.
            int touching = 0;
            foreach (var candidate in raw)
                if (Math.Abs(candidate.X - 10) <= 2 && Math.Abs(candidate.Y - 10) <= 2) touching++;
            Assert.Equal(5, touching);
        }

        [Fact]
        public void SeparationSpreadsThePicksApart()
        {
            var board = Board(21);
            Place(board, 10, 10);

            var spread = Plan.Best(board, 5, 3);
            Assert.Equal(5, spread.Count);

            for (int i = 0; i < spread.Count; i++)
                for (int j = i + 1; j < spread.Count; j++)
                {
                    int dx = Math.Abs(spread[i].X - spread[j].X);
                    int dy = Math.Abs(spread[i].Y - spread[j].Y);
                    Assert.True(dx > 3 || dy > 3,
                        $"({spread[i].X},{spread[i].Y}) and ({spread[j].X},{spread[j].Y}) are too close");
                }
        }

        [Fact]
        public void TheBestTileIsStillFirst()
        {
            var board = Board(21);
            Place(board, 10, 10);

            var raw = Plan.Best(board, 5, 0);
            var spread = Plan.Best(board, 5, 3);

            // Separation reorders what comes after the winner; it must not replace it.
            Assert.Equal(raw[0].X, spread[0].X);
            Assert.Equal(raw[0].Y, spread[0].Y);
        }

        [Fact]
        public void ACrampedBoardStillFillsTheShortlist()
        {
            // Nine tiles, asking for five that are four apart - impossible. Showing
            // two suggestions because the rest were crowded out would look like a bug,
            // so the constraint is relaxed rather than the list left short.
            var board = Board(3);
            var spread = Plan.Best(board, 5, 4);
            Assert.Equal(5, spread.Count);
        }

        [Fact]
        public void SeparationNeverRepeatsATile()
        {
            var board = Board(3);
            var spread = Plan.Best(board, 9, 4);

            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var candidate in spread)
                Assert.True(seen.Add((candidate.X, candidate.Y)),
                    $"({candidate.X},{candidate.Y}) appears twice");
        }

        [Fact]
        public void ABuildingBesideItsOwnKindScoresWorseThanBesideAStranger()
        {
            // The reported symptom: identical buildings share every category, so the
            // affinity term ranked them highest. The game's own text keeps excluding
            // them - "Does not affect other [Fisher]" - so they must not.
            var twin = Board(15);
            twin.Buildings.Add(new Piece { X = 7, Y = 7, Tag = 1, Range = 2, Categories = new[] { 7 } });
            twin.Buildable[7 * 15 + 7] = false;

            var stranger = Board(15);
            stranger.Buildings.Add(new Piece { X = 7, Y = 7, Tag = 2, Range = 2, Categories = new[] { 7 } });
            stranger.Buildable[7 * 15 + 7] = false;

            var besideTwin = Value.Score(twin, 7, 8);
            var besideStranger = Value.Score(stranger, 7, 8);

            Assert.Equal(1, besideTwin.SameTag);
            Assert.Equal(0, besideTwin.Shared);
            Assert.Equal(0, besideStranger.SameTag);
            Assert.Equal(1, besideStranger.Shared);
            Assert.True(besideTwin.Total < besideStranger.Total);
        }
    }
}
