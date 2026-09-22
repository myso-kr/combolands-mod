namespace Combolands.Mod.Cheat
{
    // Money, score, and the four spendable counters.
    //
    // Everything goes through ScoreController's own Change* methods rather than
    // writing the backing fields. Those methods are what refresh MoneyPanel, move the
    // milestone progress bar and raise MoneyChanged - a field write would leave the
    // number right and the UI wrong, which reads as a broken mod rather than a cheat.
    //
    // withTrigger is false throughout. Passing true would fire the game's
    // on-gold-earned trigger chain, so a "+100 gold" button would silently score
    // points through whatever buildings happened to be on the map.
    internal static class Economy
    {
        private const string Score = "GameState.ScoreController";

        internal static bool Available { get { return Singletons.Exists(Score); } }

        internal static int Money { get { return Singletons.Read(Score, "Money", 0); } }
        internal static long Points { get { return Singletons.Read(Score, "Score", 0L); } }
        internal static int Rerolls { get { return Singletons.Read(Score, "Rerolls", 0); } }
        internal static int Removes { get { return Singletons.Read(Score, "Removes", 0); } }
        internal static int Dismisses { get { return Singletons.Read(Score, "Dismisses", 0); } }
        internal static int Rewinds { get { return Singletons.Read(Score, "Rewinds", 0); } }
        internal static float Enchant { get { return Singletons.Read(Score, "Enchant", 0f); } }

        internal static void AddMoney(int amount)
        {
            Act(() => Singletons.Call(Score, "ChangeMoney", amount, null, false));
        }

        internal static void AddScore(long amount)
        {
            Act(() => Singletons.Call(Score, "ChangeScore", amount));
        }

        internal static void AddRerolls(int amount)
        {
            Act(() => Singletons.Call(Score, "ChangeRerolls", amount, null, false));
        }

        internal static void AddRemoves(int amount)
        {
            Act(() => Singletons.Call(Score, "ChangeRemoves", amount, null, false));
        }

        internal static void AddDismisses(int amount)
        {
            Act(() => Singletons.Call(Score, "ChangeDismisses", amount));
        }

        internal static void AddRewinds(int amount)
        {
            Act(() => Singletons.Call(Score, "ChangeRewinds", amount));
        }

        internal static void AddEnchant(float amount)
        {
            Act(() => Singletons.Call(Score, "ChangeEnchantment", amount));
        }

        private static void Act(System.Func<bool> body)
        {
            Integrity.MarkCheated();
            Log.Guard("cheat.economy", () => body());
        }
    }
}
