using System.Reflection;
using HarmonyLib;
using HarmonyInstance = HarmonyLib.Harmony;

namespace Combolands.Mod.Cheat
{
    // Placement rules.
    //
    // BuildingController.CanBuildBuildingAt already takes
    //
    //     bool ignoreAllPlacementRestrictions = false
    //
    // so "build anywhere" is one prefix that flips that argument, not a
    // reimplementation of the rules. The game stays the authority on what a legal
    // tile is; we only change the answer to "does this one have to be legal".
    //
    // The toggle is off by default and off at the start of every session. Left on it
    // would let a building sit somewhere the game will not re-derive on load, and the
    // save is written from the map.
    internal static class Build
    {
        private const string Buildings = "Entities.BuildingController";

        private static bool _patched;
        private static bool _ignoreRestrictions;

        internal static bool Available { get { return Singletons.Exists(Buildings); } }

        internal static bool IgnoreRestrictions
        {
            get { return _ignoreRestrictions; }
            set
            {
                if (_ignoreRestrictions == value) return;
                _ignoreRestrictions = value;
                if (value) Integrity.MarkCheated();
                Log.Info("cheat", "placement restrictions " + (value ? "ignored" : "enforced"));
            }
        }

        internal static void Apply(HarmonyInstance harmony)
        {
            if (_patched) return;

            var controller = Anchors.Type(Buildings);
            var canBuild = Anchors.MethodByName(controller, "CanBuildBuildingAt", 5);
            if (canBuild == null)
            {
                Log.Error("cheat", "CanBuildBuildingAt not found - build-anywhere unavailable");
                return;
            }

            harmony.Patch(canBuild, prefix: new HarmonyMethod(
                typeof(Build).GetMethod(nameof(ForceIgnore), BindingFlags.NonPublic | BindingFlags.Static)));
            _patched = true;
            Log.Info("cheat", "placement prefix installed (C5)");
        }

        // __4 is ignoreAllPlacementRestrictions - the fifth parameter. Setting it by
        // reference before the original runs is the whole feature.
        private static void ForceIgnore(ref bool __4)
        {
            if (_ignoreRestrictions) __4 = true;
        }

        // The game's own flag, for one building rather than all of them. Public, and
        // it draws its own cursor, so it behaves like the consumable that sets it.
        internal static void NextBuildingAnywhere()
        {
            Integrity.MarkCheated();
            Log.Guard("cheat.build", () => Singletons.Call(Buildings, "SetCanPlaceNextBuildingAnywhere", true));
        }

        internal static void Reset()
        {
            _ignoreRestrictions = false;
        }
    }
}
