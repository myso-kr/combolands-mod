using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Combolands.Mod.Autoplay
{
    // The only code that touches MapController and BuildingController.
    //
    // Everything it knows how to do is turn the live game into a Snapshot. Reading
    // game state and deciding what to do with it are different jobs; entangled,
    // neither is testable. Split, Value.cs takes a plain board and CI can run it.
    //
    // Every MemberInfo is resolved once. A 44x27 board is 1,188 tiles, and looking up
    // "IsEmpty" by name for each one is the difference between a few milliseconds and
    // a visible hitch.
    internal static class Board
    {
        private const string Map = "Environment.MapController";
        private const string Buildings = "Entities.BuildingController";

        private static bool _resolved;
        private static PropertyInfo _grid, _gridWidth, _gridHeight;
        private static MethodInfo _getTile;
        private static PropertyInfo _tileX, _tileY, _tileEmpty, _tileCantBuild;
        private static PropertyInfo _buildingList;
        private static PropertyInfo _pieceX, _pieceY, _pieceRange, _pieceTag, _pieceCategories;

        internal static bool Available
        {
            get { return Singletons.Exists(Map) && Singletons.Exists(Buildings); }
        }

        // Returns null when the board cannot be read, which outside a run is normal.
        internal static Snapshot Read(object candidatePiece)
        {
            if (!Available || candidatePiece == null) return null;
            if (!Resolve()) return null;

            var map = Singletons.Get(Map);
            var grid = _grid.GetValue(map, null);
            if (grid == null) return null;

            var board = new Snapshot
            {
                Width = (int)_gridWidth.GetValue(grid, null),
                Height = (int)_gridHeight.GetValue(grid, null),
            };
            if (board.Width <= 0 || board.Height <= 0) return null;
            board.Buildable = new bool[board.Width * board.Height];

            var args = new object[2];
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    args[0] = x; args[1] = y;
                    var tile = _getTile.Invoke(grid, args);
                    if (tile == null) continue;

                    // The cheap half of buildability. The game's real rule - terrain,
                    // range restrictions, nesting - is asked about later, and only
                    // for the handful of tiles that make the shortlist.
                    var empty = (bool)_tileEmpty.GetValue(tile, null);
                    var cantBuild = (bool)_tileCantBuild.GetValue(tile, null);
                    board.Buildable[y * board.Width + x] = empty && !cantBuild;
                }

            var live = _buildingList.GetValue(Singletons.Get(Buildings), null) as IEnumerable;
            if (live != null)
            {
                foreach (var building in live)
                {
                    if (building == null) continue;
                    board.Buildings.Add(ToPiece(building));
                }
            }

            board.Candidate = ToPiece(candidatePiece);

            // The same-type rule is the candidate's own property, not the tile's, so
            // it is asked once per refresh. Honouring the cheat toggle here keeps the
            // helper and "ignore all placement restrictions" telling the same story.
            board.CandidateRangeRestricted =
                !Cheat.Build.IgnoreRestrictions && HasRangeRestriction(candidatePiece);
            board.PlazaTag = Tag("Plaza");
            board.CorruptedObeliskTag = Tag("CorruptedObelisk");
            ReadDeclaredTargets(board, candidatePiece);
            return board;
        }

        private static Piece ToPiece(object piece)
        {
            return new Piece
            {
                X = (int)_pieceX.GetValue(piece, null),
                Y = (int)_pieceY.GetValue(piece, null),
                Range = (int)_pieceRange.GetValue(piece, null),
                Tag = Convert.ToInt32(_pieceTag.GetValue(piece, null)),
                Categories = Categories(piece),
            };
        }

        // GamePiece.Categories is a HashSet<GamePieceCategory>. Naming that enum here
        // would mean referencing Assembly-CSharp, and the valuation only needs to
        // know whether two pieces share a member, so the values come across as ints.
        private static int[] Categories(object piece)
        {
            var set = _pieceCategories.GetValue(piece, null) as IEnumerable;
            if (set == null) return new int[0];

            var values = new List<int>(4);
            foreach (var category in set)
                values.Add(Convert.ToInt32(category));
            return values.ToArray();
        }

        // Resolved by NAME. GameTag has hundreds of members and Crux renumber it as
        // they add buildings, so a hardcoded 214 would quietly become some other
        // building after an update.
        private static readonly Dictionary<string, int> TagCache = new Dictionary<string, int>();

        private static int Tag(string name)
        {
            int value;
            if (TagCache.TryGetValue(name, out value)) return value;

            value = -1;
            var enumType = Anchors.Type("Entities.GameTag");
            if (enumType != null && Enum.IsDefined(enumType, name))
                value = Convert.ToInt32(Enum.Parse(enumType, name));
            else
                Log.Warn("helper", "GameTag." + name + " not found - that rule is off");

            TagCache[name] = value;
            return value;
        }

        private static PropertyInfo _targetTags, _values, _targetCategories;
        private static MethodInfo _scoreForTag, _scoreForCategory;

        // What the candidate says it is looking for. All four of these are pure
        // lookups into tables the behaviour already holds - nothing is triggered and
        // no state is touched.
        private static void ReadDeclaredTargets(Snapshot board, object piece)
        {
            if (_targetTags == null)
            {
                var pieceType = Anchors.Type("Entities.Building");
                _targetTags = Anchors.Property(pieceType, "TargetTags");
                _values = Anchors.Property(pieceType, "Values");
            }
            if (_targetTags == null || _values == null) return;

            var behaviour = Behaviour(piece);
            var tags = _targetTags.GetValue(piece, null) as IEnumerable;
            if (behaviour != null && tags != null)
            {
                if (_scoreForTag == null)
                    _scoreForTag = Anchors.MethodByName(behaviour.GetType(), "GetScoreForTag", 2);

                if (_scoreForTag != null)
                    foreach (var tag in tags)
                    {
                        if (tag == null) continue;
                        var score = Convert.ToInt32(_scoreForTag.Invoke(behaviour, new[] { piece, tag }));
                        if (score <= 0) continue;
                        board.TargetTagScores[Convert.ToInt32(tag)] = score;
                        if (score > board.MaxTargetScore) board.MaxTargetScore = score;
                    }
            }

            var values = _values.GetValue(piece, null);
            if (values == null) return;

            if (_targetCategories == null)
                _targetCategories = Anchors.Property(values.GetType(), "TargetCategories");
            if (_targetCategories == null) return;

            var categories = _targetCategories.GetValue(values, null) as IEnumerable;
            if (categories == null) return;

            if (_scoreForCategory == null)
                _scoreForCategory = Anchors.MethodByName(values.GetType(), "GetScoreForTargetCategory", 1);
            if (_scoreForCategory == null) return;

            foreach (var category in categories)
            {
                if (category == null) continue;
                var score = Convert.ToInt32(_scoreForCategory.Invoke(values, new[] { category }));
                if (score <= 0) continue;
                board.TargetCategoryScores[Convert.ToInt32(category)] = score;
                if (score > board.MaxTargetScore) board.MaxTargetScore = score;
            }
        }

        private static PropertyInfo _behaviour;
        private static MethodInfo _hasRangeRestriction;

        private static object Behaviour(object piece)
        {
            if (_behaviour == null)
            {
                _behaviour = Anchors.Property(Anchors.Type("Entities.Building"), "Behaviour");
                if (_behaviour == null) return null;
            }
            return _behaviour.GetValue(piece, null);
        }

        private static bool HasRangeRestriction(object piece)
        {
            var behaviour = Behaviour(piece);
            if (behaviour == null) return false;

            if (_hasRangeRestriction == null)
            {
                _hasRangeRestriction = Anchors.MethodByName(
                    behaviour.GetType(), "HasRangePlacementRestriction", 1);
                if (_hasRangeRestriction == null) return false;
            }

            var restricted = _hasRangeRestriction.Invoke(behaviour, new[] { piece });
            return restricted is bool && (bool)restricted;
        }

        private static bool Resolve()
        {
            if (_resolved) return _grid != null;
            _resolved = true;

            var mapType = Anchors.Type(Map);
            var buildingsType = Anchors.Type(Buildings);
            var tileType = Anchors.Type("Environment.Tile");
            var pieceType = Anchors.Type("Entities.Building");
            if (mapType == null || buildingsType == null || tileType == null || pieceType == null)
                return false;

            _grid = Anchors.Property(mapType, "Grid");
            if (_grid == null) return false;

            var gridType = _grid.PropertyType;
            _gridWidth = Anchors.Property(gridType, "Width");
            _gridHeight = Anchors.Property(gridType, "Height");
            _getTile = gridType.GetMethod("GetTile", Anchors.All, null,
                new[] { typeof(int), typeof(int) }, null);

            _tileX = Anchors.Property(tileType, "X");
            _tileY = Anchors.Property(tileType, "Y");
            _tileEmpty = Anchors.Property(tileType, "IsEmpty");
            _tileCantBuild = Anchors.Property(tileType, "CantBuildOn");

            _buildingList = Anchors.Property(buildingsType, "Buildings");

            _pieceX = Anchors.Property(pieceType, "X");
            _pieceY = Anchors.Property(pieceType, "Y");
            _pieceRange = Anchors.Property(pieceType, "Range");
            _pieceTag = Anchors.Property(pieceType, "Tag");
            _pieceCategories = Anchors.Property(pieceType, "Categories");

            var ok = _gridWidth != null && _gridHeight != null && _getTile != null
                  && _tileEmpty != null && _tileCantBuild != null && _buildingList != null
                  && _pieceX != null && _pieceY != null && _pieceRange != null
                  && _pieceTag != null && _pieceCategories != null;
            if (!ok) _grid = null;
            return ok;
        }

        // The game's own placement rule, for one tile. Reflection per call, so this is
        // asked only about shortlisted tiles - never about all 1,188.
        private static MethodInfo _canBuild;

        internal static bool CanBuildAt(object candidatePiece, int x, int y)
        {
            if (_canBuild == null)
            {
                _canBuild = Anchors.MethodByName(Anchors.Type(Buildings), "CanBuildBuildingAt", 5);
                if (_canBuild == null) return true;   // unknown, so do not hide the tile
            }

            var controller = Singletons.Get(Buildings);
            if (controller == null) return false;

            // true here disables EXACTLY one check inside CanBuildBuildingAt - the
            // same-type-in-range one - and nothing else. That check is unreliable
            // under batch probing because BuildingExtensions._rangeCache is keyed by
            // Building and ignores the override tile, so thirty probes in a frame get
            // one answer thirty times. Rules.SameTypeInRange evaluates it properly,
            // per tile. Everything else the game checks stays authoritative.
            var args = new[] { candidatePiece, x, y, null, (object)true };
            var allowed = _canBuild.Invoke(controller, args);
            return allowed is bool && (bool)allowed;
        }
    }
}
