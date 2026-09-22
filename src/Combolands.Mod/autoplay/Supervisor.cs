using System.Collections.Generic;
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
                if (_pace.Known) state += "   [" + _pace + "]";
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

        internal static void Forget()
        {
            Refused.Clear();
            Hopeless.Clear();
            _refusedFor = null;
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
        //
        // Screens come first because they BLOCK: a milestone summary waiting for a
        // click, a pack to pick from, a shop standing open. There is no point ranking
        // tiles while a modal is up, and a loop that only knew how to place buildings
        // sat in front of each of those forever.
        internal static bool Step()
        {
            if (Screens.Handle()) { _last = Screens.Last; return true; }

            // A targeting effect nobody is driving blocks everything behind it.
            if (Items.EscapeTargeting()) { _last = Items.Last; return true; }

            var piece = State.PieceBeingPlaced();

            if (piece != null) return PlaceHeld(piece);

            // Spending a blueprint puts one in hand, so it is tried before drawing a
            // new card - a shelf that stays full stops the run taking anything else.
            if (Items.UseOne(_pace)) { _last = Items.Last; return true; }

            if (Config.AutoplayPicks) return PickAnOffer();

            _last = "nothing to do here";
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

        // Tiles this exact piece has been refused, and building types that turned out
        // to have nowhere to go. Both exist because a refusal used to produce nothing
        // but another identical attempt next tick.
        private static readonly HashSet<long> Refused = new HashSet<long>();
        private static readonly HashSet<int> Hopeless = new HashSet<int>();
        private static object _refusedFor;
        private static Pace _pace = Pace.Unknown;

        private static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        private static bool PlaceHeld(object piece)
        {
            // Before anything is read: the ghost follows the cursor, and the cursor
            // may be outside the window. Off the map it cannot even be measured.
            if (!Exec.OnMap(piece) && !Exec.BringOnMap(piece))
            {
                _last = "the held building is off the map and will not come back";
                return false;
            }

            if (!ReferenceEquals(piece, _refusedFor))
            {
                Refused.Clear();
                _refusedFor = piece;
            }

            var board = Board.Read(piece, null);
            if (board == null) { _last = "cannot read the board"; return false; }
            _pace = board.Pace;

            // Separation is for a human choosing between options. A machine wants the
            // single best tile, so it is asked for one with none.
            foreach (var candidate in Plan.Best(board, 40, 0))
            {
                if (Refused.Contains(Key(candidate.X, candidate.Y))) continue;
                if (!Board.CanBuildAt(piece, candidate.X, candidate.Y)) continue;

                switch (Exec.PlaceAt(candidate.X, candidate.Y))
                {
                    case Exec.Result.Placed:
                        Hopeless.Clear();
                        _last = string.Format("placed at ({0},{1})  {2} pts of targets",
                            candidate.X, candidate.Y, candidate.Score.TargetScore);
                        return true;

                    case Exec.Result.Refused:
                        // Our range shape does not know about Crane or Stable, so the
                        // game can legitimately refuse a tile we ranked. Remember it
                        // rather than offering it again next tick, forever.
                        Refused.Add(Key(candidate.X, candidate.Y));
                        continue;

                    default:
                        _last = "could not reach the placement path";
                        return false;
                }
            }

            // The ranked list is exhausted. Ask the game about the rest of the board,
            // properly - full rule, caches fresh - and take the first tile it accepts.
            if (Fallback(piece, board)) return true;

            // Genuinely nowhere. Put the card back, the way a right click would, and
            // remember not to pick this building again until something has changed.
            Hopeless.Add(board.Candidate.Tag);
            if (Exec.CancelPlacing())
            {
                _last = "nowhere legal for it - put it back";
                return true;
            }

            _last = "nowhere legal to place, and it will not go back";
            return false;
        }

        private static bool Fallback(object piece, Snapshot board)
        {
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    if (!board.IsBuildable(x, y)) continue;
                    if (Refused.Contains(Key(x, y))) continue;
                    if (!Board.CanBuildAtStrict(piece, x, y)) continue;

                    if (Exec.PlaceAt(x, y) != Exec.Result.Placed)
                    {
                        Refused.Add(Key(x, y));
                        continue;
                    }

                    Hopeless.Clear();
                    _last = string.Format("placed at ({0},{1}) - the ranked tiles were all refused", x, y);
                    return true;
                }
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
                if (Hopeless.Contains(offers[i].Tag)) continue;

                var board = Board.ReadFor(offers[i], null);
                if (board == null) continue;

                var picks = Plan.Best(board, 1, 0);
                if (picks.Count == 0) continue;
                if (picks[0].Score.Total <= bestScore) continue;

                bestScore = picks[0].Score.Total;
                bestOffer = i;
            }

            if (bestOffer < 0)
            {
                // Every offer is either unplaceable or already known hopeless. Let
                // them back in: the board changes under us, and a building that had
                // nowhere to go a moment ago may have somewhere now.
                if (Hopeless.Count > 0) { Hopeless.Clear(); _last = "retrying the offers"; return false; }
                _last = "no offer has anywhere to go";
                return false;
            }

            var button = Offers.ButtonAt(bestOffer);
            if (button == null) { _last = "could not reach the choice button"; return false; }

            if (!Exec.Choose(button)) { _last = "the choice did not take"; return false; }

            _last = "picked " + offers[bestOffer].Name;
            return true;
        }

    }
}
