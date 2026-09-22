using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Combolands.Mod.Autoplay
{
    // The buildings on offer this week, described well enough to score - without
    // instantiating any of them.
    //
    // Instantiating would be a write, and the helper does not write. A prefab would
    // work but lies: reading Values.Range off a prefab runs the live modifier chain
    // against a piece sitting at (0,0), so a TownBell near the origin would change
    // the answer. So everything here comes from the BEHAVIOUR's own base fields,
    // which is what a building is worth before it is anywhere:
    //
    //     _range, _majorCategory, _minorCategories        base stats
    //     GetBehaviourTargetTags()                        parameterless
    //     GetBehaviourTargetCategories()                  parameterless
    //     GetScoreForTag(null, tag)                       ignores the piece in the base
    //     GetTileTypesCanBePlacedOn(tag)                  takes a tag, not an instance
    //
    // Two things cannot be answered this way, and the scout is honest about both:
    // the nineteen behaviours that override CanBeBuiltOn with a rule of their own
    // ("only next to Trees"), and HasRangePlacementRestriction, which dereferences
    // the piece. See Scout below.
    internal struct Offer
    {
        public int Tag;
        public string Name;
        public Piece Piece;
        public Dictionary<int, int> TagScores;
        public Dictionary<int, int> CategoryScores;
        public int MaxScore;
        public int[] TileTypes;      // null means "could not be determined"
    }

    internal static class Offers
    {
        private const string ChoiceBar = "UI.BuildingChoiceBar";

        private static bool _resolved;
        private static PropertyInfo _choices, _choiceTag;
        private static PropertyInfo _behaviourDict;
        private static FieldInfo _range, _major, _minor;
        private static MethodInfo _targetTags, _targetCategories, _scoreForTag, _tileTypes;
        private static PropertyInfo _categoryValue, _categoryScore;
        private static MethodInfo _dataFor;
        private static PropertyInfo _dataName;

        private static readonly Dictionary<int, Offer> Cache = new Dictionary<int, Offer>();

        internal static bool Available { get { return Singletons.Exists(ChoiceBar); } }

        // What the player is being offered, left to right.
        internal static List<Offer> Current()
        {
            var offers = new List<Offer>(4);
            if (!Resolve()) return offers;

            var bar = Singletons.Get(ChoiceBar);
            if (bar == null) return offers;

            var buttons = _choices.GetValue(bar, null) as IEnumerable;
            if (buttons == null) return offers;

            foreach (var button in buttons)
            {
                if (button == null) continue;
                var tag = Convert.ToInt32(_choiceTag.GetValue(button, null));
                if (tag == 0) continue;

                Offer offer;
                if (!Cache.TryGetValue(tag, out offer))
                {
                    if (!Describe(tag, out offer)) continue;
                    Cache[tag] = offer;
                }
                offers.Add(offer);
            }
            return offers;
        }

        // Base stats and declared targets are fixed per building type, so they are
        // worked out once. Nothing here depends on the board.
        private static bool Describe(int tag, out Offer offer)
        {
            offer = default(Offer);

            var behaviour = BehaviourFor(tag);
            if (behaviour == null) return false;

            var categories = new List<int>(4);
            if (_major != null) categories.Add(Convert.ToInt32(_major.GetValue(behaviour)));
            if (_minor != null)
            {
                var minors = _minor.GetValue(behaviour) as IEnumerable;
                if (minors != null)
                    foreach (var category in minors) categories.Add(Convert.ToInt32(category));
            }

            offer.Tag = tag;
            offer.Name = NameOf(tag);
            offer.Piece = new Piece
            {
                Tag = tag,
                Range = _range == null ? 0 : Convert.ToInt32(_range.GetValue(behaviour)),
                Categories = categories.ToArray(),
            };
            offer.TagScores = new Dictionary<int, int>();
            offer.CategoryScores = new Dictionary<int, int>();

            ReadTargets(behaviour, ref offer);
            offer.TileTypes = ReadTileTypes(behaviour, tag);
            return true;
        }

        private static void ReadTargets(object behaviour, ref Offer offer)
        {
            if (_targetTags != null && _scoreForTag != null)
            {
                var tags = _targetTags.Invoke(behaviour, null) as IEnumerable;
                if (tags != null)
                    foreach (var tag in tags)
                    {
                        if (tag == null) continue;
                        // The base GetScoreForTag reads a table and ignores the piece,
                        // which is why null is safe here and nowhere else.
                        var score = Convert.ToInt32(_scoreForTag.Invoke(behaviour, new object[] { null, tag }));
                        if (score <= 0) continue;
                        offer.TagScores[Convert.ToInt32(tag)] = score;
                        if (score > offer.MaxScore) offer.MaxScore = score;
                    }
            }

            if (_targetCategories == null) return;

            var list = _targetCategories.Invoke(behaviour, null) as IEnumerable;
            if (list == null) return;

            foreach (var entry in list)
            {
                if (entry == null) continue;
                if (_categoryValue == null)
                {
                    _categoryValue = Anchors.Property(entry.GetType(), "GamePieceCategory");
                    _categoryScore = Anchors.Property(entry.GetType(), "Score");
                }
                if (_categoryValue == null || _categoryScore == null) return;

                var score = Convert.ToInt32(_categoryScore.GetValue(entry, null));
                if (score <= 0) continue;
                offer.CategoryScores[Convert.ToInt32(_categoryValue.GetValue(entry, null))] = score;
                if (score > offer.MaxScore) offer.MaxScore = score;
            }
        }

        private static int[] ReadTileTypes(object behaviour, int tag)
        {
            if (_tileTypes == null) return null;
            try
            {
                var types = _tileTypes.Invoke(behaviour, new object[] { tag }) as IEnumerable;
                if (types == null) return null;

                var values = new List<int>(4);
                foreach (var type in types) values.Add(Convert.ToInt32(type));
                return values.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // The game's own name, which means the translated one - GetDataFor returns a
        // _BaseData and our A3 patch is on its Name.
        private static string NameOf(int tag)
        {
            if (_dataFor == null) return "#" + tag;
            try
            {
                var holder = ScriptableSingleton("Entities.GamePieceDataHolder");
                if (holder == null) return "#" + tag;

                var enumType = Anchors.Type("Entities.GameTag");
                var data = _dataFor.Invoke(holder, new[] { Enum.ToObject(enumType, tag) });
                if (data == null) return "#" + tag;

                if (_dataName == null) _dataName = Anchors.Property(data.GetType(), "Name");
                return _dataName == null ? "#" + tag : (_dataName.GetValue(data, null) as string ?? "#" + tag);
            }
            catch
            {
                return "#" + tag;
            }
        }

        private static object BehaviourFor(int tag)
        {
            if (_behaviourDict == null) return null;

            var type = Anchors.Type("Entities.BuildingBehaviours.BuildingBehaviours");
            var instance = Anchors.Property(type, "Instance");
            var holder = instance == null ? null : instance.GetValue(null, null);
            if (holder == null) return null;

            var dict = _behaviourDict.GetValue(holder, null) as IDictionary;
            if (dict == null) return null;

            var enumType = Anchors.Type("Entities.GameTag");
            var key = Enum.ToObject(enumType, tag);
            return dict.Contains(key) ? dict[key] : null;
        }

        private static object ScriptableSingleton(string typeName)
        {
            var target = Anchors.Type(typeName);
            var generic = Anchors.Game == null ? null : Anchors.Game.GetType("Library.ScriptableObjectSingleton`1", false);
            if (target == null || generic == null) return null;

            try
            {
                var closed = generic.MakeGenericType(target);
                var instance = Anchors.Property(closed, "Instance");
                return instance == null ? null : instance.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool Resolve()
        {
            if (_resolved) return _choices != null;
            _resolved = true;

            var barType = Anchors.Type(ChoiceBar);
            var buttonType = Anchors.Type("UI.BuildingChoiceButton");
            var behaviourType = Anchors.Type("Entities.BuildingBehaviours._BuildingBehaviour");
            var holderType = Anchors.Type("Entities.BuildingBehaviours.BuildingBehaviours");
            if (barType == null || buttonType == null || behaviourType == null || holderType == null)
                return false;

            _choices = Anchors.Property(barType, "Choices");
            _choiceTag = Anchors.Property(buttonType, "GameTag");
            _behaviourDict = Anchors.Property(holderType, "BuildingBehaviourDict");

            _range = Anchors.Field(behaviourType, "_range");
            _major = Anchors.Field(behaviourType, "_majorCategory");
            _minor = Anchors.Field(behaviourType, "_minorCategories");

            _targetTags = Anchors.MethodByName(behaviourType, "GetBehaviourTargetTags", 0);
            _targetCategories = Anchors.MethodByName(behaviourType, "GetBehaviourTargetCategories", 0);
            _scoreForTag = Anchors.MethodByName(behaviourType, "GetScoreForTag", 2);
            _tileTypes = Anchors.MethodByName(behaviourType, "GetTileTypesCanBePlacedOn", 1);

            var dataHolder = Anchors.Type("Entities.GamePieceDataHolder");
            if (dataHolder != null) _dataFor = Anchors.MethodByName(dataHolder, "GetDataFor", 1);

            if (_choices == null || _choiceTag == null || _behaviourDict == null)
            {
                _choices = null;
                return false;
            }
            return true;
        }

        internal static void Forget()
        {
            Cache.Clear();
        }
    }
}
