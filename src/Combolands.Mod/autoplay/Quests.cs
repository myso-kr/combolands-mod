using System;
using System.Collections;
using UnityEngine;

namespace Combolands.Mod.Autoplay
{
    // Council requests: choosing one, steering toward it, and collecting the reward.
    //
    // Collecting is not optional. MilestoneManager.CompleteCurrentMilestoneRoutine
    // puts the game into ClaimingQuestReward - which disables all input - and then
    // spins on `while (IsQuestRewardWaitingToBeClaimed)` with no timeout and no way
    // out. An unclaimed reward does not cost you a reward; it ends the run where it
    // stands. That is the hard stall, and the reason this file exists.
    //
    // Choosing matters for a softer reason: a request is a commitment for a whole
    // milestone and autoplay cannot satisfy every kind there is. It does not aim
    // potions or paints, does not dismiss and does not remove, so a request to do
    // those is one it will fail however well the buildings go. Taking the first
    // option, which is what it did before, drew one of those about a third of the
    // time.
    internal static class Quests
    {
        private const string Controller = "Quests.CouncilQuestController";
        private const string TextArea = "UI.Quests.CouncilQuestTextArea";
        private const string CompletedAnim = "UI.Quests.QuestCompletedAnim";
        private const string SelectionPanel = "UI.Quests.QuestSelectionPanel";
        private const string Consumables = "UI.ConsumablesPanel";
        private const string Heirlooms = "UI.HeirloomsPanel";

        internal static string Last = "";

        // CouncilQuestType by NAME rather than by number, so a reordered enum cannot
        // quietly turn "place buildings" into "cast spells". Ranked by what actually
        // advances each one - every entry below was read off its progress hook in
        // CouncilQuestController, not guessed from the name.
        private static readonly string[] Preferred =
        {
            // CreateBuilding() fires from BuildingController for every building put
            // down. This is the loop's entire job; the request comes free.
            "CreateBuildings",

            // ScorePoints() advances it only when the building that scored carries
            // the requested category - so it is steerable, and
            // Value.WeightQuestCategory is what steers it.
            "EarnPointsOfCategory",

            // BuildBlueprints() fires from PlacingBuilding, and blueprints are the one
            // kind of consumable autoplay does spend.
            "BuildBlueprints",

            // These three advance on their own as the board runs: crops transform,
            // buildings gain multipliers, gold arrives. Nothing to play toward, but
            // nothing to play against either.
            "TransformBuildings",
            "IncreaseBuildingMult",
            "EarnGold",
        };

        private static readonly string[] Avoid =
        {
            // UseSpell() and UseBuildingSupply() come from the consumable panel's
            // targeting flow, which needs a real mouse button held down - see Items.cs
            // for why a mod cannot supply one.
            "UseSpells",
            "UseBuildingSupplies",

            // Dismissing and removing are things autoplay never does.
            "DismissBuildings",
            "RemoveBuildings",

            // And this one is not a judgement call: nothing in the game calls
            // ProceedQuestsOfTypeByAmount for FinishEarly. The type is in the enum and
            // has no progress hook anywhere, so it cannot be completed.
            "FinishEarly",
        };

        // --- collecting -----------------------------------------------------------

        private static int _claimTries;

        // The button, not the animation.
        //
        // QuestCompletedAnim also has a PressClaimRewardButton, but it dereferences
        // its quest unconditionally and its whole object hides itself after about five
        // seconds - at which point the reward button flies across the screen and lands
        // in the quest panel, which is where it lives from then on.
        // CouncilQuestTextArea's version checks its own precondition before doing
        // anything, so it is both the safer call and the one a player presses.
        internal static bool ClaimReward()
        {
            var area = Singletons.Get(TextArea);
            if (area == null) return false;

            var quest = FirstQuest(area);
            if (quest == null) { _claimTries = 0; return false; }
            if (!Flag(quest, "Completed")) { _claimTries = 0; return false; }

            var reward = Read(quest, "QuestReward");
            if (reward == null || Flag(reward, "RewardClaimed")) { _claimTries = 0; return false; }

            // The completion animation is still playing. It hands the button over when
            // it finishes; pressing through it would claim into a screen that is about
            // to move.
            if (AnimPlaying()) return false;

            // The button only exists while the request list is expanded, because that
            // is what it is parented to. A player who collapsed the panel has to open
            // it again, and so does this.
            if (!Expanded(area))
            {
                if (!Call(area, "PressHeaderButton")) return false;
                Last = "opened the council panel to claim a reward";
                Log.Info("autoplay", Last);
                return true;
            }

            // ProcessReward refuses when the reward needs a slot and there is none,
            // and says so on screen. Pressing again changes nothing, so fall through
            // instead and let Items.UseOne spend a blueprint - the reward is still
            // here next tick. That works for a consumable slot and cannot work for an
            // heirloom one, which is what the attempt counter below is for.
            if (!RoomFor(reward))
            {
                if (++_claimTries < 8) return false;
                return SkipReward(area, reward);
            }

            if (!Call(area, "PressClaimRewardButton")) return false;
            _claimTries++;

            Last = "claimed a council reward";
            Log.Info("autoplay", Last);
            return true;
        }

        // The game's own escape hatch, and a real loss - the reward is thrown away. It
        // is taken only when nothing has been able to make room for it, and the
        // alternative is a milestone that never completes.
        //
        // The button refuses outside the ClaimingQuestReward state, which is only
        // entered while a milestone is completing - and that is the one moment the
        // reward has to go. So this checks whether the press actually took rather than
        // announcing a loss that did not happen; a refused skip just resets the wait.
        private static bool SkipReward(object area, object reward)
        {
            _claimTries = 0;
            if (!Call(area, "PressSkipRewardButton")) return false;
            if (!Flag(reward, "RewardClaimed")) return false;

            Last = "gave up a council reward - nothing could make room for it";
            Log.Warn("autoplay", Last);
            return true;
        }

        private static bool AnimPlaying()
        {
            var anim = Singletons.Get(CompletedAnim) as Component;
            return Alive.Is(anim) && anim.gameObject.activeInHierarchy;
        }

        private static bool Expanded(object area)
        {
            var block = Read(area, "_textBlock") as Component;
            if (!Alive.Is(block)) return true;      // cannot tell; do not block on it
            return block.gameObject.activeInHierarchy;
        }

        // Can this reward actually be taken right now?
        //
        // Only two of the twenty reward types can refuse: ProcessReward returns false
        // for a blueprint with no consumable slot and for an heirloom with no heirloom
        // slot. Everything else - gold, rerolls, removes, dismisses, packs - is taken
        // unconditionally, and a favour with nowhere to go quietly becomes a vote. So
        // this asks the panel that the reward would actually go into, rather than
        // gating every reward on the consumable shelf.
        private static bool RoomFor(object reward)
        {
            var type = Read(reward, "RewardType");
            if (type == null) return true;

            var name = type.ToString();

            // false: ask the question, do not make the game announce the answer.
            if (name.IndexOf("Blueprint", StringComparison.OrdinalIgnoreCase) >= 0)
                return HasSpace(Consumables, false);

            // Heirloom space depends on the tag - a duplicate can stack where a new
            // one would not fit.
            if (name.IndexOf("Heirloom", StringComparison.OrdinalIgnoreCase) >= 0)
                return HasSpace(Heirlooms, Read(reward, "RewardTag"));

            return true;
        }

        private static bool HasSpace(string panelType, object argument)
        {
            var panel = Singletons.Get(panelType);
            if (panel == null || argument == null) return true;

            bool ambiguous;
            var hasSpace = Reflect.MethodByName(panel.GetType(), "HasSpace", 1, out ambiguous);
            if (hasSpace == null) return true;

            try
            {
                var space = hasSpace.Invoke(panel, new object[] { argument });
                return !(space is bool) || (bool)space;
            }
            catch { return true; }
        }

        private static object FirstQuest(object area)
        {
            var list = Read(area, "_currentQuests") as IList;
            if (list == null || list.Count == 0) return null;
            return list[0];
        }

        // --- choosing -------------------------------------------------------------

        private static bool _rerolled;

        internal static bool Choose()
        {
            var panel = Singletons.Get(SelectionPanel);
            if (panel == null) return false;

            var options = ActiveOptions();
            if (options.Count == 0) { _rerolled = false; return false; }

            object best = null;
            int bestRank = int.MinValue;
            string bestType = "";

            for (int i = 0; i < options.Count; i++)
            {
                var type = TypeNameOf(options[i]);
                var rank = Rank(type);
                if (rank <= bestRank) continue;

                bestRank = rank;
                best = options[i];
                bestType = type;
            }

            if (best == null) return false;

            // The first reroll of a selection is free - _currentRerollCost starts at
            // zero and Confirm resets it - so when every option is one autoplay cannot
            // finish, there is nothing to lose by asking for three more. Once only:
            // the cost goes up immediately, and a second roll would be spending the
            // run's gold on a coin flip.
            if (bestRank < 0 && !_rerolled)
            {
                _rerolled = true;
                if (Call(panel, "PressRerollButton"))
                {
                    Last = "rerolled the council requests - none of them were doable";
                    Log.Info("autoplay", Last);
                    return true;
                }
            }

            // SelectQuest is what the option's own click handler calls, and calling it
            // directly avoids the one hazard in that handler: it TOGGLES, so selecting
            // the chosen option a second time would clear the selection - and Confirm
            // dereferences that selection with no null check of its own.
            if (!Invoke(panel, "SelectQuest", best)) return false;
            if (Read(panel, "_selected") == null) return false;
            if (!Call(panel, "Confirm")) return false;

            _rerolled = false;
            Last = "took the " + (bestType.Length > 0 ? bestType : "council") + " request";
            Log.Info("autoplay", Last);
            return true;
        }

        private static int Rank(string type)
        {
            for (int i = 0; i < Avoid.Length; i++)
                if (Avoid[i] == type) return -10;

            // Earlier in the list is better, so the score falls as the index rises.
            for (int i = 0; i < Preferred.Length; i++)
                if (Preferred[i] == type) return Preferred.Length - i;

            // Unrecognised - a type added by a patch, most likely. Better than one we
            // know cannot be finished, worse than one we know can.
            return 0;
        }

        // The options are instantiated children rather than a list we can read, so
        // they are found by type. FindObjectsOfTypeAll also returns prefabs and
        // inactive objects, and selecting a prefab does nothing while looking exactly
        // like a stall.
        private static System.Collections.Generic.List<object> ActiveOptions()
        {
            var live = new System.Collections.Generic.List<object>(4);

            var type = Anchors.Type("UI.Quests.CouncilQuestOptionButton");
            if (type == null) return live;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch { return live; }

            for (int i = 0; i < found.Length; i++)
            {
                var behaviour = found[i] as Component;
                if (!Alive.Is(behaviour)) continue;
                if (!behaviour.gameObject.activeInHierarchy) continue;
                live.Add(found[i]);
            }
            return live;
        }

        private static string TypeNameOf(object option)
        {
            var quest = Read(option, "Quest");
            if (quest == null) return "";

            var value = Read(quest, "Type");
            return value == null ? "" : value.ToString();
        }

        // --- steering toward it ---------------------------------------------------

        // The GamePieceCategory an active "score N points with X" request wants, or -1
        // for anything else.
        //
        // This is the only request the valuation can help with, and the hook says
        // exactly why: ScorePoints advances it only when the building that scored
        // carries the requested category. So the useful bias is toward TAKING
        // buildings of that category - a choice between candidates, not between tiles.
        // See Value.WeightQuestCategory.
        internal static int TargetCategory()
        {
            var controller = Singletons.Get(Controller);
            if (controller == null) return -1;

            bool ambiguous;
            var current = Reflect.MethodByName(controller.GetType(), "CurrentQuest", 0, out ambiguous);
            if (current == null) return -1;

            object quest;
            try { quest = current.Invoke(controller, null); }
            catch { return -1; }
            if (quest == null) return -1;

            if (Flag(quest, "Completed")) return -1;

            var type = Read(quest, "Type");
            if (type == null || type.ToString() != "EarnPointsOfCategory") return -1;

            var category = Read(quest, "TargetCategory");
            if (category == null) return -1;

            try { return Convert.ToInt32(category); }
            catch { return -1; }
        }

        // --- plumbing -------------------------------------------------------------

        // Reflect rather than Anchors: these members are a mix of public properties,
        // public fields and private fields, and probing for one shape after another is
        // normal here - it is not a missing anchor worth a warning every tick.
        private static object Read(object target, string member)
        {
            if (!Alive.Is(target)) return null;
            try
            {
                var property = Reflect.Property(target.GetType(), member);
                if (property != null) return property.GetValue(target, null);

                var field = Reflect.Field(target.GetType(), member);
                if (field != null) return field.GetValue(target);
            }
            catch { }
            return null;
        }

        private static bool Flag(object target, string member)
        {
            var value = Read(target, member);
            return value is bool && (bool)value;
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

        private static bool Invoke(object target, string method, object argument)
        {
            if (!Alive.Is(target)) return false;

            bool ambiguous;
            var info = Reflect.MethodByName(target.GetType(), method, 1, out ambiguous);
            if (info == null) return false;

            info.Invoke(target, new object[] { argument });
            return true;
        }
    }
}
