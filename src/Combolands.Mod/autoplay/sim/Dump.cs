using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Combolands.Mod.Autoplay.Sim
{
    // Reading the game's scoring rules out, once, so nothing has to read them again.
    //
    // Everything the simulator needs is a base value on a `_BuildingBehaviour`, and
    // every getter that would normally want a live building accepts `null` and falls
    // back to exactly that base value - `GetScoreForTag(null, tag)` returns what the
    // behaviour declares rather than what one instance has been upgraded to. So the
    // whole rule set can be read from the behaviour dictionary with no run in
    // progress and nothing on the board.
    //
    // The output is `generated/rules.json`, which is NOT committed. It is derived
    // from the game, like `generated/strings.en.json`, and anyone with a copy of the
    // game can make it again. What is committed is what the trainer produces from
    // it - six numbers - which is ours.
    internal static class Dump
    {
        private const string Behaviours = "Entities.BuildingBehaviours.BuildingBehaviours";
        private const string Behaviour = "Entities.BuildingBehaviours._BuildingBehaviour";

        internal static string Write(string path)
        {
            var rules = Read();
            if (rules == null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, rules, new UTF8Encoding(false));
            return path;
        }

        // Whether there is anything to read yet. `BuildingBehaviours` is NOT a
        // MonoSingleton - it is a plain class carrying its own static `Instance`,
        // which is why asking Singletons for it finds nothing however far into a run
        // you are. It is populated when a run starts, so on the menu this is simply
        // "not yet" rather than a fault, and says so quietly.
        internal static bool Ready { get { return Holder() != null; } }

        private static object Holder()
        {
            var type = Anchors.Type(Behaviours);
            if (type == null) return null;

            var instance = Reflect.Property(type, "Instance");
            if (instance == null) return null;

            return Log.Guard("sim.holder", () => instance.GetValue(null, null), null);
        }

        private static string Read()
        {
            var holder = Holder();
            if (holder == null) return null;        // no run in progress; try again later

            var dictionary = Reflect.Property(holder.GetType(), "BuildingBehaviourDict");
            var map = dictionary == null ? null : dictionary.GetValue(holder, null) as IDictionary;
            if (map == null)
            {
                Log.Error("sim", "BuildingBehaviourDict is not a dictionary - see ANCHORS row P17");
                return null;
            }

            var type = Anchors.Type(Behaviour);
            if (type == null) return null;

            var members = new Members(type);
            if (!members.Ok) return null;

            var pieces = new List<string>();
            var categories = new SortedDictionary<int, string>();
            var tileTypes = new SortedDictionary<int, string>();

            int exact = 0, approximate = 0, unknown = 0;

            foreach (DictionaryEntry entry in map)
            {
                var piece = Piece(members, entry.Key, entry.Value, categories, tileTypes);
                if (piece == null) { unknown++; continue; }

                pieces.Add(piece.Text);
                if (piece.Fidelity == Fidelity.Exact) exact++; else approximate++;
            }

            Log.Info("sim", string.Format("read {0} buildings - {1} exact, {2} approximate, {3} unreadable",
                pieces.Count, exact, approximate, unknown));

            return Document(pieces, categories, tileTypes);
        }

        // --- one building -------------------------------------------------------------

        private sealed class Entry
        {
            public string Text;
            public Fidelity Fidelity;
        }

        private static Entry Piece(Members members, object tagValue, object behaviour,
                                   SortedDictionary<int, string> categories,
                                   SortedDictionary<int, string> tileTypes)
        {
            if (!Alive.Is(behaviour)) return null;

            int tag;
            try { tag = Convert.ToInt32(tagValue); }
            catch { return null; }

            var text = new StringBuilder();
            text.Append("    {");
            text.Append("\"tag\": ").Append(tag);
            text.Append(", \"name\": ").Append(JsonValue.Quote(tagValue.ToString()));

            var reach = Enum.GetName(members.PreviewModeType, members.PreviewMode.GetValue(behaviour)) ?? "None";
            var range = Int(members.Range, behaviour);

            // How often it fires. A cooldown-3 Woodcutter scores every third turn,
            // not every turn - `CountIsReadyToActivate` counts down and only pays on
            // zero - so treating it as a per-week income overstates it threefold.
            // The simulator amortises by this.
            var cooldown = Int(members.Cooldown, behaviour);

            text.Append(", \"range\": ").Append(range.ToString(CultureInfo.InvariantCulture));
            text.Append(", \"cooldown\": ").Append(cooldown.ToString(CultureInfo.InvariantCulture));
            var self = Quietly(() => members.ScoreParam.Invoke(behaviour, new object[] { null }));
            text.Append(", \"selfScore\": ").Append(AsInt(self));
            text.Append(", \"rarity\": ").Append(Rarity(members, behaviour, tagValue));

            // Its own categories: the major one plus any minors.
            var own = new List<int>();
            Remember(members.Major.GetValue(behaviour), own, categories);
            Remember(members.Minors.GetValue(behaviour) as IEnumerable, own, categories);
            text.Append(", \"categories\": ").Append(Ints(own));

            var tagScores = Scores(behaviour, members.TargetTags,
                                   t => Score(members.ScoreForTag, behaviour, new object[] { null, t }));
            text.Append(", \"tagScores\": ").Append(tagScores);

            // NOT the same shape as the others. GetBehaviourTargetCategories returns
            // `List<TargetCategory>`, and TargetCategory is a STRUCT OF FIELDS -
            // { TargetNumber, GamePieceCategory, Score } - not an enum. Converting
            // the element itself throws, which is why every building in the first
            // dump scored nothing for any category. The same trap as ANCHORS row P12.
            var categoryScores = CategoryScores(members, behaviour, categories);
            text.Append(", \"categoryScores\": ").Append(categoryScores);

            text.Append(", \"rarityScores\": ")
                .Append(Scores(behaviour, members.TargetRarities,
                               r => Score(members.ScoreForRarity, behaviour, new object[] { r })));

            text.Append(", \"tileTypeScores\": ")
                .Append(Scores(behaviour, members.TargetTileTypes,
                               t => Score(members.ScoreForTileType, behaviour, new object[] { t }),
                               tileTypes));

            text.Append(", \"tileTypes\": ").Append(Placeable(members, behaviour, tagValue, tileTypes));
            text.Append(", \"sameTypeRestricted\": ").Append(Restricted(members, behaviour) ? "true" : "false");

            // A self score that cannot be read without a live building is a score
            // this simulator cannot know - Shrine pays 50 per reroll held. The piece
            // is still worth modelling for what it scores off its neighbours.
            var fidelity = Judge(members, behaviour, reach);
            if (self == null && reach == "SelfOnly") fidelity = Fidelity.Approximate;

            // A building with targets, a range and NO preview mode still scores - it
            // just does not offer the game a preview. Woodcutter is the plain case:
            // eighty points a tree, range two, no `_scorePreviewMode` set, because it
            // pays on a cooldown rather than every week.
            //
            // Calling that zero would silently drop some of the best scorers in the
            // game. Calling it InRange is a guess, so it is a LABELLED guess: the
            // piece is modelled and marked approximate, and `reachInferred` says
            // which of the two the reach came from.
            var inferred = false;
            if (reach == "None" && range > 0 && (tagScores != "{}" || categoryScores != "{}"))
            {
                reach = "InRange";
                inferred = true;
                fidelity = Fidelity.Approximate;
            }

            text.Append(", \"reach\": ").Append(JsonValue.Quote(reach));
            text.Append(", \"reachInferred\": ").Append(inferred ? "true" : "false");
            text.Append(", \"fidelity\": ").Append(JsonValue.Quote(Name(fidelity)));
            text.Append('}');

            return new Entry { Text = text.ToString(), Fidelity = fidelity };
        }

        // Whether this building is one the simulator reproduces exactly.
        //
        // Two ways it is not. A behaviour that overrides GetScorePreview has its own
        // idea of what it scores, and the base reproduction would be wrong. And a
        // behaviour with no preview mode that nonetheless declares targets is scoring
        // through some path the preview does not describe at all.
        //
        // Either way the static score is still computed and is still a lower bound -
        // what is not modelled is what it does BEYOND scoring. Saying which is which
        // is the difference between a simulator and a guess in a simulator's clothes.
        private static Fidelity Judge(Members members, object behaviour, string reach)
        {
            var type = behaviour.GetType();

            var declared = type.GetMethod("GetScorePreview",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (declared != null) return Fidelity.Approximate;

            if (reach == "None")
            {
                var tags = members.TargetTags.Invoke(behaviour, null) as ICollection;
                var cats = members.TargetCategories.Invoke(behaviour, null) as ICollection;
                if ((tags != null && tags.Count > 0) || (cats != null && cats.Count > 0))
                    return Fidelity.Approximate;
            }

            return Fidelity.Exact;
        }

        private static string Name(Fidelity fidelity)
        {
            switch (fidelity)
            {
                case Fidelity.Approximate: return "approximate";
                case Fidelity.Unknown: return "unknown";
                default: return "exact";
            }
        }

        // --- the pieces of one entry ----------------------------------------------------

        private static string Scores(object behaviour, MethodInfo targets, Func<object, int> score,
                                     SortedDictionary<int, string> names = null)
        {
            var text = new StringBuilder("{");
            var first = true;

            var list = Log.Guard("sim.targets", () => targets.Invoke(behaviour, null) as IEnumerable, null);
            if (list != null)
            {
                foreach (var target in list)
                {
                    if (target == null) continue;

                    int key;
                    try { key = Convert.ToInt32(target); }
                    catch { continue; }

                    if (names != null && !names.ContainsKey(key)) names[key] = target.ToString();

                    if (!first) text.Append(", ");
                    first = false;
                    text.Append(JsonValue.Quote(key.ToString(CultureInfo.InvariantCulture)));
                    text.Append(": ").Append(score(target).ToString(CultureInfo.InvariantCulture));
                }
            }

            return text.Append('}').ToString();
        }

        // TargetCategory is { int TargetNumber; GamePieceCategory GamePieceCategory;
        // int Score; } - three public fields. The category and its price are both
        // right there, so there is no need to ask GetTargetCategoryScoreByCategory
        // for something the struct already carries.
        private static string CategoryScores(Members members, object behaviour,
                                             SortedDictionary<int, string> names)
        {
            var text = new StringBuilder("{");
            var first = true;

            var list = Quietly(() => members.TargetCategories.Invoke(behaviour, null)) as IEnumerable;
            if (list != null)
                foreach (var entry in list)
                {
                    if (entry == null) continue;

                    if (members.CategoryField == null)
                    {
                        members.Resolve(entry.GetType());
                        if (members.CategoryField == null) break;
                    }

                    int key, score;
                    try
                    {
                        key = Convert.ToInt32(members.CategoryField.GetValue(entry));
                        score = Convert.ToInt32(members.CategoryScoreField.GetValue(entry));
                    }
                    catch { continue; }

                    if (!names.ContainsKey(key))
                        names[key] = members.CategoryField.GetValue(entry).ToString();

                    if (!first) text.Append(", ");
                    first = false;
                    text.Append(JsonValue.Quote(key.ToString(CultureInfo.InvariantCulture)));
                    text.Append(": ").Append(score.ToString(CultureInfo.InvariantCulture));
                }

            return text.Append('}').ToString();
        }

        private static string Placeable(Members members, object behaviour, object tag,
                                        SortedDictionary<int, string> names)
        {
            var found = new List<int>();

            var list = Log.Guard("sim.terrain",
                () => members.TileTypesFor.Invoke(behaviour, new[] { tag }) as IEnumerable, null);
            if (list != null)
                foreach (var type in list)
                {
                    if (type == null) continue;
                    try
                    {
                        var key = Convert.ToInt32(type);
                        if (!names.ContainsKey(key)) names[key] = type.ToString();
                        found.Add(key);
                    }
                    catch { }
                }

            return Ints(found);
        }

        // The FIELD, not the method.
        //
        // `HasRangePlacementRestriction` ends at `return _hasRangePlacementRestriction`
        // and everything above that return is an exemption granted by run state - an
        // equipped Fishing Net, an adjacent Plaza. Calling it with no building to ask
        // about dereferences `ItemHeirloomController` and throws, which is what filled
        // the log with stack traces; and if it had answered, it would have answered
        // about a run rather than about the building.
        //
        // The field is the rule. The exemptions are the game's to apply at the moment
        // of placing, and it does - see docs/ANCHORS.md row P13.
        private static bool Restricted(Members members, object behaviour)
        {
            if (members.RangeRestriction == null) return false;

            var value = Quietly(() => members.RangeRestriction.GetValue(behaviour));
            return value is bool && (bool)value;
        }

        private static string Rarity(Members members, object behaviour, object tag)
        {
            var value = Quietly(() => members.Rarity == null
                ? null : members.Rarity.Invoke(behaviour, new object[] { null }));

            try { return value == null ? "0" : Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture); }
            catch { return "0"; }
        }

        // Some of these getters want a live building and say so by throwing - Shrine
        // scores `50 * ScoreController.Rerolls`, which has no static answer at all.
        // That is information, not a fault: the piece is marked approximate and the
        // planner treats it as a lower bound. Logging a stack trace for each one
        // buried the two messages in this dump that do mean something.
        private static object Quietly(Func<object> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static void Remember(object value, List<int> into, SortedDictionary<int, string> names)
        {
            if (value == null) return;

            var many = value as IEnumerable;
            if (many != null && !(value is string))
            {
                foreach (var one in many) Remember(one, into, names);
                return;
            }

            try
            {
                var key = Convert.ToInt32(value);
                if (!names.ContainsKey(key)) names[key] = value.ToString();
                if (!into.Contains(key)) into.Add(key);
            }
            catch { }
        }

        private static string Ints(List<int> values)
        {
            var text = new StringBuilder("[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return text.Append(']').ToString();
        }

        private static int Int(FieldInfo field, object owner)
        {
            if (field == null) return 0;

            var value = Quietly(() => field.GetValue(owner));
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static string AsInt(object value)
        {
            try { return value == null ? "0" : Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture); }
            catch { return "0"; }
        }

        private static int Score(MethodInfo method, object owner, object[] args)
        {
            var value = Log.Guard("sim.score", () => method.Invoke(owner, args), null);
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static string Document(List<string> pieces,
                                       SortedDictionary<int, string> categories,
                                       SortedDictionary<int, string> tileTypes)
        {
            var text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"meta\": {\n");
            text.Append("    \"_comment\": \"Read out of the game by Config.DumpRules. Derived from the game, so NOT committed - see .gitignore.\",\n");
            text.Append("    \"game\": ").Append(JsonValue.Quote(UnityEngine.Application.version ?? "")).Append(",\n");
            text.Append("    \"dumped\": ").Append(JsonValue.Quote(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append('\n');
            text.Append("  },\n");

            text.Append("  \"categories\": ").Append(NameMap(categories)).Append(",\n");
            text.Append("  \"tileTypes\": ").Append(NameMap(tileTypes)).Append(",\n");

            text.Append("  \"pieces\": [\n");
            for (int i = 0; i < pieces.Count; i++)
            {
                text.Append(pieces[i]);
                text.Append(i == pieces.Count - 1 ? "\n" : ",\n");
            }
            text.Append("  ]\n}\n");

            return text.ToString();
        }

        private static string NameMap(SortedDictionary<int, string> names)
        {
            var text = new StringBuilder("{");
            var first = true;
            foreach (var pair in names)
            {
                if (!first) text.Append(", ");
                first = false;
                text.Append(JsonValue.Quote(pair.Key.ToString(CultureInfo.InvariantCulture)));
                text.Append(": ").Append(JsonValue.Quote(pair.Value));
            }
            return text.Append('}').ToString();
        }

        // --- the members this needs, resolved once ---------------------------------------

        private sealed class Members
        {
            internal readonly FieldInfo Range, Major, Minors, PreviewMode, Cooldown;

            // Resolved from the first TargetCategory actually seen, because the type
            // lives in the game assembly and naming it here would be one more anchor
            // for no gain.
            internal FieldInfo CategoryField, CategoryScoreField;

            internal void Resolve(Type targetCategory)
            {
                CategoryField = Reflect.Field(targetCategory, "GamePieceCategory");
                CategoryScoreField = Reflect.Field(targetCategory, "Score");

                if (CategoryField == null || CategoryScoreField == null)
                {
                    CategoryField = null;
                    Log.Error("sim", "TargetCategory has no GamePieceCategory/Score fields"
                        + " - category scoring cannot be read. See docs/ANCHORS.md row P12.");
                }
            }
            internal readonly MethodInfo TargetTags, TargetCategories, TargetRarities, TargetTileTypes;
            internal readonly MethodInfo ScoreForTag, ScoreForCategory, ScoreForRarity, ScoreForTileType;
            internal readonly MethodInfo ScoreParam, TileTypesFor, Rarity;
            internal readonly FieldInfo RangeRestriction;
            internal readonly Type PreviewModeType;

            internal Members(Type type)
            {
                Range = Reflect.Field(type, "_range");
                Major = Reflect.Field(type, "_majorCategory");
                Minors = Reflect.Field(type, "_minorCategories");
                PreviewMode = Reflect.Field(type, "_scorePreviewMode");
                Cooldown = Reflect.Field(type, "_cooldownParam");
                PreviewModeType = PreviewMode == null ? null : PreviewMode.FieldType;

                bool ambiguous;
                TargetTags = Reflect.MethodByName(type, "GetBehaviourTargetTags", 0, out ambiguous);
                TargetCategories = Reflect.MethodByName(type, "GetBehaviourTargetCategories", 0, out ambiguous);
                TargetRarities = Reflect.MethodByName(type, "GetBehaviourTargetRarities", 0, out ambiguous);
                TargetTileTypes = Reflect.MethodByName(type, "GetBehaviourTargetTileTypes", 0, out ambiguous);

                ScoreForTag = Reflect.MethodByName(type, "GetScoreForTag", 2, out ambiguous);
                ScoreForCategory = Reflect.MethodByName(type, "GetTargetCategoryScoreByCategory", 2, out ambiguous);
                ScoreForRarity = Reflect.MethodByName(type, "GetScoreForRarity", 1, out ambiguous);
                ScoreForTileType = Reflect.MethodByName(type, "GetScoreForTileType", 1, out ambiguous);

                ScoreParam = Reflect.MethodByName(type, "GetScoreParam", 1, out ambiguous);
                TileTypesFor = Reflect.MethodByName(type, "GetTileTypesCanBePlacedOn", 1, out ambiguous);
                RangeRestriction = Reflect.Field(type, "_hasRangePlacementRestriction");
                Rarity = Reflect.MethodByName(type, "GetRarity", 1, out ambiguous);
            }

            // Everything except Rarity and RangeRestriction, which have sensible
            // fallbacks. A missing scorer is not a piece worth writing - a rule set
            // that half-loaded would produce a plan that is confidently wrong.
            internal bool Ok
            {
                get
                {
                    var missing = Name(Range, "_range") ?? Name(Major, "_majorCategory")
                               ?? Name(Minors, "_minorCategories") ?? Name(PreviewMode, "_scorePreviewMode")
                               ?? Name(TargetTags, "GetBehaviourTargetTags")
                               ?? Name(TargetCategories, "GetBehaviourTargetCategories")
                               ?? Name(TargetRarities, "GetBehaviourTargetRarities")
                               ?? Name(TargetTileTypes, "GetBehaviourTargetTileTypes")
                               ?? Name(ScoreForTag, "GetScoreForTag")
                               ?? Name(ScoreForCategory, "GetTargetCategoryScoreByCategory")
                               ?? Name(ScoreForRarity, "GetScoreForRarity")
                               ?? Name(ScoreForTileType, "GetScoreForTileType")
                               ?? Name(ScoreParam, "GetScoreParam")
                               ?? Name(TileTypesFor, "GetTileTypesCanBePlacedOn");

                    if (missing == null) return true;

                    Log.Error("sim", "_BuildingBehaviour." + missing
                        + " is gone - the rule set cannot be read. See docs/ANCHORS.md row P17.");
                    return false;
                }
            }

            private static string Name(object member, string called)
            {
                return member == null ? called : null;
            }
        }
    }
}
