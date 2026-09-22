using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
        private static PropertyInfo _tileX, _tileY, _tileEmpty, _tileCantBuild, _tileType;
        private static PropertyInfo _buildingList;
        private static PropertyInfo _pieceX, _pieceY, _pieceRange, _pieceTag, _pieceCategories;

        internal static bool Available
        {
            get { return Singletons.Exists(Map) && Singletons.Exists(Buildings); }
        }

        // Returns null when the board cannot be read, which outside a run is normal.
        //
        // `window` is where suggestions may go - normally what the camera can see.
        // Tiles are only read inside it plus a margin, because reading a tile is two
        // reflection calls and a 44x27 board is 1,188 of them. Buildings are read
        // everywhere regardless: one off-screen can easily be in range of a tile on
        // screen, and leaving it out would silently change the answer.
        internal static Snapshot Read(object candidatePiece, Rect? window = null)
        {
            if (!Alive.Is(candidatePiece)) return null;

            var board = ReadBoard(window, RangeOf(candidatePiece));
            if (board == null) return null;

            board.Candidate = ToPiece(candidatePiece);

            // The same-type rule is the candidate's own property, not the tile's, so
            // it is asked once per refresh. Honouring the cheat toggle here keeps the
            // helper and "ignore all placement restrictions" telling the same story.
            board.CandidateRangeRestricted =
                !Cheat.Build.IgnoreRestrictions && HasRangeRestriction(candidatePiece);
            ReadDeclaredTargets(board, candidatePiece);

            // Terrain is left to the game: CanBuildBuildingAt is asked about every
            // shortlisted tile and knows the nineteen per-building overrides we do not.
            board.CandidateTileTypes = null;
            return board;
        }

        // Scouting: a building from the choice bar, which has no instance to ask.
        internal static Snapshot ReadFor(Offer offer, Rect? window = null)
        {
            var board = ReadBoard(window, offer.Piece.Range);
            if (board == null) return null;

            board.Candidate = offer.Piece;
            board.TargetTagScores = offer.TagScores;
            board.TargetCategoryScores = offer.CategoryScores;
            board.MaxTargetScore = offer.MaxScore;
            board.CandidateTileTypes = offer.TileTypes;

            // HasRangePlacementRestriction dereferences the piece, so it cannot be
            // asked about a building that does not exist yet. True is the conservative
            // answer - it is the default for almost everything - and being wrong here
            // hides a legal tile rather than suggesting an illegal one.
            board.CandidateRangeRestricted = !Cheat.Build.IgnoreRestrictions;
            return board;
        }

        private static int RangeOf(object piece)
        {
            if (!Resolve() || _pieceRange == null) return 2;
            try { return (int)_pieceRange.GetValue(piece, null); }
            catch { return 2; }
        }

        private static Snapshot ReadBoard(Rect? window, int candidateRange)
        {
            if (!Available) return null;
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
            board.TileTypes = new int[board.Width * board.Height];
            for (int i = 0; i < board.TileTypes.Length; i++) board.TileTypes[i] = -1;

            if (window.HasValue)
                board.SearchWithin((int)window.Value.xMin, (int)window.Value.yMin,
                                   (int)window.Value.xMax, (int)window.Value.yMax);
            else
                board.SearchWithin(0, 0, board.Width - 1, board.Height - 1);

            // Value.Room looks a short way outside each candidate tile, so the read
            // has to reach past the window by that much or tiles at its edge would
            // look boxed in by nothing.
            int margin = (candidateRange < 2 ? 2 : candidateRange) + 1;
            int fromX = Max(0, board.SearchMinX - margin);
            int fromY = Max(0, board.SearchMinY - margin);
            int toX = Min(board.Width - 1, board.SearchMaxX + margin);
            int toY = Min(board.Height - 1, board.SearchMaxY + margin);

            var args = new object[2];
            for (int y = fromY; y <= toY; y++)
                for (int x = fromX; x <= toX; x++)
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

                    if (_tileType != null)
                        board.TileTypes[y * board.Width + x] =
                            Convert.ToInt32(_tileType.GetValue(tile, null));
                }

            var live = _buildingList.GetValue(Singletons.Get(Buildings), null) as IEnumerable;
            if (live != null)
            {
                foreach (var building in live)
                {
                    // Not `building == null`: that is a reference comparison on an
                    // `object`, and a destroyed Unity object passes it. See Alive.cs.
                    if (!Alive.Is(building)) continue;
                    board.Buildings.Add(ToPiece(building));
                }
            }

            board.PlazaTag = Tag("Plaza");
            board.CorruptedObeliskTag = Tag("CorruptedObelisk");
            board.Pace = ReadPace();
            return board;
        }

        // How far behind the milestone the run is. Everything here is public on the
        // game's own controllers; the only judgement is using LAST WEEK's score as the
        // rate estimate rather than the milestone average - a board that has just come
        // good is exactly when the answer matters, and the average would hide it.
        private static Pace ReadPace()
        {
            const string Game = "GameState.GameController";
            const string Score = "GameState.ScoreController";
            const string Stats = "GameState.RunStatsController";

            if (!Singletons.Exists(Game) || !Singletons.Exists(Score)) return Pace.Unknown;

            var required = Singletons.Read(Game, "ScoreRequired", 0L);
            if (required <= 0) return Pace.Unknown;

            var score = Singletons.Read(Score, "Score", 0L);
            var weeks = Singletons.Read(Game, "WeeksRemaining", 0);
            var lastWeek = Singletons.Exists(Stats)
                ? Singletons.Read(Stats, "BestScore", 0L)
                : 0L;

            return Pace.From(score, required, weeks, lastWeek);
        }

        private static int Max(int a, int b) { return a > b ? a : b; }
        private static int Min(int a, int b) { return a < b ? a : b; }

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

            // The game's implementation counts Plazas adjacent to the piece, which
            // means walking from its tile - and a ghost that is off the map has none.
            // Exec.BringOnMap normally prevents that; this is the seatbelt, and true
            // is the conservative answer.
            try
            {
                var restricted = _hasRangeRestriction.Invoke(behaviour, new[] { piece });
                return restricted is bool && (bool)restricted;
            }
            catch
            {
                return true;
            }
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
            _tileType = Anchors.Property(tileType, "Type");

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

        private static MethodInfo _resetCaches;

        // BuildingExtensions._rangeCache is keyed by Building and ignores the override
        // tile, so a batch of probes shares one answer. The game resets it before
        // every one of its own checks; so does this, for the strict path that has to
        // be believed.
        internal static void ResetGameCaches()
        {
            if (_resetCaches == null)
            {
                _resetCaches = Anchors.Method(Anchors.Type("Entities.BuildingExtensions"), "ResetCaches");
                if (_resetCaches == null) return;
            }
            try { _resetCaches.Invoke(null, null); }
            catch { }
        }

        // The game's whole answer, same-type rule included, with the cache made fresh
        // first. Slower than CanBuildAt and correct where it matters - the fallback
        // that runs when every ranked tile has been refused.
        internal static bool CanBuildAtStrict(object candidatePiece, int x, int y)
        {
            ResetGameCaches();
            return Ask(candidatePiece, x, y, false);
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

            return Ask(candidatePiece, x, y, true);
        }

        // ignoreSameType disables EXACTLY one check inside CanBuildBuildingAt and
        // nothing else. That check is unreliable under batch probing because
        // BuildingExtensions._rangeCache is keyed by Building and ignores the override
        // tile, so thirty probes in a frame get one answer thirty times.
        // Rules.SameTypeInRange evaluates it properly, per tile - but our range shape
        // does not know about Crane or Stable, so the game can still refuse a tile we
        // offered. That is what CanBuildAtStrict is for.
        private static bool Ask(object candidatePiece, int x, int y, bool ignoreSameType)
        {
            var controller = Singletons.Get(Buildings);
            if (controller == null) return false;

            var args = new[] { candidatePiece, x, y, null, (object)ignoreSameType };
            try
            {
                var allowed = _canBuild.Invoke(controller, args);
                return allowed is bool && (bool)allowed;
            }
            catch
            {
                return false;
            }
        }
    }
}
