using System;
using System.Collections.Generic;
using System.Reflection;

namespace Combolands.Mod
{
    // The game hangs its controllers off two generic bases in the Library namespace:
    //
    //     Library.MonoSingleton<T>            .Instance  .HasInstance
    //     Library.SerializedMonoSingleton<T>  .Instance  .HasInstance
    //
    // Static members of a generic type are per constructed type, so asking for
    // MonoSingleton<ScoreController>.Instance really does reach the live one.
    //
    // HasInstance is checked first every time. Instance logs a Unity error when the
    // singleton is missing, and a cheat panel drawn on the main menu - where none of
    // these exist yet - would otherwise write one error per control per frame.
    internal static class Singletons
    {
        private static readonly Dictionary<string, PropertyInfo[]> Cache =
            new Dictionary<string, PropertyInfo[]>(StringComparer.Ordinal);

        private static readonly string[] Bases =
        {
            "Library.MonoSingleton`1",
            "Library.SerializedMonoSingleton`1",
        };

        // Returns null when the singleton does not exist yet, which is the normal
        // state outside a run rather than a fault.
        internal static object Get(string typeName)
        {
            var pair = Resolve(typeName);
            if (pair == null) return null;

            var has = pair[0].GetValue(null, null);
            if (!(has is bool) || !(bool)has) return null;
            return pair[1].GetValue(null, null);
        }

        internal static bool Exists(string typeName)
        {
            var pair = Resolve(typeName);
            if (pair == null) return false;
            var has = pair[0].GetValue(null, null);
            return has is bool && (bool)has;
        }

        private static PropertyInfo[] Resolve(string typeName)
        {
            PropertyInfo[] cached;
            if (Cache.TryGetValue(typeName, out cached)) return cached;

            PropertyInfo[] found = null;
            var target = Anchors.Type(typeName);
            if (target != null)
            {
                foreach (var baseName in Bases)
                {
                    var generic = Anchors.Game.GetType(baseName, false);
                    if (generic == null) continue;

                    Type closed;
                    try { closed = generic.MakeGenericType(target); }
                    catch { continue; }

                    const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                    var has = closed.GetProperty("HasInstance", Flags);
                    var instance = closed.GetProperty("Instance", Flags);
                    if (has == null || instance == null) continue;

                    // MakeGenericType succeeds for the wrong base too - the constraint
                    // is only checked on use - so confirm the target really derives
                    // from this one before believing it.
                    if (!closed.IsAssignableFrom(target)) continue;

                    found = new[] { has, instance };
                    break;
                }
                if (found == null)
                    Log.Warn("singletons", typeName + " is not a MonoSingleton we recognise");
            }

            Cache[typeName] = found;
            return found;
        }

        // --- calling into them ----------------------------------------------------

        internal static bool Call(string typeName, string method, params object[] args)
        {
            var instance = Get(typeName);
            if (instance == null) return false;

            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                types[i] = args[i] == null ? typeof(object) : args[i].GetType();

            var m = args.Length == 0
                ? instance.GetType().GetMethod(method, Anchors.All, null, Type.EmptyTypes, null)
                : instance.GetType().GetMethod(method, Anchors.All, null, types, null);

            // A null argument types as System.Object and will not match, so fall back
            // to matching on name and arity.
            if (m == null)
            {
                foreach (var candidate in instance.GetType().GetMethods(Anchors.All))
                {
                    if (candidate.Name != method) continue;
                    if (candidate.GetParameters().Length != args.Length) continue;
                    m = candidate;
                    break;
                }
            }

            if (m == null)
            {
                Log.Warn("singletons", "no method " + typeName + "." + method
                                     + " taking " + args.Length + " argument(s)");
                return false;
            }

            m.Invoke(instance, args);
            return true;
        }

        internal static T Read<T>(string typeName, string member, T fallback)
        {
            var instance = Get(typeName);
            if (instance == null) return fallback;

            var p = instance.GetType().GetProperty(member, Anchors.All);
            if (p != null) return (T)p.GetValue(instance, null);

            var f = instance.GetType().GetField(member, Anchors.All);
            if (f != null) return (T)f.GetValue(instance);

            return fallback;
        }

        internal static bool Write(string typeName, string field, object value)
        {
            var instance = Get(typeName);
            if (instance == null) return false;

            var f = instance.GetType().GetField(field, Anchors.All);
            if (f == null) return false;
            f.SetValue(instance, value);
            return true;
        }
    }
}
