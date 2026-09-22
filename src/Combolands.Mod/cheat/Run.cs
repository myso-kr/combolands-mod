namespace Combolands.Mod.Cheat
{
    // Weeks, the score target, the milestone, and the shop - the things that decide
    // how long a run lasts rather than what is in it.
    //
    // DebugChangeWeeks and DebugSetTarget are the game's own, left public in the
    // retail build. Using those rather than writing WeeksAllowed and ScoreRequired
    // means the milestone bar and the week counter stay in step by construction.
    internal static class Run
    {
        private const string Game = "GameState.GameController";
        private const string Milestones = "GameState.MilestoneManager";
        private const string Shop = "GameState.ShopManager";
        private const string Cheats = "Interaction.CheatsHandler";

        internal static bool Available { get { return Singletons.Exists(Game); } }

        internal static int WeeksRemaining { get { return Singletons.Read(Game, "WeeksRemaining", 0); } }
        internal static long ScoreRequired { get { return Singletons.Read(Game, "ScoreRequired", 0L); } }
        internal static int MilestoneIndex { get { return Singletons.Read(Milestones, "CurrentMilestoneIndex", -1); } }

        internal static void AddWeek()
        {
            Act(() => Singletons.Call(Game, "AddExtraWeek"));
        }

        internal static void SetWeeks(int weeks)
        {
            Act(() => Singletons.Call(Game, "DebugChangeWeeks", weeks));
        }

        internal static void SetTarget(int target)
        {
            Act(() => Singletons.Call(Game, "DebugSetTarget", target));
        }

        internal static void EndMilestone()
        {
            Act(() => Singletons.Call(Milestones, "EndCurrentMilestoneEarly"));
        }

        internal static void JumpToMilestone(int index)
        {
            Act(() =>
            {
                if (!Singletons.Call(Milestones, "DebugSetCurrentMilestone", index)) return false;
                return Singletons.Call(Milestones, "EndCurrentMilestoneEarly");
            });
        }

        internal static void OpenShop()
        {
            Act(() => Singletons.Call(Shop, "DebugShowShop"));
        }

        // Not a cheat by itself - it skips the scoring animation, which is also what
        // makes an unattended autoplay run finish this century. It does not mark the
        // session, and M6 reuses it.
        internal static bool SpeedUpScoring
        {
            get { return Singletons.Read(Cheats, "SpeedUpScoring", false); }
            set { Log.Guard("cheat.speed", () => Singletons.Write(Cheats, "SpeedUpScoring", value)); }
        }

        internal static bool SpeedAvailable { get { return Singletons.Exists(Cheats); } }

        private static void Act(System.Func<bool> body)
        {
            Integrity.MarkCheated();
            Log.Guard("cheat.run", () => body());
        }
    }
}
