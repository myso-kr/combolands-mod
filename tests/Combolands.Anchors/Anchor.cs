using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Combolands.Mod;

namespace Combolands.Anchors
{
    internal enum Kind
    {
        // A property or a field, whichever the game happens to use. For members the
        // mod itself probes both ways, which is a handful of them and always
        // deliberately - see Screens.Read and Quests.Read.
        Either,
        Property,
        Field,
        Method,
    }

    internal readonly struct Member
    {
        internal readonly string Name;
        internal readonly Kind Kind;
        internal readonly int ArgCount;

        // An anchor is usually one type, but a few span two - P12 reads a property off
        // GamePiece and calls a method on the behaviour that answers for it. `on`
        // names the type when it is not the anchor's own.
        internal readonly string Owner;

        private Member(string name, Kind kind, int argCount, string on)
        {
            Name = name;
            Kind = kind;
            ArgCount = argCount;
            Owner = on;
        }

        internal static Member Property(string name, string on = null) { return new Member(name, Kind.Property, -1, on); }
        internal static Member Field(string name, string on = null) { return new Member(name, Kind.Field, -1, on); }
        internal static Member Either(string name, string on = null) { return new Member(name, Kind.Either, -1, on); }

        // By arity, not by parameter types. Most of these take game enums, and
        // spelling those out would make the manifest depend on the very assembly it
        // is checking. Arity plus name is what Reflect.MethodByName binds on at
        // runtime, so it is also the honest question to ask.
        internal static Member Method(string name, int argCount, string on = null)
        {
            return new Member(name, Kind.Method, argCount, on);
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case Kind.Method: return Name + "(" + ArgCount + " args)";
                case Kind.Property: return Name + " (property)";
                case Kind.Field: return Name + " (field)";
                default: return Name;
            }
        }
    }

    internal sealed class Anchor
    {
        internal string Id;            // matches the row in docs/ANCHORS.md
        internal string What;          // the same short phrase the table uses
        internal string Assembly = "Assembly-CSharp";
        internal string Type;
        internal Member[] Members = System.Array.Empty<Member>();

        // Whether the mod actually BINDS this - resolves it by name at runtime, or
        // compiles against its signature. The rest of the catalogue is anchors in the
        // documentation's sense: facts about the game the mod leans on without ever
        // naming a member, like an asset layout or the shape of a formula it copied.
        // Those get a `Why` instead of a check, because a probe that passed would be
        // answering a question nobody asked.
        internal bool Bound = true;
        internal string Why;

        // For the handful of anchors whose whole content is "this type still exists":
        // a marker class, a state, a click handler the mod finds by type and clicks
        // through IPointerClickHandler.
        internal bool TypeOnly { get { return Members.Length == 0; } }

        public override string ToString() { return Id + " " + What; }

        // Empty means the anchor holds. Each entry is one member that moved, phrased
        // so the failure message names the thing to go and look at.
        internal IReadOnlyList<string> Check()
        {
            var missing = new List<string>();

            var owners = Members.Select(m => m.Owner ?? Type)
                                .Concat(new[] { Type })
                                .Distinct();

            var resolved = new Dictionary<string, Type>();
            foreach (var name in owners)
            {
                var found = Game.Find(Assembly, name);
                if (found == null) missing.Add(Assembly + " has no type " + name);
                else resolved[name] = found;
            }
            if (missing.Count > 0) return missing;

            foreach (var member in Members)
            {
                var owner = member.Owner ?? Type;

                string why;
                if (!Resolves(resolved[owner], member, out why))
                    missing.Add(owner + "." + member + " - " + why);
            }
            return missing;
        }

        private static bool Resolves(Type type, Member member, out string why)
        {
            why = null;
            try
            {
                switch (member.Kind)
                {
                    case Kind.Property:
                        if (Reflect.Property(type, member.Name) != null) return true;
                        why = Reflect.Field(type, member.Name) != null
                            ? "is a FIELD now, not a property"
                            : "not found";
                        return false;

                    case Kind.Field:
                        if (Reflect.Field(type, member.Name) != null) return true;
                        why = Reflect.Property(type, member.Name) != null
                            ? "is a PROPERTY now, not a field"
                            : "not found";
                        return false;

                    case Kind.Method:
                        bool ambiguous;
                        if (Reflect.MethodByName(type, member.Name, member.ArgCount, out ambiguous) != null)
                            return true;
                        why = ambiguous
                            ? "two overloads take " + member.ArgCount + " arguments, so binding it is a guess"
                            : Overloads(type, member.Name);
                        return false;

                    default:
                        if (Reflect.Property(type, member.Name) != null) return true;
                        if (Reflect.Field(type, member.Name) != null) return true;
                        why = "neither a property nor a field";
                        return false;
                }
            }
            catch (Exception error)
            {
                // A member whose signature mentions a type the resolver cannot find.
                // Worth reporting as a miss rather than swallowing - the mod would
                // hit the same wall at runtime.
                why = error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        // "not found" is a poor answer when the method is right there with a
        // different shape, which is the usual way an anchor breaks.
        private static string Overloads(Type type, string name)
        {
            var arities = new List<int>();
            for (var walk = type; walk != null; walk = walk.BaseType)
            {
                foreach (var method in walk.GetMethods(Reflect.All))
                    if (method.Name == name) arities.Add(method.GetParameters().Length);
            }

            if (arities.Count == 0) return "no method of that name";

            return "found only with " + string.Join("/", arities.Distinct().OrderBy(n => n)) + " arguments";
        }
    }
}
