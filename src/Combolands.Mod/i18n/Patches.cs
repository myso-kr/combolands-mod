using System;
using System.Reflection;
using HarmonyLib;
// See the note in Plugin.cs: MelonLoader's legacy `Harmony` namespace
// shadows the type name.
using HarmonyInstance = HarmonyLib.Harmony;

namespace Combolands.Mod.I18n
{
    // Where the translation actually attaches.
    //
    // A1 carries almost all of it. A3 is here because a game piece with no
    // LocalizedString falls back to showing its ScriptableObject's file name, which
    // has no key to translate by - see the note on literal keys below.
    internal static class Patches
    {
        private const string Id = "kr.myso.combolands.i18n";

        private static FieldInfo _keyField;

        internal static void Apply(HarmonyInstance harmony)
        {
            var asset = Anchors.LocalizedStringAsset;                              // A1, A2
            _keyField = Anchors.Field(asset, "Key");
            var getText = Anchors.Method(asset, "GetText");

            if (getText != null && _keyField != null)
            {
                harmony.Patch(getText, postfix: Post(nameof(ByKey)));
                Log.Info("i18n", "patched LocalizedStringAsset.GetText (A1)");
            }
            else
            {
                Log.Error("i18n", "A1 missing - the translation will not apply at all");
            }

            var name = Anchors.Property(Anchors.BaseData, "Name");                 // A3
            if (name != null && name.GetGetMethod(true) != null)
            {
                harmony.Patch(name.GetGetMethod(true), postfix: Post(nameof(ByLiteral)));
                Log.Info("i18n", "patched _BaseData.Name (A3)");
            }
        }

        private static HarmonyMethod Post(string method)
        {
            return new HarmonyMethod(typeof(Patches).GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Static));
        }

        // --- A1: the choke point --------------------------------------------------

        // Every localised string in the game comes through here. __instance is the
        // LocalizedStringAsset, so its Key is the join - not the English text, and not
        // the asset's position in a list. That is what makes the translation survive
        // Crux rewording their own English.
        private static void ByKey(object __instance, ref string __result)
        {
            if (__instance == null) return;
            var key = Log.Guard("i18n.key", () => _keyField.GetValue(__instance) as string, null);
            string translated;
            if (key != null && Catalog.TryGet(key, out translated))
                __result = translated;
        }

        // --- A3: the ones with no key ---------------------------------------------

        // _BaseData.Name returns base.name - the ScriptableObject's file name - when a
        // piece has no LocalizedString. There is no Key to look up, so the catalogue
        // accepts the English text itself as a key.
        //
        // Two kinds of key in one file is a small price for one lookup path. A
        // translator who sees stray English adds an entry keyed by exactly that
        // English and it is fixed, whether or not the game gave it a key.
        private static void ByLiteral(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            string translated;
            if (Catalog.TryGet(__result, out translated))
                __result = translated;
        }
    }
}
