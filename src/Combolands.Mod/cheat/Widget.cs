using System;
using System.Reflection;
using Combolands.Mod.Autoplay;
using HarmonyLib;
using UnityEngine;
using HarmonyInstance = HarmonyLib.Harmony;

namespace Combolands.Mod.Cheat
{
    // The panel: layout, input, and the confirmation state for the two cheats that
    // cannot be undone. It reads game state and calls the modules; it never writes to
    // the game itself, which is what keeps "what does this button do" answerable from
    // one file.
    internal static class Widget
    {
        private const int WindowId = 0xC0110A;

        private static bool _open;
        private static Rect _rect = new Rect(24f, 24f, 340f, 0f);
        private static Vector2 _scroll;
        private static bool _confirmUnlockAll;
        private static bool _confirmMaxLevel;
        private static GUIStyle _note;

        internal static bool Open { get { return _open; } }

        // --- input ---------------------------------------------------------------

        internal static void Update()
        {
            if (!Input.GetKeyDown(Config.WidgetKey)) return;
            _open = !_open;
            _confirmUnlockAll = _confirmMaxLevel = false;
        }

        // The game asks UiUtils.IsPointerOverUIObject() before acting on a click, and
        // that only knows about uGUI - an IMGUI window is invisible to it. Without
        // this, every click on the panel also lands on the map behind it and places a
        // building. Telling the game the pointer IS over UI is not a trick; while the
        // panel is under the cursor, it is.
        internal static void Apply(HarmonyInstance harmony)
        {
            var utils = Anchors.Type("Library.Utils.UiUtils");
            var probe = Anchors.Method(utils, "IsPointerOverUIObject");
            if (probe == null)
            {
                Log.Warn("cheat", "UiUtils.IsPointerOverUIObject not found - clicks on the panel will reach the map");
                return;
            }

            harmony.Patch(probe, postfix: new HarmonyMethod(
                typeof(Widget).GetMethod(nameof(AlsoOverPanel), BindingFlags.NonPublic | BindingFlags.Static)));
            Log.Info("cheat", "panel input guard installed");
        }

        private static void AlsoOverPanel(ref bool __result)
        {
            if (__result || !_open) return;
            // Screen space has y up, GUI space has y down, and _rect is in the scaled
            // space GUI.matrix sets up - so the cursor has to be divided by the same
            // scale to be comparable.
            var scale = Config.WidgetScale;
            var point = new Vector2(Input.mousePosition.x / scale,
                                    (Screen.height - Input.mousePosition.y) / scale);
            if (_rect.Contains(point)) __result = true;
        }

        // --- drawing -------------------------------------------------------------

        internal static void Draw()
        {
            if (!_open) return;
            if (_note == null)
            {
                _note = new GUIStyle(GUI.skin.label);
                _note.wordWrap = true;
                _note.fontSize = 11;
            }
            // IMGUI has no DPI awareness; on a 1440p ultrawide the default skin is
            // about a tenth of the screen wide. Scaling the matrix keeps every layout
            // number below in one coordinate system.
            var previous = GUI.matrix;
            var scale = Config.WidgetScale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            _rect = GUILayout.Window(WindowId, _rect, Body, "Combolands Mod  —  " + Config.WidgetKey);
            GUI.matrix = previous;
        }

        private static void Body(int id)
        {
            Status();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(420f));

            Economy();
            RunControls();
            Building();
            Helper();
            Autoplay();
            Pace();
            PermanentUnlocks();

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private static void Economy()
        {
            if (!Header("Economy", Cheat.Economy.Available)) return;

            Note(string.Format("gold {0}     score {1:N0}     enchant {2:0.##}",
                Cheat.Economy.Money, Cheat.Economy.Points, Cheat.Economy.Enchant));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+100 gold")) Cheat.Economy.AddMoney(100);
            if (GUILayout.Button("+1,000")) Cheat.Economy.AddMoney(1000);
            if (GUILayout.Button("+10,000")) Cheat.Economy.AddMoney(10000);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+10k score")) Cheat.Economy.AddScore(10000L);
            if (GUILayout.Button("+1M score")) Cheat.Economy.AddScore(1000000L);
            if (GUILayout.Button("+1 enchant")) Cheat.Economy.AddEnchant(1f);
            GUILayout.EndHorizontal();

            Note(string.Format("reroll {0}    remove {1}    dismiss {2}    rewind {3}",
                Cheat.Economy.Rerolls, Cheat.Economy.Removes,
                Cheat.Economy.Dismisses, Cheat.Economy.Rewinds));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+5 reroll")) Cheat.Economy.AddRerolls(5);
            if (GUILayout.Button("+5 remove")) Cheat.Economy.AddRemoves(5);
            if (GUILayout.Button("+5 dismiss")) Cheat.Economy.AddDismisses(5);
            if (GUILayout.Button("+5 rewind")) Cheat.Economy.AddRewinds(5);
            GUILayout.EndHorizontal();
        }

        private static void RunControls()
        {
            if (!Header("Run", Run.Available)) return;

            Note(string.Format("milestone {0}     weeks left {1}     target {2:N0}",
                Run.MilestoneIndex, Run.WeeksRemaining, Run.ScoreRequired));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1 week")) Run.AddWeek();
            if (GUILayout.Button("100 weeks")) Run.SetWeeks(100);
            if (GUILayout.Button("target 1M")) Run.SetTarget(1000000);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("end milestone now")) Run.EndMilestone();
            if (GUILayout.Button("open shop")) Run.OpenShop();
            GUILayout.EndHorizontal();
        }

        private static void Building()
        {
            if (!Header("Building", Build.Available)) return;

            Build.IgnoreRestrictions = GUILayout.Toggle(
                Build.IgnoreRestrictions, " ignore all placement restrictions");
            if (GUILayout.Button("next building anywhere")) Build.NextBuildingAnywhere();
        }

        // Reads the overlay's own summary rather than recomputing anything. The
        // helper decides; the panel reports.
        private static void Helper()
        {
            if (!Config.PlayHelper) return;

            GUILayout.Space(6f);
            GUILayout.Label("Play helper  (" + Config.HelperKey + ")");

            var on = GUILayout.Toggle(Overlay.Enabled, " highlight the best placements");
            if (on != Overlay.Enabled) Overlay.Toggle();
            Note(Overlay.Summary());
            Note(Overlay.Legend);
            Note("Suggestions only. Nothing here is written to the game.");
        }

        // Autoplay is the one thing in this panel that plays FOR you, so it says what
        // it did last and what it will not touch.
        private static void Autoplay()
        {
            if (!Config.Autoplay) return;

            GUILayout.Space(6f);
            GUILayout.Label("Autoplay  (" + Config.AutoplayKey + " run, "
                          + Config.AutoplayStepKey + " step)");

            var running = GUILayout.Toggle(Supervisor.Running, " place buildings automatically");
            if (running != Supervisor.Running) Supervisor.Toggle();

            if (GUILayout.Button("one step")) Supervisor.Step();

            Note(Supervisor.Status);
            Note("Places buildings only. Shops, packs, quests and milestones are yours.");
            Note("Keeps running when the window loses focus. The keys do not - click "
               + "the window before pressing " + Config.AutoplayKey + " to stop it.");
            Note(Config.AutoplayBlocksAchievements
                ? "Counts as a cheat: achievements are blocked while it is used."
                : "Makes only legal moves, so it does not block achievements.");
        }

        private static void Pace()
        {
            if (!Header("Pace", Run.SpeedAvailable)) return;

            var fast = GUILayout.Toggle(Run.SpeedUpScoring, " skip the scoring animation");
            if (fast != Run.SpeedUpScoring) Run.SpeedUpScoring = fast;
            Note("The game's own flag. Not a cheat, and it does not block achievements.");
        }

        private static void PermanentUnlocks()
        {
            if (!Header("Unlocks  (permanent)", Unlocks.Available)) return;

            Note("These write the unlock save, not this run. Abandoning the run will "
               + "not undo them.");
            Confirm(ref _confirmUnlockAll, "unlock all guilds", Unlocks.UnlockAllGuilds);
            Confirm(ref _confirmMaxLevel, "max level all guilds", Unlocks.MaxLevelAllGuilds);
        }

        // --- pieces --------------------------------------------------------------

        private static void Status()
        {
            Note(!Integrity.Cheated
                ? "No cheat used yet. Achievements still submit."
                : Integrity.Blocking
                    ? "Cheats used. Achievements are BLOCKED for this session."
                    : "Cheats used. Achievements are NOT blocked - BlockAchievements is off.");
            GUILayout.Space(4f);
        }

        private static bool Header(string title, bool available)
        {
            GUILayout.Space(6f);
            GUILayout.Label(available ? title : title + "   (not in a run)");
            return available;
        }

        private static void Note(string text)
        {
            GUILayout.Label(text, _note);
        }

        private static void Confirm(ref bool armed, string text, Action action)
        {
            if (!armed)
            {
                if (GUILayout.Button(text)) armed = true;
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("really " + text + "?")) { armed = false; action(); }
            if (GUILayout.Button("no", GUILayout.Width(40f))) armed = false;
            GUILayout.EndHorizontal();
        }
    }
}
