using System;
using System.Linq;
using System.Reflection;

namespace Combolands.Mod
{
    // Every game type this mod touches is reached from here, by name, at runtime.
    //
    // The plugin does not reference Assembly-CSharp. That is deliberate: a game update
    // that renames one method should cost one logged miss and one dead feature, not a
    // plugin that fails to load and takes the other two features with it.
    //
    // The names below are the catalogue in docs/ANCHORS.md. Keep them in step.
    internal static class Anchors
    {
        internal const BindingFlags All = Reflect.All;

        private static Assembly _game;

        internal static Assembly Game
        {
            get
            {
                return _game ?? (_game = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp"));
            }
        }

        internal static bool Ready
        {
            get { return Game != null; }
        }

        // --- the anchors, by id ---------------------------------------------------

        internal static Type LocalizedStringAsset   { get { return Type("Library.Localization.LocalizedStringAsset"); } }   // A1, A2
        internal static Type BaseData               { get { return Type("Entities.Data._BaseData"); } }                     // A3
        internal static Type LocalizedString        { get { return Type("Library.Localization.LocalizedString"); } }        // A4
        internal static Type StringLookup           { get { return Type("Interaction.StringLookup"); } }                    // A5

        // --- resolution -----------------------------------------------------------

        internal static Type Type(string fullName)
        {
            var asm = Game;
            if (asm == null) { Miss("Assembly-CSharp", fullName); return null; }
            var t = asm.GetType(fullName, false);
            if (t == null) Miss("type", fullName);
            return t;
        }

        // Lookup itself lives in Reflect, which is pure and tested. Anchors adds the
        // one thing a test cannot: saying out loud which anchor went missing.
        internal static MethodInfo Method(Type owner, string name, params System.Type[] args)
        {
            if (owner == null) return null;
            var found = Reflect.Method(owner, name, args);
            if (found == null) Miss("method", owner.FullName + "." + name);
            return found;
        }

        internal static MethodInfo MethodByName(Type owner, string name, int argCount)
        {
            if (owner == null) return null;
            bool ambiguous;
            var found = Reflect.MethodByName(owner, name, argCount, out ambiguous);
            if (found == null)
                Miss(ambiguous ? "unambiguous method" : "method", owner.FullName + "." + name);
            return found;
        }

        internal static PropertyInfo Property(Type owner, string name)
        {
            if (owner == null) return null;
            var found = Reflect.Property(owner, name);
            if (found == null) Miss("property", owner.FullName + "." + name);
            return found;
        }

        internal static FieldInfo Field(Type owner, string name)
        {
            if (owner == null) return null;
            var found = Reflect.Field(owner, name);
            if (found == null) Miss("field", owner.FullName + "." + name);
            return found;
        }

        private static void Miss(string kind, string what)
        {
            // Not Guard(): a missing anchor is the one thing worth saying loudly and
            // by name, because it is what a game update looks like from in here.
            Log.Warn("anchors", "MISSING " + kind + " " + what + " - see docs/ANCHORS.md");
        }
    }
}
