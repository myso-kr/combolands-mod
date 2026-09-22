using System;
using System.Reflection;

namespace Combolands.Mod
{
    // Member lookup that survives shadowing. Pure - no Unity, no MelonLoader, no
    // logging - so tests can link it and pin the behaviour.
    //
    // The reason it exists: `Type.GetProperty(name, FlattenHierarchy)` throws
    // AmbiguousMatchException when a derived type redeclares a member with `new`, and
    // the game does exactly that - Building redeclares GamePiece's Behaviour with a
    // narrower type. The throw happened inside a Guard, so the placement helper went
    // quiet and stayed quiet, with nothing on screen to say why.
    //
    // Walking the hierarchy from the most derived type with DeclaredOnly picks the
    // declaration the game's own code would bind to, and cannot be ambiguous between
    // levels - only within one, which is a real overload question and is reported
    // rather than guessed at.
    internal static class Reflect
    {
        internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance | BindingFlags.Static
                                        | BindingFlags.FlattenHierarchy;

        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance | BindingFlags.Static
                                            | BindingFlags.DeclaredOnly;

        internal static PropertyInfo Property(Type owner, string name)
        {
            for (var type = owner; type != null; type = type.BaseType)
            {
                var found = type.GetProperty(name, Declared);
                if (found != null) return found;
            }
            return null;
        }

        internal static FieldInfo Field(Type owner, string name)
        {
            for (var type = owner; type != null; type = type.BaseType)
            {
                var found = type.GetField(name, Declared);
                if (found != null) return found;
            }
            return null;
        }

        internal static MethodInfo Method(Type owner, string name, Type[] args)
        {
            for (var type = owner; type != null; type = type.BaseType)
            {
                var found = args == null || args.Length == 0
                    ? type.GetMethod(name, Declared, null, Type.EmptyTypes, null)
                    : type.GetMethod(name, Declared, null, args, null);
                if (found != null) return found;
            }
            return null;
        }

        // By name and arity, for methods whose parameter types we do not want to spell
        // out - an enum from the game assembly, say.
        //
        // `ambiguous` separates "there is no such method" from "there are two at the
        // same level and picking one would be a guess".
        internal static MethodInfo MethodByName(Type owner, string name, int argCount, out bool ambiguous)
        {
            ambiguous = false;

            for (var type = owner; type != null; type = type.BaseType)
            {
                MethodInfo found = null;
                foreach (var candidate in type.GetMethods(Declared))
                {
                    if (candidate.Name != name) continue;
                    if (candidate.GetParameters().Length != argCount) continue;
                    if (found != null) { ambiguous = true; return null; }
                    found = candidate;
                }
                if (found != null) return found;
            }
            return null;
        }
    }
}
