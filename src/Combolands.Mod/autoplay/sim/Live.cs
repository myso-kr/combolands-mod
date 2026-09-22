using System;
using System.Collections.Generic;
using System.IO;

namespace Combolands.Mod.Autoplay.Sim
{
    // The simulator, attached to a running game.
    //
    // Two files make the difference between the helper guessing and the helper
    // computing: the rule set dumped out of this game, and the policy fitted against
    // it. Both are optional. Without them everything still works on the old
    // proximity valuation - worse, but working - because a player who has never run
    // the dump should not find the helper dead.
    //
    // Whether the simulator is in use is worth saying out loud, and the overlay does.
    // "the helper is guessing" and "the helper is computing" produce different advice
    // and a player is entitled to know which they are looking at.
    internal static class Live
    {
        private static Rules _rules;
        private static Policy _policy;
        private static bool _tried;
        private static string _why = "not loaded yet";

        internal static Rules Rules { get { Load(); return _rules; } }
        internal static Policy Policy { get { Load(); return _policy ?? Sim.Policy.Default(); } }

        internal static bool Ready { get { Load(); return _rules != null; } }
        internal static string Why { get { Load(); return _why; } }

        internal static void Forget()
        {
            _tried = false;
            _rules = null;
            _policy = null;
        }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;

            var path = Config.RulesFile;
            if (!File.Exists(path))
            {
                _why = "no rule set at " + path + " - set DumpRules = true and start a run";
                Log.Info("sim", _why + "; the helper will use the old proximity valuation");
                return;
            }

            try
            {
                _rules = Sim.Rules.Parse(File.ReadAllText(path));
            }
            catch (Exception error)
            {
                // A half-loaded rule set would produce a valuation that is confidently
                // wrong, which is worse than one that is vaguely right. Refuse it.
                _why = "could not read " + path + ": " + error.Message;
                Log.Error("sim", _why);
                return;
            }

            _why = string.Format("{0} buildings, {1} exact, {2} approximate",
                _rules.Count, _rules.CountOf(Fidelity.Exact), _rules.CountOf(Fidelity.Approximate));
            Log.Info("sim", "rule set loaded - " + _why);

            var weights = Config.PolicyFile;
            if (!File.Exists(weights))
            {
                Log.Info("sim", "no fitted policy at " + weights + " - using the built-in weights");
                return;
            }

            try
            {
                _policy = Sim.Policy.FromJson(File.ReadAllText(weights));
                Log.Info("sim", "policy loaded - " + _policy);
            }
            catch (Exception error)
            {
                Log.Warn("sim", "could not read " + weights + ": " + error.Message
                    + " - using the built-in weights");
            }
        }

        // --- the live board, as data --------------------------------------------------

        // Built fresh on each refresh rather than kept and patched. The helper already
        // refreshes only when the game says the board changed
        // (`BuildingExtensions.ResetCaches`), so this runs about as often as a
        // building is placed - and a stale simulated board would be the worst of both
        // worlds: confident and wrong.
        internal static Map Build(int width, int height,
                                  Func<int, int, int> tileType,
                                  Func<int, int, bool> blocked,
                                  IEnumerable<Placed> buildings)
        {
            var map = new Map(width, height);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    map.SetTile(x, y, tileType(x, y), blocked(x, y));

            foreach (var building in buildings)
            {
                if (!map.Inside(building.X, building.Y)) continue;
                if (!map.IsEmpty(building.X, building.Y)) continue;

                map.Place(building.Tag, building.X, building.Y, building.Multiplier);
            }

            return map;
        }

        internal struct Placed
        {
            public int Tag;
            public int X;
            public int Y;
            public float Multiplier;
        }
    }
}
