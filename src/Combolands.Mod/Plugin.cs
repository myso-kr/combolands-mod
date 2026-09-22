// MelonLoader ships a legacy `Harmony` NAMESPACE at global scope, so neither the
// bare type name nor an alias called `Harmony` compiles. Hence HarmonyInstance.
using HarmonyInstance = HarmonyLib.Harmony;
using MelonLoader;
using Combolands.Mod;
using Combolands.Mod.I18n;

[assembly: MelonInfo(typeof(Plugin), "Combolands Mod", "0.1.0", "myso-kr",
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

            if (!Config.Translate)
            {
                Log.Info("plugin", "translation disabled by config");
                return;
            }

            // Order matters exactly once: the font has to exist before the first
            // string is drawn, or the first frame of the menu is tofu.
            Log.Guard("font", Font.Create);
            Catalog.Load(Config.Language);
            Log.Guard("i18n", () => Patches.Apply(Patcher));
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Not once at startup: two of the game's fonts only load with the game
            // scene. See Font.RegisterWithLoadedFonts.
            Log.Guard("font.scene", Font.RegisterWithLoadedFonts);

            if (_dumped || !Config.DumpStrings) return;
            _dumped = true;
            Log.Guard("dump", Dump.Run);
        }
    }
}
