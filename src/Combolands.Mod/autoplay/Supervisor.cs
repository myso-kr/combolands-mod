using UnityEngine;

namespace Combolands.Mod.Autoplay
{
    // The loop. It decides WHEN to act; Value and Plan decide WHAT, and Exec is the
    // only thing that touches the game.
    //
    // Two speeds, because they carry different risk:
    //
    //   Step (a keypress)    one action, then stop. The player is still driving
    //   Run (a toggle)       keeps going while there is something obvious to do
    //
    // Both refuse to act unless the game says it is ready, and neither overrules a
    // placement rule - Exec asks the game whether the tile is legal and gives up if
    // the answer is no. Autoplay cannot put a building somewhere the player could
    // not have.
    //
    // What it does NOT do: shops, packs, quests, council votes, milestones. It plays
    // the placement phase and stops at the edge of it. Saying that plainly is better
    // than a loop that wanders into a screen it does not understand.
    internal static class Supervisor
    {
        internal static bool Running;

        private static int _cooldown;
        private static string _last = "idle";

        // Unity stops calling Update when the window loses focus unless
        // Application.runInBackground is set, and Combolands exposes that as a
        // setting a player may well have turned off. In windowed mode, moving the
        // cursor away and clicking something else is enough - the loop simply stops
        // mid-run, which looks like a bug in the loop rather than a paused game.
        //
        // While autoplay is running it is forced on and put back afterwards. Forced
        // rather than asked, because a loop whose whole point is to run unattended
        // cannot require the window to stay in front.
        private static bool _restoreBackground;
        private static bool _previousBackground;

        internal static string Status
        {
            get
            {
                var state = (Running ? "running - " : "stopped - ") + _last;
                if (Log.HasFailed("autoplay.tick"))
                    state += string.Format("   [{0} error(s), see MelonLoader/Latest.log]",
                        Log.FailureCount("autoplay.tick"));
                return state;
            }
        }

        internal static void Toggle()
        {
            if (Running) { Stop(); return; }

            Running = true;
            _cooldown = 0;
            KeepRunningUnfocused();
            Log.Info("autoplay", "running");
        }

        internal static void Stop()
        {
            if (!Running) return;
            Running = false;
            RestoreBackground();
            Log.Info("autoplay", "stopped");
        }

        private static void KeepRunningUnfocused()
        {
            if (_restoreBackground) return;
            if (Application.runInBackground) return;      // already what we need

            _previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            _restoreBackground = true;
            Log.Info("autoplay", "forcing runInBackground so the loop survives losing focus");
        }

        private static void RestoreBackground()
        {
            if (!_restoreBackground) return;
            Application.runInBackground = _previousBackground;
            _restoreBackground = false;
            Log.Info("autoplay", "runInBackground restored");
        }

        // One action, now. This is what the step key does, and what Run calls.
        internal static bool Step()
        {
            var piece = State.PieceBeingPlaced();

            if (piece != null) return PlaceHeld(piece);
            if (Config.AutoplayPicks) return PickAnOffer();

            _last = "nothing in hand, and picking is off";
            return false;
        }

        internal static void Tick()
        {
            if (!Running) return;

            if (_cooldown > 0) { _cooldown--; return; }
            _cooldown = Config.AutoplayDelay;

            // Nothing to do is not a failure. The player may be in a shop, reading the
            // council room, or between milestones - all places this loop has no
            // business in.
            Step();
        }

        // --- the two actions ------------------------------------------------------

        private static bool PlaceHeld(object piece)
        {
            // Before anything is read: the ghost follows the cursor, and the cursor
            // may be outside the window. Off the map it cannot even be measured.
            if (!Exec.OnMap(piece) && !Exec.BringOnMap(piece))
            {
                _last = "the held building is off the map and will not come back";
                return false;
            }

            // The whole board, never the viewport. That limit exists so a HUMAN can
            // see the highlight being offered; a loop does not need to look at a tile
            // to put a building on it, and clamping it to the camera would make the
            // best move depend on where the view happened to be scrolled.
            var board = Board.Read(piece, null);
            if (board == null) { _last = "cannot read the board"; return false; }

            // Separation is for a human choosing between options. A machine wants the
            // single best tile, so it is asked for one with none.
            var best = Plan.Best(board, 12, 0);
            for (int i = 0; i < best.Count; i++)
            {
                if (!Board.CanBuildAt(piece, best[i].X, best[i].Y)) continue;
                if (Exec.PlaceAt(best[i].X, best[i].Y))
                {
                    _last = string.Format("placed at ({0},{1})  x{2:0.#} targets",
                        best[i].X, best[i].Y, best[i].Score.TargetHits);
                    return true;
                }
            }

            _last = "nowhere legal to place";
            return false;
        }

        private static bool PickAnOffer()
        {
            if (!Exec.CanUseUi()) { _last = "waiting - the game is not accepting input"; return false; }

            var offers = Offers.Current();
            if (offers.Count == 0) { _last = "no buildings on offer"; return false; }

            int bestOffer = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < offers.Count; i++)
            {
                var board = Board.ReadFor(offers[i], null);
                if (board == null) continue;

                var picks = Plan.Best(board, 1, 0);
                if (picks.Count == 0) continue;
                if (picks[0].Score.Total <= bestScore) continue;

                bestScore = picks[0].Score.Total;
                bestOffer = i;
            }

            if (bestOffer < 0) { _last = "no offer has anywhere to go"; return false; }

            var button = Offers.ButtonAt(bestOffer);
            if (button == null) { _last = "could not reach the choice button"; return false; }

            if (!Exec.Choose(button)) { _last = "the choice did not take"; return false; }

            _last = "picked " + offers[bestOffer].Name;
            return true;
        }

    }
}
