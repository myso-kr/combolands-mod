using Combolands.Mod.Autoplay;
using Xunit;

namespace Combolands.Mod.Tests
{
    // A milestone is "reach ScoreRequired within WeeksRemaining weeks", and which of
    // those is scarce decides what a good placement looks like. With weeks to spare,
    // a tile that sets up future synergies beats one that pays now; with two weeks
    // left and a target to hit, an engine that pays off in four is worth nothing.
    public class PaceTests
    {
        [Fact]
        public void MeetingTheTargetRemovesAllUrgency()
        {
            var pace = Pace.From(score: 1200, required: 1000, weeksLeft: 3, scoredLastWeek: 400);

            Assert.Equal(0f, pace.Pressure);
            Assert.True(pace.ScoreNowWeight < 1f, "nothing left to rush for");
            Assert.True(pace.FutureWeight > 1f, "so build the engine");
        }

        [Fact]
        public void ExactlyOnPaceChangesNothing()
        {
            // 600 to go, 3 weeks, scoring 200 a week: precisely enough.
            var pace = Pace.From(score: 400, required: 1000, weeksLeft: 3, scoredLastWeek: 200);

            Assert.Equal(1f, pace.Pressure, 2);
            Assert.Equal(1f, pace.ScoreNowWeight, 2);
            Assert.Equal(1f, pace.FutureWeight, 2);
        }

        [Fact]
        public void BehindPaceFavoursScoringNow()
        {
            // 900 to go, 3 weeks, scoring 100 a week: three times the rate needed.
            var behind = Pace.From(score: 100, required: 1000, weeksLeft: 3, scoredLastWeek: 100);
            var ahead = Pace.From(score: 900, required: 1000, weeksLeft: 5, scoredLastWeek: 400);

            Assert.True(behind.Pressure > 2f);
            Assert.True(behind.ScoreNowWeight > ahead.ScoreNowWeight);
            Assert.True(behind.FutureWeight < ahead.FutureWeight);
        }

        [Fact]
        public void OutOfWeeksWithPointsOwedIsTheCeilingNotInfinity()
        {
            var pace = Pace.From(score: 100, required: 1000, weeksLeft: 0, scoredLastWeek: 50);

            Assert.True(pace.Pressure > 3f);
            Assert.True(float.IsFinite(pace.Pressure), "infinity would make every weight meaningless");
            Assert.True(pace.ScoreNowWeight <= 2f);
            Assert.True(pace.FutureWeight >= 0.2f);
        }

        [Fact]
        public void AZeroRateDoesNotDivideByZero()
        {
            var pace = Pace.From(score: 0, required: 1000, weeksLeft: 4, scoredLastWeek: 0);

            Assert.True(float.IsFinite(pace.Pressure));
            Assert.True(pace.Pressure > 1f, "scoring nothing while owing everything is behind");
        }

        [Fact]
        public void UnknownPaceBehavesLikePlayingNormally()
        {
            var pace = Pace.Unknown;

            Assert.False(pace.Known);
            Assert.Equal(1f, pace.ScoreNowWeight);
            Assert.Equal(1f, pace.FutureWeight);
        }

        [Fact]
        public void PressureActuallyMovesTheRanking()
        {
            // One tile pays points; another is open ground with room to grow. Which
            // wins must depend on how much time is left.
            var relaxed = BoardWith(Pace.From(500, 1000, 8, 400));
            var desperate = BoardWith(Pace.From(100, 1000, 1, 100));

            var paying = Value.Score(relaxed, 5, 6).Total - Value.Score(relaxed, 18, 18).Total;
            var payingUnderPressure = Value.Score(desperate, 5, 6).Total - Value.Score(desperate, 18, 18).Total;

            Assert.True(payingUnderPressure > paying,
                "the scoring tile should pull further ahead when time is short");
        }

        private static Snapshot BoardWith(Pace pace)
        {
            var board = new Snapshot { Width = 21, Height = 21, Buildable = new bool[21 * 21] };
            for (int i = 0; i < board.Buildable.Length; i++) board.Buildable[i] = true;
            board.Candidate = new Piece { Tag = 1, Range = 2, Categories = new[] { 1 } };
            board.TargetTagScores[50] = 30;
            board.MaxTargetScore = 30;
            board.Pace = pace;

            board.Buildings.Add(new Piece { X = 5, Y = 5, Tag = 50, Range = 0, Categories = new[] { 9 } });
            board.Buildable[5 * 21 + 5] = false;
            return board;
        }
    }
}
