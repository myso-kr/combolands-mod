using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Combolands.Mod.Autoplay
{
    // The screens a run stops on, and how to get past them.
    //
    // Placing buildings is only part of playing Combolands. Between milestones the
    // game puts up a milestone summary that waits for a click, a pack to pick from, a
    // shop to enter or skip - and a loop that only knows how to place buildings sits
    // in front of each one forever.
    //
    // Every action here is the player's: a public method the UI itself calls, or a
    // left click on the same handler a mouse would hit. Nothing reaches past the UI
    // into game state.
    //
    // Order matters. These are tried before placement because they BLOCK placement -
    // there is no point ranking tiles while a modal is up.
    internal static class Screens
    {
        private const string Milestone = "UI.MilestoneScreen.MilestoneScreen";
        private const string Dialog = "Shared.UI.MessageDialog";
        private const string Packs = "UI.PackSelectionPanel";
        private const string ShopPanelType = "UI.ShopPanel";

        internal static string Last = "";

        // Nothing is touched for a moment after an action lands.
        //
        // These screens close through coroutines and tweens, so the flag that said
        // "this screen is up" is still true on the next tick - and calling the same
        // method twice is not idempotent. ShopExterior.SkipShop consumes its skip
        // reward on the first line and nulls it on the last, while IsShown is cleared
        // later by a routine, so the second call dereferenced null and threw every
        // tick for the rest of the run.
        //
        // The precondition below is the real fix for that one. This is the general
        // guard, because it will not be the last method here with a precondition we
        // cannot see.
        // Real seconds, not ticks: the loop's own delay is configurable, so a counter
        // of steps would mean anything from half a second to half a minute.
        private static float _settledAt;
        private const float Settle = 1.5f;

        // Returns true when something was dealt with, so the loop can stop for this
        // tick rather than also trying to place a building into a closing modal.
        // MilestoneManager runs a queue after every milestone - NewMilestonePack,
        // ShopVisit, SnapshotMap, SelectQuest, ShowNewTarget - and each element that
        // waits for the player is a place a placement-only loop stops forever. Every
        // one of them is handled here, in the order they can appear.
        internal static bool Handle()
        {
            if (Time.unscaledTime < _settledAt) return true;
            if (!Act()) return false;

            _settledAt = Time.unscaledTime + Settle;
            return true;
        }

        private static bool Act()
        {
            if (ClaimQuestReward()) return true;
            if (DismissMilestone()) return true;
            if (CloseDialog()) return true;
            if (TakeStartOfMilestonePack()) return true;
            if (ChooseQuest()) return true;
            if (PickFromPack()) return true;
            if (LeaveShop()) return true;
            return false;
        }

        // --- the start-of-milestone pack ------------------------------------------

        // A row of packs to choose between. They are plain click handlers, so this is
        // the click - and there is no board-dependent way to rank a sealed pack, so it
        // takes the first rather than pretending to judge.
        private static bool TakeStartOfMilestonePack()
        {
            var pack = FirstActive("UI.StartOfMilestonePack");
            if (pack == null) return false;
            if (!LeftClick(pack)) return false;

            Last = "took a start-of-milestone pack";
            Log.Info("autoplay", Last);
            return true;
        }

        // --- the council request --------------------------------------------------

        // Which request to take is a real decision - autoplay cannot do all of them -
        // so it lives in Quests.cs with the rest of the council handling.
        private static bool ChooseQuest()
        {
            if (!Quests.Choose()) return false;
            Last = Quests.Last;
            return true;
        }

        // A finished request puts its reward behind a button and disables input until
        // it is pressed, so this goes first: nothing else on this list can proceed
        // while it is up.
        private static bool ClaimQuestReward()
        {
            if (!Quests.ClaimReward()) return false;
            Last = Quests.Last;
            return true;
        }

        // --- finding and clicking UI that is not a singleton -----------------------

        // These panels hold their options as instantiated children rather than in a
        // list we can read, so they are found by type. FindObjectsOfTypeAll also
        // returns prefabs and inactive objects, hence the activeInHierarchy test - a
        // click on a prefab does nothing and looks like a stall.
        private static UnityEngine.Object FirstActive(string typeName)
        {
            var type = Anchors.Type(typeName);
            if (type == null) return null;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch { return null; }

            for (int i = 0; i < found.Length; i++)
            {
                var behaviour = found[i] as Component;
                if (!Alive.Is(behaviour)) continue;
                if (!behaviour.gameObject.activeInHierarchy) continue;
                return found[i];
            }
            return null;
        }

        private static bool LeftClick(object target)
        {
            if (!Alive.Is(target)) return false;

            var click = Anchors.MethodByName(target.GetType(), "OnPointerClick", 1);
            if (click == null) return false;

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            click.Invoke(target, new object[] { data });
            return true;
        }

        // --- the milestone summary ------------------------------------------------

        // It sets a flag and waits. ProcessClick clears the flag, which is exactly
        // what clicking does - the screen does no other work in its click handler.
        private static bool DismissMilestone()
        {
            var screen = Singletons.Get(Milestone);
            if (screen == null) return false;

            var waiting = Read<bool>(screen, "WaitingForClick");
            if (!waiting) return false;

            if (!Call(screen, "ProcessClick")) return false;
            Last = "closed the milestone summary";
            Log.Info("autoplay", Last);
            return true;
        }

        // --- modal dialogs --------------------------------------------------------

        // Close, never a button. A MessageDialog is also how the game asks "abandon
        // this run?" and "reset your save?", and answering those on the player's
        // behalf is not automation, it is vandalism. Closing is the same as walking
        // away from the question.
        private static bool CloseDialog()
        {
            var dialog = Singletons.Get(Dialog);
            if (dialog == null) return false;
            if (!Read<bool>(dialog, "IsShown")) return false;

            if (!Call(dialog, "Close")) return false;
            Last = "dismissed a dialog";
            Log.Info("autoplay", Last);
            return true;
        }

        // --- pack selection -------------------------------------------------------

        private static FieldInfo _options;
        private static Type _shopItemType;
        private static int _packStuck;

        private static bool HasConsumableSpace()
        {
            var panel = Singletons.Get("UI.ConsumablesPanel");
            if (panel == null) return true;      // cannot tell; do not block on it

            bool ambiguous;
            var hasSpace = Reflect.MethodByName(panel.GetType(), "HasSpace", 1, out ambiguous);
            if (hasSpace == null) return true;

            try
            {
                // false: ask, do not make the game print the message at us.
                var space = hasSpace.Invoke(panel, new object[] { false });
                return !(space is bool) || (bool)space;
            }
            catch { return true; }
        }

        // Picks the option that scores best on the board as it stands, which for a
        // blueprint is a real answer and for anything else falls back to "the first
        // one", because a heirloom's worth is not a function of the map.
        private static bool PickFromPack()
        {
            var panel = Singletons.Get(Packs);
            if (panel == null) return false;

            if (_options == null)
            {
                _options = Anchors.Field(panel.GetType(), "_options");
                if (_options == null) return false;
            }

            var list = _options.GetValue(panel) as IList;
            if (list == null || list.Count == 0) { _packStuck = 0; return false; }

            // Clicking an option the game has nowhere to put does nothing but print
            // "slots are full" - and then we click it again next tick, forever. Let
            // the loop fall through instead: Items.UseOne spends a blueprint, which
            // frees a slot, and the option is still here afterwards.
            if (!HasConsumableSpace())
            {
                if (++_packStuck < 6) return false;

                // Nothing freed a slot in six tries. An unopened pack is a loss; a
                // run that never continues is a bigger one.
                _packStuck = 0;
                if (!Call(panel, "SkipSelection")) return false;
                Last = "skipped a pack - no room for anything in it";
                Log.Info("autoplay", Last);
                return true;
            }
            _packStuck = 0;

            int best = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < list.Count; i++)
            {
                if (!Alive.Is(list[i])) continue;
                if (best < 0) best = i;                  // the fallback: something

                var tag = TagOf(list[i]);
                if (tag == 0) continue;

                float score;
                if (!Score(tag, out score)) continue;
                if (score <= bestScore) continue;

                bestScore = score;
                best = i;
            }

            if (best < 0) return false;
            if (!ClickOption(list[best])) return false;

            Last = "took a pack option";
            Log.Info("autoplay", Last);
            return true;
        }

        // How good would this building be on the board right now? Reuses the same
        // description path scouting uses, so a pack pick and a card pick agree.
        private static bool Score(int tag, out float score)
        {
            score = 0f;

            Offer offer;
            if (!Offers.Describe(tag, out offer)) return false;

            var board = Board.ReadFor(offer, null);
            if (board == null) return false;

            var picks = Plan.Best(board, 1, 0);
            if (picks.Count == 0) return false;

            score = picks[0].Score.Total;
            return true;
        }

        private static int TagOf(object shopSingle)
        {
            var data = Read<object>(shopSingle, "Data");
            if (data == null) return 0;

            var property = Anchors.Property(data.GetType(), "GameTag");
            if (property == null) return 0;

            try { return Convert.ToInt32(property.GetValue(data, null)); }
            catch { return 0; }
        }

        // ShopSingle requires a ShopItem on the same object, and ShopItem is what
        // implements IPointerClickHandler. So this is the same left click a player
        // makes, one component along.
        private static bool ClickOption(object shopSingle)
        {
            var behaviour = shopSingle as Component;
            if (behaviour == null) return false;

            if (_shopItemType == null)
            {
                _shopItemType = Anchors.Type("UI.Shop.ShopItem");
                if (_shopItemType == null) return false;
            }

            var item = behaviour.GetComponent(_shopItemType);
            if (!Alive.Is(item)) return false;

            var click = Anchors.MethodByName(_shopItemType, "OnPointerClick", 1);
            if (click == null) return false;

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            click.Invoke(item, new object[] { data });
            return true;
        }

        // --- the shop -------------------------------------------------------------

        // Buying is a judgement this does not make yet; leaving is not. A shop left
        // open is a run that never continues, so for now autoplay finishes shopping
        // without spending, and Shop.cs will take the decision over.
        // The shop is a decision, not a door to close, so it has its own file.
        private static bool LeaveShop()
        {
            if (!Shop.Act()) return false;
            Last = Shop.Last;
            return true;
        }

        // --- plumbing -------------------------------------------------------------

        // Reflect, not Anchors: this PROBES for a member that may legitimately be
        // either a property or a field, and Anchors logs a missing-anchor warning on
        // the way past. ShopPanel.Exterior is a public field, so the property attempt
        // was writing a warning several times a second about something that was
        // working perfectly.
        private static T Read<T>(object target, string member)
        {
            if (!Alive.Is(target)) return default(T);
            try
            {
                var property = Reflect.Property(target.GetType(), member);
                if (property != null) return (T)property.GetValue(target, null);

                var field = Reflect.Field(target.GetType(), member);
                if (field != null) return (T)field.GetValue(target);
            }
            catch { }
            return default(T);
        }

        private static bool Call(object target, string method)
        {
            if (!Alive.Is(target)) return false;

            bool ambiguous;
            var info = Reflect.MethodByName(target.GetType(), method, 0, out ambiguous);
            if (info == null) return false;

            info.Invoke(target, null);
            return true;
        }
    }
}
