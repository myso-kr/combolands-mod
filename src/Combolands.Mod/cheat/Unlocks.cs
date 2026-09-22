namespace Combolands.Mod.Cheat
{
    // Guild unlocks and levels.
    //
    // These two are different in kind from everything else in the widget: they write
    // to the PERSISTENT unlock save, not to the current run. Unlocking all guilds
    // cannot be undone by abandoning the run, and the game's own debug menu warns
    // about it - "Not recommended for first time players" - which is the game telling
    // you it spoils its own progression.
    //
    // So the widget asks twice. Cheap to do, and the mistake is otherwise permanent.
    internal static class Unlocks
    {
        private const string State = "Progression.UnlockStateController";

        internal static bool Available { get { return Singletons.Exists(State); } }

        internal static void UnlockAllGuilds()
        {
            Act("DebugUnlockAllGuilds");
        }

        internal static void MaxLevelAllGuilds()
        {
            Act("DebugLevelUpAllGuilds");
        }

        private static void Act(string method)
        {
            Integrity.MarkCheated();
            Log.Guard("cheat.unlocks", () =>
            {
                if (Singletons.Call(State, method))
                    Log.Info("cheat", method + " - this changed the permanent unlock save");
            });
        }
    }
}
