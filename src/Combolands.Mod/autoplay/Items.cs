using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Combolands.Mod.Autoplay
{
    // Using what the run has picked up.
    //
    // Consumables split by what happens when you use one:
    //
    //   Blueprint        -> PlacingBuilding. The loop already plays that well
    //   BuildingUpgrade  -> ChangingStat / PaintingBuilding   } a targeting state
    //   Potion           -> Triggering / Transforming / Moving }
    //
    // Only the first is used here, and the reason is specific rather than cautious.
    // The targeting states apply their effect through
    // `_BuildingUtilityTargetingState.ClickedOnBuilding`, whose first line is
    // `if (InputKeys.LMBDown)` - a real mouse button, which a mod cannot synthesise
    // without patching LMB globally and making every other click handler in the game
    // fire at once. Entering one of those states and then backing out risks consuming
    // a limited item for nothing, so autoplay does not start what it cannot finish.
    //
    // What it does do is refuse to be stuck in one: if a targeting state is somehow
    // current and nothing is driving it, the state is exited the way a right click
    // would exit it.
    internal static class Items
    {
        private const string Panel = "UI.ConsumablesPanel";
        private const string Interaction = "Interaction.InteractionController";

        internal static string Last = "";

        // --- using a blueprint ----------------------------------------------------

        private static PropertyInfo _slots, _usePanel, _slotObject;
        private static PropertyInfo _consumableType;
        private static FieldInfo _consumableField;
        private static Type _behaviourType;

        // Blueprints are placed, and placement is the thing this loop is good at. The
        // only question is when: a consumable held is a consumable not lost, so they
        // are spent when the shelf is full and a new one would be refused, or when the
        // milestone is close enough that holding anything back is a waste.
        internal static bool UseOne(Pace pace)
        {
            var panel = Singletons.Get(Panel);
            if (panel == null) return false;

            if (!Resolve(panel)) return false;
            if (!ShouldSpend(panel, pace)) return false;

            var slots = _slots.GetValue(panel, null) as IEnumerable;
            if (slots == null) return false;

            foreach (var slot in slots)
            {
                if (!Alive.Is(slot)) continue;

                var held = _slotObject.GetValue(slot, null) as GameObject;
                if (!Alive.Is(held)) continue;

                var behaviour = held.GetComponent(_behaviourType);
                if (!Alive.Is(behaviour)) continue;
                if (!IsBlueprint(behaviour)) continue;

                if (!Use(behaviour, panel)) continue;

                Last = "used a blueprint";
                Log.Info("autoplay", Last);
                return true;
            }
            return false;
        }

        // Clicking a consumable only opens the use/sell panel; the panel's own Use
        // button is what runs the effect. Both steps, in order, exactly as a player
        // does them.
        private static bool Use(object behaviour, object panel)
        {
            var click = Anchors.MethodByName(behaviour.GetType(), "OnPointerClick", 1);
            if (click == null) return false;

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            click.Invoke(behaviour, new object[] { data });

            var use = _usePanel.GetValue(panel, null);
            if (!Alive.Is(use)) return false;

            bool ambiguous;
            var press = Reflect.MethodByName(use.GetType(), "PressUseButton", 0, out ambiguous);
            if (press == null) return false;

            press.Invoke(use, null);
            return true;
        }

        private static bool ShouldSpend(object panel, Pace pace)
        {
            if (Config.AutoplayUseItems == 0) return false;      // never
            if (Config.AutoplayUseItems == 2) return true;       // always

            // The default: when the shelf is full, or when the milestone is tight
            // enough that a held card is a wasted one.
            bool ambiguous;
            var hasSpace = Reflect.MethodByName(panel.GetType(), "HasSpace", 1, out ambiguous);
            if (hasSpace != null)
            {
                try
                {
                    var space = hasSpace.Invoke(panel, new object[] { false });
                    if (space is bool && !(bool)space) return true;
                }
                catch { }
            }

            return pace.Known && pace.Pressure >= 1.5f;
        }

        private static bool IsBlueprint(object behaviour)
        {
            // GameItemType.Blueprint. Resolved by name so a renumbered enum cannot
            // quietly turn "blueprint" into "potion" - which would mean autoplay
            // entering a targeting state it deliberately avoids.
            if (_blueprintValue == int.MinValue)
            {
                var enumType = Anchors.Type("GameState.Data.GameItemType")
                            ?? Anchors.Type("Entities.GameItemType");
                _blueprintValue = enumType != null && Enum.IsDefined(enumType, "Blueprint")
                    ? Convert.ToInt32(Enum.Parse(enumType, "Blueprint"))
                    : -1;
            }
            if (_blueprintValue < 0) return false;

            var consumable = _consumableField.GetValue(behaviour);
            if (!Alive.Is(consumable)) return false;

            if (_consumableType == null)
            {
                _consumableType = Anchors.Property(consumable.GetType(), "ConsumableType");
                if (_consumableType == null) return false;
            }

            try { return Convert.ToInt32(_consumableType.GetValue(consumable, null)) == _blueprintValue; }
            catch { return false; }
        }

        private static int _blueprintValue = int.MinValue;

        private static bool Resolve(object panel)
        {
            if (_slots != null) return true;

            _slots = Anchors.Property(panel.GetType(), "Slots");
            _usePanel = Anchors.Property(panel.GetType(), "ConsumablesUsePanel");
            _slotObject = Anchors.Property(Anchors.Type("UI.UiObjectSlot"), "CurrentUiObject");
            _behaviourType = Anchors.Type("Entities.ConsumableBehaviour");

            // A private FIELD, not a property. ConsumableBehaviour keeps its
            // Consumable in `_consumable` and exposes no accessor for it.
            if (_behaviourType != null)
                _consumableField = Anchors.Field(_behaviourType, "_consumable");

            var ok = _slots != null && _usePanel != null && _slotObject != null
                  && _behaviourType != null && _consumableField != null;
            if (!ok) _slots = null;
            return ok;
        }

        // --- refusing to be stuck in a targeting state -----------------------------

        private static Type _targetingBase;
        private static MethodInfo _exit;

        // A paint or a potion puts the game into a state that waits for a click on a
        // building. Autoplay does not start those, but the player might, or an effect
        // might - and a loop that cannot drive the state would otherwise sit behind it
        // forever. This leaves the way a right click leaves.
        internal static bool EscapeTargeting()
        {
            var controller = Singletons.Get(Interaction);
            if (controller == null) return false;

            var current = Anchors.Property(controller.GetType(), "CurrentInteractionState");
            var state = current == null ? null : current.GetValue(controller, null);
            if (state == null) return false;

            if (_targetingBase == null)
            {
                _targetingBase = Anchors.Type("Interaction.InteractionStates._BuildingUtilityTargetingState");
                if (_targetingBase == null) return false;
            }
            if (!_targetingBase.IsInstanceOfType(state)) return false;

            if (_exit == null)
            {
                _exit = Reflect.Method(state.GetType(), "ExitEffectTargetingState", null);
                if (_exit == null) return false;
            }

            _exit.Invoke(state, null);
            Last = "backed out of a targeting effect it cannot aim";
            Log.Info("autoplay", Last);
            return true;
        }
    }
}
