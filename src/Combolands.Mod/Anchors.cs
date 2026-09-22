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
        internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance | BindingFlags.Static
                                        | BindingFlags.FlattenHierarchy;

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

        internal static MethodInfo Method(Type owner, string name, params System.Type[] args)
        {
            if (owner == null) return null;
            var m = args.Length == 0
                ? owner.GetMethod(name, All, null, System.Type.EmptyTypes, null)
                : owner.GetMethod(name, All, null, args, null);
            if (m == null) Miss("method", owner.FullName + "." + name);
            return m;
        }

        // For methods whose parameter types we do not want to spell out - an enum
        // from the game assembly, say. Ambiguity is reported rather than guessed at.
        internal static MethodInfo MethodByName(Type owner, string name, int argCount)
        {
            if (owner == null) return null;
            MethodInfo found = null;
            foreach (var m in owner.GetMethods(All))
            {
                if (m.Name != name || m.GetParameters().Length != argCount) continue;
                if (found != null)
                {
                    Miss("unambiguous method", owner.FullName + "." + name);
                    return null;
                }
                found = m;
            }
            if (found == null) Miss("method", owner.FullName + "." + name);
            return found;
        }

        internal static PropertyInfo Property(Type owner, string name)
        {
            if (owner == null) return null;
            var p = owner.GetProperty(name, All);
            if (p == null) Miss("property", owner.FullName + "." + name);
            return p;
        }

        internal static FieldInfo Field(Type owner, string name)
        {
            if (owner == null) return null;
            var f = owner.GetField(name, All);
            if (f == null) Miss("field", owner.FullName + "." + name);
            return f;
        }

        private static void Miss(string kind, string what)
        {
            // Not Guard(): a missing anchor is the one thing worth saying loudly and
            // by name, because it is what a game update looks like from in here.
            Log.Warn("anchors", "MISSING " + kind + " " + what + " - see docs/ANCHORS.md");
        }
    }
}
