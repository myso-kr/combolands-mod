using System;

namespace Combolands.Mod.Autoplay
{
    // Whether this draw is worth keeping.
    //
    // Three training runs said the same thing about the placement weights: they do
    // not move the clear rate, and all three pointed at the same explanation - which
    // three cards the bar offers dominates where they go. A reroll is the only lever
    // on that, and `ConsumablesPanel.PressRerollButton` spends one, refills the bar
    // through `GameController.ShowNewBuildingChoices`, and does NOT end the turn. So
    // it is free in the only currency a milestone is short of.
    //
    // Which makes an unspent reroll at the end of a run a placement that was never
    // improved, and hoarding them the one clearly wrong policy.
    //
    // Measured rather than assumed: over 150 simulated milestones, rerolling below
    // seven tenths of a normal draw cleared one more and finished 1.9 points of the
    // target closer on average than never rerolling. See docs/SIMULATION.md.
    internal static class Rerolls
    {
        private const string Score = "GameState.ScoreController";
        private const string Consumables = "UI.ConsumablesPanel";

        internal static string Last = "";

        // What a draw is normally worth on this board, learned as the run goes.
        //
        // A fixed threshold cannot work: three hundred points is a good week on the
        // first milestone and a wasted one on the sixth, and the game never says
        // which pool it is drawing from. A running mean of the draws actually played
        // needs neither number and adapts to both.
        private static double _total;
        private static int _draws;

        internal static void Forget()
        {
            _total = 0;
            _draws = 0;
        }

        internal static int Held { get { return Singletons.Read(Score, "Rerolls", 0); } }

        // Called with the best placement value this draw offers - the same number
        // PickAnOffer ranks on, because a draw is worth whatever the best thing you
        // can do with it is worth.
        //
        // Returns true when a reroll was spent, which means the caller should stop:
        // the choice bar it was looking at no longer exists.
        internal static bool Consider(float bestValue)
        {
            if (!Config.AutoplayRerolls) return false;

            // The first draw of a run has nothing to be compared against. Keeping it
            // is the only honest answer, and it is also what sets the benchmark.
            if (_draws == 0) { Remember(bestValue); return false; }

            var normal = _total / _draws;
            if (normal <= 0) { Remember(bestValue); return false; }

            if (bestValue >= normal * Config.AutoplayRerollBelow) { Remember(bestValue); return false; }

            var held = Held;
            if (held <= 0) { Remember(bestValue); return false; }

            if (!Spend())
            {
                // The button refused. Remember the draw anyway - it is the draw we
                // are going to play, whatever we wanted.
                Remember(bestValue);
                return false;
            }

            Last = string.Format("rerolled - this draw was worth {0:N0} against a usual {1:N0}, {2} left",
                                 bestValue, normal, held - 1);
            Log.Info("autoplay", Last);
            return true;
        }

        // Deliberately after any reroll: the average has to describe the draws
        // actually played, not the ones thrown away. Counting a rejected draw would
        // drag the benchmark down and make the next one look good by comparison,
        // which is how a threshold like this quietly stops firing.
        private static void Remember(double value)
        {
            _total += value;
            _draws++;
        }

        private static bool Spend()
        {
            // The panel's own precondition. It checks `CanInteractWithUi` and does
            // nothing when the answer is no - silently, which would look from here
            // like a reroll that was spent and changed nothing.
            if (!Exec.CanUseUi()) return false;

            var panel = Singletons.Get(Consumables);
            if (panel == null) return false;

            bool ambiguous;
            var press = Reflect.MethodByName(panel.GetType(), "PressRerollButton", 0, out ambiguous);
            if (press == null)
            {
                Log.Warn("autoplay", "ConsumablesPanel.PressRerollButton is gone - "
                    + "rerolling is off. See docs/ANCHORS.md row P23.");
                return false;
            }

            press.Invoke(panel, null);
            return true;
        }
    }
}
