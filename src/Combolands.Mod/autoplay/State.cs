using System.Reflection;

namespace Combolands.Mod.Autoplay
{
    // When it is meaningful to say anything.
    //
    // The helper has exactly one thing to offer, and only while the player is holding
    // a building: where to put it. Every other InteractionState - shopping, opening a
    // pack, reading the council room - is one where a grid of highlights would be
    // noise drawn over a menu.
    //
    // This is also the seam stages 2 and 3 grow from. Knowing WHEN it is safe to act
    // is a different question from knowing WHAT to do, and it is the harder one: a
    // signature tells you ChangeToState exists, nothing tells you when calling it
    // will not strand the UI mid-transition.
    internal static class State
    {
        private const string Interaction = "Interaction.InteractionController";

        private static bool _resolved;
        private static PropertyInfo _current, _placing;
        private static FieldInfo _currentlyPlacing;

        // The Building following the cursor, or null when the player is not placing.
        internal static object PieceBeingPlaced()
        {
            if (!Resolve()) return null;

            var controller = Singletons.Get(Interaction);
            if (controller == null) return null;

            var state = _current.GetValue(controller, null);
            if (state == null) return null;

            // Reference equality against the controller's own PlacingBuilding, rather
            // than a type-name check: the states are single instances the controller
            // owns, so this is exactly the question being asked.
            var placingState = _placing.GetValue(controller, null);
            if (!ReferenceEquals(state, placingState)) return null;

            return _currentlyPlacing.GetValue(state);
        }

        private static bool Resolve()
        {
            if (_resolved) return _current != null;
            _resolved = true;

            var controllerType = Anchors.Type(Interaction);
            var placingType = Anchors.Type("Interaction.InteractionStates.PlacingBuilding");
            if (controllerType == null || placingType == null) return false;

            _current = Anchors.Property(controllerType, "CurrentInteractionState");
            _placing = Anchors.Property(controllerType, "PlacingBuilding");
            _currentlyPlacing = Anchors.Field(placingType, "_currentlyPlacing");

            if (_current == null || _placing == null || _currentlyPlacing == null)
            {
                _current = null;
                return false;
            }
            return true;
        }
    }
}
