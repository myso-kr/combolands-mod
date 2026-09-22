using System.Reflection;
using HarmonyLib;
using HarmonyInstance = HarmonyLib.Harmony;

namespace Combolands.Mod.Cheat
{
    // Steam achievements and cheating do not belong in the same run.
    //
    // The moment any cheat in this mod is used, achievement submission stops for the
    // rest of the session. That is the default rather than an option a careful
    // player has to find, because the failure is one-way: an achievement unlocked by
    // a cheat cannot be taken back, and it lands on an account the player keeps.
    //
    // AchievementsHandler.Achieve(Achievement) is the one place the game calls out to
    // the platform, so one prefix covers every achievement it has.
    internal static class Integrity
    {
        private static bool _patched;

        internal static bool Cheated { get; private set; }

        internal static bool Blocking
        {
            get { return Cheated && Config.BlockAchievements; }
        }

        // Called by every cheat before it touches game state.
        internal static void MarkCheated()
        {
            if (Cheated) return;
            Cheated = true;
            Log.Info("cheat", Config.BlockAchievements
                ? "a cheat was used - achievements are blocked for this session"
                : "a cheat was used - achievements are NOT blocked (BlockAchievements is off)");
        }

        internal static void Apply(HarmonyInstance harmony)
        {
            if (_patched) return;

            var handler = Anchors.Type("External.AchievementsHandler")
                       ?? Anchors.Type("AchievementsHandler");
            var achieve = Anchors.MethodByName(handler, "Achieve", 1);
            if (achieve == null)
            {
                Log.Error("cheat", "AchievementsHandler.Achieve not found - achievements CANNOT be blocked");
                return;
            }

            harmony.Patch(achieve, prefix: new HarmonyMethod(
                typeof(Integrity).GetMethod(nameof(Gate), BindingFlags.NonPublic | BindingFlags.Static)));
            _patched = true;
            Log.Info("cheat", "achievement gate installed");
        }

        // Returning false skips the original, so the platform is never told.
        //
        // No parameters: Achieve takes an enum from the game assembly, and binding it
        // here would mean either referencing Assembly-CSharp or hoping Harmony boxes
        // it. Neither is worth knowing which achievement was suppressed.
        private static bool Gate()
        {
            return !Blocking;
        }
    }
}
