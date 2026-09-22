using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Combolands.Mod.Autoplay
{
    // The only file in autoplay/ that WRITES to the game.
    //
    // Everything else reads, which is why stage 1 was safe to ship half-finished.
    // This is not, so it does exactly one thing: it performs the player's own
    // actions, through the player's own code paths, with coordinates instead of a
    // mouse.
    //
    //   choosing a building   -> BuildingChoiceButton.OnPointerClick(left)
    //   placing it            -> PlacingBuilding.OnUpdate(...) then PlaceCurrentBuilding(...)
    //
    // Not BuildingController.InstantiateAndBuildBuildingAt, which is the obvious
    // route and the wrong one: it builds the building and skips everything around it
    // - the placement count, the choice bar clearing, multiple-placement, the
    // consumable-on-create, the state transition at the end. The state machine would
    // be left believing the player is still placing.
    //
    // OnUpdate is the same method InteractionController calls every frame. It moves
    // the ghost, recomputes whether the tile is legal, and refreshes the highlight
    // caches the placement effects read. LMBDown is false while we call it, so it
    // will not place on its own; we then invoke the same method its LMB branch does.
    internal static class Exec
    {
        private const string Interaction = "Interaction.InteractionController";
        private const string Map = "Environment.MapController";

        private static bool _resolved;
        private static PropertyInfo _placingState, _currentState, _canInteractWithUi;
        private static MethodInfo _onUpdate, _placeCurrent;
        private static FieldInfo _canPlace;
        private static PropertyInfo _grid, _gridWidth, _gridHeight, _pieceTile;
        private static MethodInfo _getTile;
        private static MethodInfo _onPointerClick;

        // --- choosing -------------------------------------------------------------

        // Clicks a card on the choice bar, exactly as a left click would.
        internal static bool Choose(object choiceButton)
        {
            if (!Alive.Is(choiceButton)) return false;
            if (!Resolve()) return false;

            if (_onPointerClick == null)
            {
                _onPointerClick = Anchors.MethodByName(choiceButton.GetType(), "OnPointerClick", 1);
                if (_onPointerClick == null) return false;
            }

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            _onPointerClick.Invoke(choiceButton, new object[] { data });

            Acted();
            Log.Info("autoplay", "picked a building from the choice bar");
            return true;
        }

        internal static bool CanUseUi()
        {
            if (!Resolve() || _canInteractWithUi == null) return false;
            var controller = Singletons.Get(Interaction);
            if (controller == null) return false;
            var allowed = _canInteractWithUi.GetValue(controller, null);
            return allowed is bool && (bool)allowed;
        }

        // --- placing --------------------------------------------------------------

        // Is the held ghost standing on an actual tile?
        //
        // It follows the cursor, and in windowed mode the cursor leaves the window the
        // moment the player looks at something else. Off the map, Building.Tile is
        // null - and the game's own accessors walk from it without checking:
        //
        //   Values.Range  -> GetBehaviourRange -> GetCountOfBuildingsOfTypeAdjacent
        //   HasRangePlacementRestriction -> GetAdjacentTiles -> Get4Neighbours(null)
        //
        // so simply reading the piece throws. That is why autoplay stopped the instant
        // the cursor left the window, and why it looked like a click problem.
        internal static bool OnMap(object piece)
        {
            if (!Alive.Is(piece) || !Resolve()) return false;
            if (_pieceTile == null) return false;

            try { return Alive.Is(_pieceTile.GetValue(piece, null)); }
            catch { return false; }
        }

        // Walks the ghost back onto the board, through the same method a mouse move
        // uses. A write, but the smallest possible one, and autoplay is about to move
        // this piece anyway.
        internal static bool BringOnMap(object piece)
        {
            if (!Resolve() || !Alive.Is(piece)) return false;

            var controller = Singletons.Get(Interaction);
            if (controller == null) return false;

            var state = _currentState.GetValue(controller, null);
            if (state == null || !ReferenceEquals(state, _placingState.GetValue(controller, null)))
                return false;

            var map = Singletons.Get(Map);
            if (map == null || _grid == null) return false;
            var grid = _grid.GetValue(map, null);
            if (grid == null) return false;

            int width = Convert.ToInt32(_gridWidth.GetValue(grid, null));
            int height = Convert.ToInt32(_gridHeight.GetValue(grid, null));
            if (width <= 0 || height <= 0) return false;

            int x = width / 2, y = height / 2;
            var tile = _getTile.Invoke(grid, new object[] { x, y });
            if (!Alive.Is(tile)) return false;

            _onUpdate.Invoke(state, new object[]
            {
                new Vector3(x, y, 0f), new Vector2Int(x, y), tile, true,
            });
            return OnMap(piece);
        }

        // Returns true when a building was actually placed.
        internal static bool PlaceAt(int x, int y)
        {
            if (!Resolve()) return false;

            var controller = Singletons.Get(Interaction);
            if (controller == null) return false;

            var state = _currentState.GetValue(controller, null);
            var placing = _placingState.GetValue(controller, null);
            if (state == null || !ReferenceEquals(state, placing)) return false;

            var tile = TileAt(x, y);
            if (!Alive.Is(tile)) return false;

            // Step one: the game's own per-frame update, told the cursor is over this
            // tile. Moves the ghost, revalidates, rebuilds the highlight caches that
            // the placement effects read.
            _onUpdate.Invoke(state, new object[]
            {
                new Vector3(x, y, 0f),
                new Vector2Int(x, y),
                tile,
                true,               // isCursorOverNewCoords
            });

            // Step two: the game's own answer to "may this go here", computed by the
            // call above. If it says no, nothing happens - the helper does not get to
            // overrule the rules.
            var allowed = _canPlace.GetValue(state);
            if (!(allowed is bool) || !(bool)allowed) return false;

            _placeCurrent.Invoke(state, new object[] { new Vector2Int(x, y) });
            Acted();
            Log.Info("autoplay", "placed at (" + x + "," + y + ")");
            return true;
        }

        // Autoplay makes only legal moves, so by the mod's own definition it is not a
        // cheat and does not close the achievement gate. Whether an achievement a bot
        // earned is one you earned is a different question and the player's, so the
        // setting exists and says so.
        private static void Acted()
        {
            if (Config.AutoplayBlocksAchievements) Cheat.Integrity.MarkCheated();
        }

        private static object TileAt(int x, int y)
        {
            var map = Singletons.Get(Map);
            if (map == null || _grid == null || _getTile == null) return null;

            var grid = _grid.GetValue(map, null);
            if (grid == null) return null;
            return _getTile.Invoke(grid, new object[] { x, y });
        }

        // --- resolution -----------------------------------------------------------

        private static bool Resolve()
        {
            if (_resolved) return _placeCurrent != null;
            _resolved = true;

            var controllerType = Anchors.Type(Interaction);
            var placingType = Anchors.Type("Interaction.InteractionStates.PlacingBuilding");
            var mapType = Anchors.Type(Map);
            if (controllerType == null || placingType == null || mapType == null) return false;

            _placingState = Anchors.Property(controllerType, "PlacingBuilding");
            _currentState = Anchors.Property(controllerType, "CurrentInteractionState");
            _canInteractWithUi = Anchors.Property(controllerType, "CanInteractWithUi");

            _onUpdate = Anchors.MethodByName(placingType, "OnUpdate", 4);
            _placeCurrent = Anchors.MethodByName(placingType, "PlaceCurrentBuilding", 1);
            _canPlace = Anchors.Field(placingType, "_canPlaceCurrentBuilding");

            _grid = Anchors.Property(mapType, "Grid");
            if (_grid != null)
            {
                _getTile = _grid.PropertyType.GetMethod("GetTile", Anchors.All, null,
                    new[] { typeof(int), typeof(int) }, null);
                _gridWidth = Anchors.Property(_grid.PropertyType, "Width");
                _gridHeight = Anchors.Property(_grid.PropertyType, "Height");
            }
            _pieceTile = Anchors.Property(Anchors.Type("Entities.Building"), "Tile");

            var ok = _placingState != null && _currentState != null && _onUpdate != null
                  && _placeCurrent != null && _canPlace != null && _getTile != null;
            if (!ok)
            {
                Log.Error("autoplay", "the placement path is not resolvable - autoplay is off");
                _placeCurrent = null;
            }
            return ok;
        }
    }
}
