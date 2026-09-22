// MelonLoader ships a legacy `Harmony` NAMESPACE at global scope, so neither the
// bare type name nor an alias called `Harmony` compiles. Hence HarmonyInstance.
using HarmonyInstance = HarmonyLib.Harmony;
using MelonLoader;
using Combolands.Mod;
using Combolands.Mod.Autoplay;
using Combolands.Mod.Cheat;
using Combolands.Mod.I18n;

[assembly: MelonInfo(typeof(Plugin), "Combolands Mod", "0.3.0", "myso-kr",
    "https://github.com/myso-kr/combolands-mod")]
[assembly: MelonGame("Crux Games", "Combolands")]

namespace Combolands.Mod
{
    // The entry point: what runs, in what order. No decisions live here.
    public class Plugin : MelonMod
    {
        private static readonly HarmonyInstance Patcher = new HarmonyInstance("kr.myso.combolands");
        private bool _dumped;

        public override void OnInitializeMelon()
        {
            Config.Load();

            if (!Anchors.Ready)
            {
                Log.Error("plugin", "Assembly-CSharp is not loaded; nothing to patch");
                return;
            }

            if (Config.Translate)
            {
                // Order matters exactly once: the font has to exist before the first
                // string is drawn, or the first frame of the menu is tofu.
                Log.Guard("font", Font.Create);
                Catalog.Load(Config.Language);
                Log.Guard("i18n", () => Patches.Apply(Patcher));
            }
            else
            {
                Log.Info("plugin", "translation disabled by config");
            }

            if (Config.Cheats)
            {
                // The achievement gate goes in first. A cheat that ran before it was
                // installed would be a cheat the gate never saw.
                Log.Guard("cheat.integrity", () => Integrity.Apply(Patcher));
                Log.Guard("cheat.build", () => Build.Apply(Patcher));
                Log.Guard("cheat.widget", () => Widget.Apply(Patcher));
                Log.Info("cheat", "widget ready on " + Config.WidgetKey);
            }
            else
            {
                Log.Info("plugin", "cheats disabled by config");
            }

            if (Config.PlayHelper)
            {
                Log.Guard("helper.hook", () => Overlay.Apply(Patcher));
                Log.Info("helper", "placement overlay ready on " + Config.HelperKey);
            }

            if (Config.Autoplay)
                Log.Info("autoplay", "ready - " + Config.AutoplayKey + " to run, "
                                   + Config.AutoplayStepKey + " for one step");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Not once at startup: two of the game's fonts only load with the game
            // scene. See Font.RegisterWithLoadedFonts.
            if (Config.Translate)
                Log.Guard("font.scene", Font.RegisterWithLoadedFonts);

            // Placement rules are per-run state, and a new scene is a new run. Left
            // on across one, a building could end up where the game will not re-derive
            // it on load - and the save is written from the map.
            Build.Reset();
            Overlay.Clear();
            Offers.Forget();

            // A new scene is a new run. A loop left running across one would start
            // placing buildings before the player has looked at the board.
            Supervisor.Stop();

            if (_dumped || !Config.DumpStrings) return;
            _dumped = true;
            Log.Guard("dump", Dump.Run);
        }

        public override void OnUpdate()
        {
            if (Config.Cheats) Log.Guard("cheat.input", Widget.Update);

            if (Config.Autoplay)
            {
                if (UnityEngine.Input.GetKeyDown(Config.AutoplayKey))
                    Log.Guard("autoplay.toggle", Supervisor.Toggle);
                if (UnityEngine.Input.GetKeyDown(Config.AutoplayStepKey))
                    Log.Guard("autoplay.step", () => Supervisor.Step());
                Log.Guard("autoplay.tick", Supervisor.Tick);
            }

            if (!Config.PlayHelper) return;
            if (UnityEngine.Input.GetKeyDown(Config.HelperKey)) Log.Guard("helper.toggle", Overlay.Toggle);
            Log.Guard("helper.refresh", Overlay.Refresh);
        }

        public override void OnGUI()
        {
            // The overlay draws first so the cheat panel sits on top of it.
            if (Config.PlayHelper) Log.Guard("helper.draw", Overlay.Draw);
            if (Config.Cheats) Log.Guard("cheat.draw", Widget.Draw);
        }
    }
}
