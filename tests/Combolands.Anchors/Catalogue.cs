using System.Collections.Generic;

namespace Combolands.Anchors
{
    // The anchor catalogue, in a form a machine can check.
    //
    // Every entry here has a row in docs/ANCHORS.md with the same id, and
    // CatalogueTests holds the two to each other - a row with no entry is an anchor
    // nobody checks, and an entry with no row is an anchor nobody documented.
    //
    // The kinds are asserted, not merely the names, because "it is a field, not a
    // property" has now broken this mod twice: ShopPanel.Exterior and
    // TargetCategory's members. A type that keeps the name and changes the shape
    // fails here loudly instead of failing in game quietly.
    //
    // `Bound = false` marks the anchors the mod does not look up: an asset layout, a
    // grammar the linter reimplements, a formula copied rather than called, a claim
    // about what one argument gates. Those are real dependencies and they are in the
    // documentation for good reason, but no probe can defend them, and one that
    // passed would only be answering a question nobody asked.
    internal static class Catalogue
    {
        private const string Localization = "Library.Localization.LocalizedStringAsset";
        private const string Behaviour = "Entities.BuildingBehaviours._BuildingBehaviour";
        private const string Behaviours = "Entities.BuildingBehaviours.BuildingBehaviours";
        private const string Buildings = "Entities.BuildingController";
        private const string Score = "GameState.ScoreController";
        private const string Placing = "Interaction.InteractionStates.PlacingBuilding";
        private const string Tmp = "Unity.TextMeshPro";
        private const string FontAsset = "TMPro.TMP_FontAsset";

        internal static IReadOnlyList<Anchor> All { get { return Anchors; } }

        private static readonly Anchor[] Anchors =
        {
            // --- language patch ---------------------------------------------------

            new Anchor
            {
                Id = "A1", What = "translation choke point",
                Type = Localization,
                Members = new[] { Member.Method("GetText", 0) },
            },
            new Anchor
            {
                Id = "A2", What = "translation key",
                Type = Localization,
                Members = new[] { Member.Field("Key") },
            },
            new Anchor
            {
                Id = "A3", What = "asset-name fallback",
                Type = "Entities.Data._BaseData",
                Members = new[] { Member.Property("Name") },
            },
            new Anchor
            {
                Id = "A4", What = "string dump",
                Type = Localization,
                Bound = false,
                // Not a member but a fact about the project: that the assets sit under
                // a Resources folder, where Resources.LoadAll can reach them. Nothing
                // in the assembly records that, and a build that moved them would pass
                // every check here.
                Why = "an asset layout, not a member - only i18n/Dump.cs can answer it",
            },
            new Anchor
            {
                Id = "A5", What = "lookup tables",
                Type = "Interaction.StringLookup",
                Bound = false,
                Why = "nothing looks it up - the lookup tables translate through A1 like everything else",
            },
            new Anchor
            {
                Id = "A6", What = "description assembly",
                Type = "Entities.Data.StringProcessor",
                Bound = false,
                Why = "a grammar tools/lint-locale.py reimplements; no member is ever bound",
            },

            // --- font -------------------------------------------------------------
            //
            // These live in Unity's TextMeshPro package, so they move with the engine
            // version rather than with Crux's code. The plugin compiles against them
            // directly rather than reflecting, so a break is a build error on a
            // machine that has the game - and this, which names which one, anywhere.

            new Anchor
            {
                Id = "F1", What = "runtime font creation",
                Assembly = Tmp, Type = FontAsset,
                Members = new[] { Member.Method("CreateFontAsset", 7) },
            },
            new Anchor
            {
                Id = "F2", What = "global fallback",
                Assembly = Tmp, Type = "TMPro.TMP_Settings",
                Members = new[] { Member.Property("fallbackFontAssets") },
            },
            new Anchor
            {
                Id = "F3", What = "per-font fallback",
                Assembly = Tmp, Type = FontAsset,
                Members = new[] { Member.Property("fallbackFontAssetTable") },
            },
            new Anchor
            {
                Id = "F4", What = "dynamic atlas",
                Assembly = Tmp, Type = FontAsset,
                Members = new[] { Member.Property("atlasPopulationMode") },
            },
            new Anchor
            {
                Id = "F5", What = "glyph size",
                Assembly = Tmp, Type = FontAsset,
                Members = new[] { Member.Property("faceInfo") },
            },

            // --- cheat ------------------------------------------------------------

            new Anchor
            {
                Id = "C1", What = "built-in cheats",
                Type = "Interaction.CheatsHandler",
                Bound = false,
                Why = "the game's own keyboard shortcut - the widget is built on C2-C9 instead",
            },
            new Anchor
            {
                Id = "C2", What = "economy",
                Type = Score,
                Members = new[]
                {
                    Member.Property("Score"), Member.Property("Money"),
                    Member.Property("Rerolls"), Member.Property("Removes"),
                    Member.Property("Dismisses"), Member.Property("Rewinds"),
                    Member.Property("Enchant"),
                },
            },
            new Anchor
            {
                Id = "C3", What = "economy writes",
                Type = Score,
                Members = new[]
                {
                    Member.Method("ChangeMoney", 3), Member.Method("ChangeScore", 1),
                    Member.Method("ChangeRerolls", 3), Member.Method("ChangeDismisses", 1),
                    Member.Method("ChangeRewinds", 1),
                },
            },
            new Anchor
            {
                Id = "C4", What = "run control",
                Type = "GameState.GameController",
                Members = new[]
                {
                    Member.Method("DebugChangeWeeks", 1), Member.Method("DebugSetTarget", 1),
                    Member.Method("AddExtraWeek", 0),
                },
            },
            new Anchor
            {
                Id = "C5", What = "placement bypass",
                Type = Buildings,
                Members = new[] { Member.Method("CanBuildBuildingAt", 5) },
            },
            new Anchor
            {
                Id = "C6", What = "spawning",
                Type = Buildings,
                // NOT InstantiateAndBuildBuildingAt, which the mod deliberately does
                // not call - P20 says why placing through the interaction state is the
                // failure worth having. This is what the build cheat does call.
                Members = new[] { Member.Method("SetCanPlaceNextBuildingAnywhere", 1) },
            },
            new Anchor
            {
                Id = "C7", What = "unlocks",
                Type = "Progression.UnlockStateController",
                Members = new[]
                {
                    Member.Method("DebugUnlockAllGuilds", 0),
                    Member.Method("DebugLevelUpAllGuilds", 0),
                },
            },
            new Anchor
            {
                Id = "C8", What = "milestone",
                Type = "GameState.MilestoneManager",
                Members = new[]
                {
                    Member.Method("DebugSetCurrentMilestone", 1),
                    Member.Method("EndCurrentMilestoneEarly", 0),
                },
            },
            new Anchor
            {
                Id = "C9", What = "shop",
                Type = "GameState.ShopManager",
                Members = new[] { Member.Method("DebugShowShop", 0) },
            },
            new Anchor
            {
                Id = "C10", What = "achievement gate",
                Type = "External.AchievementsHandler",
                // The one anchor whose failure is silent AND outward-facing: if this
                // moves, cheats keep working and Steam keeps taking achievements.
                Members = new[] { Member.Method("Achieve", 1) },
            },
            new Anchor
            {
                Id = "C11", What = "singleton access",
                Type = "Library.MonoSingleton`1",
                Members = new[] { Member.Property("Instance"), Member.Property("HasInstance") },
            },
            new Anchor
            {
                Id = "C12", What = "click guard",
                Type = "Library.Utils.UiUtils",
                Members = new[] { Member.Method("IsPointerOverUIObject", 0) },
            },

            // --- autoplay ---------------------------------------------------------

            new Anchor
            {
                Id = "P1", What = "the board",
                Type = "Environment.MapController",
                // Width, Height and GetTile are on whatever type Grid returns, which
                // the mod discovers rather than names - so they are checked the same
                // way, in AnchorTests.The_grid_still_answers.
                Members = new[] { Member.Property("Grid") },
            },
            new Anchor
            {
                Id = "P2", What = "legality",
                Type = Buildings,
                Members = new[] { Member.Method("CanBuildBuildingAt", 5) },
            },
            new Anchor
            {
                Id = "P3", What = "adjacency and range",
                Type = Buildings,
                Bound = false,
                // The road not taken: P11 is where the valuation gets range from, and
                // it copies the formula rather than asking per tile. Probing these
                // would also be a guess - IsTileInRangeOfBuilding has two overloads of
                // the same arity, which is exactly what Reflect refuses to bind.
                Why = "not bound - Snapshot.Piece.Covers computes range without asking the game",
            },
            new Anchor
            {
                Id = "P4", What = "placed pieces",
                Type = Buildings,
                Members = new[] { Member.Property("Buildings") },
            },
            new Anchor
            {
                Id = "P5", What = "state gate",
                Type = "Interaction.InteractionController",
                // Reading only. ChangeToState, MouseCoords and CurrentlyTargeting are
                // in the documentation as what a state-DRIVING autoplay would need;
                // this one never drives a state, it waits for one.
                Members = new[]
                {
                    Member.Property("CurrentInteractionState"),
                    Member.Property("PlacingBuilding"), Member.Property("Shopping"),
                },
            },
            new Anchor
            {
                Id = "P6", What = "the 34 states",
                // The ones the loop asks about by name, not all thirty-four. A state
                // nothing reads can be renamed for free.
                Type = Placing,
                Members = new[]
                {
                    Member.Method("OnEnter", 1),
                    Member.Method("ExitEffectTargetingState", 0,
                        on: "Interaction.InteractionStates._BuildingUtilityTargetingState"),
                },
            },
            new Anchor
            {
                Id = "P7", What = "scoring speed",
                Type = "Interaction.CheatsHandler",
                Members = new[] { Member.Field("SpeedUpScoring") },
            },
            new Anchor
            {
                Id = "P8", What = "the held piece",
                Type = Placing,
                Members = new[] { Member.Field("_currentlyPlacing") },
            },
            new Anchor
            {
                Id = "P9", What = "tiles",
                Type = "Environment.Tile",
                Members = new[]
                {
                    Member.Property("X"), Member.Property("Y"), Member.Property("Type"),
                    Member.Property("IsEmpty"), Member.Property("CantBuildOn"),
                },
            },
            new Anchor
            {
                Id = "P10", What = "piece shape",
                // Building redeclares GamePiece.Behaviour with `new`, which is what
                // Reflect.cs exists for - so binding these THROUGH Reflect is the
                // point of this check rather than an implementation detail of it.
                Type = "Entities.Building",
                Members = new[]
                {
                    Member.Property("X"), Member.Property("Y"), Member.Property("Range"),
                    Member.Property("Tag"), Member.Property("Categories"),
                },
            },
            new Anchor
            {
                Id = "P11", What = "range shape",
                Type = "Library.Grid.GridDrawingAlgorithms",
                Bound = false,
                // The one anchor the mod COPIES rather than calls, so no probe can
                // defend it: the method could keep its name, change its formula, and
                // everything here would pass while every highlight went subtly wrong.
                Why = "copied into Snapshot.Piece.Covers, never called - only reading the method answers it",
            },
            new Anchor
            {
                Id = "P12", What = "declared targets",
                // What makes a suggestion worth following: the game saying, in its own
                // numbers, what a building is looking for. If these move the helper
                // keeps working and quietly gets much worse, which is why they are
                // checked rather than trusted.
                Type = "Entities.GamePiece",
                Members = new[]
                {
                    Member.Property("TargetTags"),
                    Member.Method("GetScoreForTag", 2, on: "Entities._GamePieceBehaviour"),
                    Member.Property("TargetCategories", on: "Entities.GamePieceLocalValues"),
                    Member.Method("GetScoreForTargetCategory", 1, on: "Entities.GamePieceLocalValues"),
                },
            },
            new Anchor
            {
                Id = "P13", What = "same-type rule",
                Type = Behaviour,
                Members = new[] { Member.Method("HasRangePlacementRestriction", 1) },
            },
            new Anchor
            {
                Id = "P14", What = "rule escape hatch",
                Type = Buildings,
                Bound = false,
                // The method is C5 and is checked there. This row is about a fact no
                // signature carries: that the flag gates exactly one `if`. A refactor
                // widening it would make the helper suggest illegal tiles while
                // compiling, running and passing every check here.
                Why = "a claim about what one argument gates, which C5's signature cannot express",
            },
            new Anchor
            {
                Id = "P15", What = "board changed",
                Type = "Entities.BuildingExtensions",
                Members = new[] { Member.Method("ResetCaches", 0) },
            },
            new Anchor
            {
                Id = "P16", What = "what is on offer",
                Type = "UI.BuildingChoiceBar",
                Members = new[]
                {
                    Member.Property("Choices"),
                    Member.Property("GameTag", on: "UI.BuildingChoiceButton"),
                },
            },
            new Anchor
            {
                Id = "P17", What = "base stats, no instance",
                // Private fields, deliberately. The public accessors all take a
                // GamePiece and the overrides dereference it, so asking them about a
                // building that does not exist yet means passing null and hoping.
                Type = Behaviour,
                Members = new[]
                {
                    Member.Field("_range"), Member.Field("_majorCategory"),
                    Member.Field("_minorCategories"),
                    Member.Method("GetBehaviourTargetTags", 0),
                    Member.Method("GetBehaviourTargetCategories", 0),
                    Member.Method("GetScoreForTag", 2),
                    Member.Property("BuildingBehaviourDict", on: Behaviours),
                },
            },
            new Anchor
            {
                Id = "P18", What = "terrain by tag",
                Type = Behaviour,
                Members = new[] { Member.Method("GetTileTypesCanBePlacedOn", 1) },
            },
            new Anchor
            {
                Id = "P19", What = "piece name",
                Type = "Entities.GamePieceDataHolder",
                Members = new[] { Member.Method("GetDataFor", 1) },
            },
            new Anchor
            {
                Id = "P20", What = "placing, for real",
                Type = Placing,
                Members = new[]
                {
                    Member.Method("OnUpdate", 4), Member.Method("PlaceCurrentBuilding", 1),
                    Member.Field("_canPlaceCurrentBuilding"),
                },
            },
            new Anchor
            {
                Id = "P21", What = "choosing, for real",
                Type = "UI.BuildingChoiceButton",
                Members = new[] { Member.Method("OnPointerClick", 1) },
            },
            new Anchor
            {
                Id = "P22", What = "is it on the board",
                Type = "Entities.Building",
                Members = new[] { Member.Property("Tile") },
            },

            // --- the simulator ----------------------------------------------------
            //
            // Read once, into a rule set that is then used with no game present. A
            // break here does not stop autoplay; it stops the next dump, and the mod
            // falls back to the proximity valuation saying so in the log.

            new Anchor
            {
                Id = "M1", What = "the score itself",
                Type = Behaviour,
                Bound = false,
                // Reproduced in sim/Preview.cs, never called - calling it needs a live
                // building on a live tile, which is the thing a planner cannot afford.
                // So it can keep its name, change its arithmetic, and every check here
                // passes while every number the helper prints is quietly wrong.
                Why = "reproduced rather than called - only reading GetScorePreview answers it",
            },
            new Anchor
            {
                Id = "M2", What = "which neighbourhood",
                Type = Behaviour,
                Members = new[] { Member.Field("_scorePreviewMode") },
            },
            new Anchor
            {
                Id = "M3", What = "how often it pays",
                Type = Behaviour,
                Members = new[] { Member.Field("_cooldownParam") },
            },
            new Anchor
            {
                Id = "M4", What = "what it pays for",
                Type = Behaviour,
                Members = new[]
                {
                    Member.Method("GetBehaviourTargetTags", 0),
                    Member.Method("GetBehaviourTargetCategories", 0),
                    Member.Method("GetBehaviourTargetRarities", 0),
                    Member.Method("GetBehaviourTargetTileTypes", 0),
                    Member.Method("GetScoreForTag", 2),
                    Member.Method("GetScoreForRarity", 1),
                    Member.Method("GetScoreForTileType", 1),
                    Member.Method("GetScoreParam", 1),

                    // The struct the first dump could not read. Fields, not an enum,
                    // and the reason every category in the game scored zero.
                    Member.Field("GamePieceCategory", on: "Entities.TargetCategory"),
                    Member.Field("Score", on: "Entities.TargetCategory"),
                },
            },
            new Anchor
            {
                Id = "M5", What = "the multiplier",
                Type = "Entities.GamePiece",
                Members = new[] { Member.Property("Multiplier") },
            },
            new Anchor
            {
                Id = "M6", What = "the same-type rule, statically",
                // The FIELD. The method of the same name ends at it, and everything
                // above that return is an exemption granted by run state.
                Type = Behaviour,
                Members = new[] { Member.Field("_hasRangePlacementRestriction") },
            },

            new Anchor
            {
                Id = "M7", What = "activation",
                Type = Behaviour,
                Members = new[]
                {
                    Member.Field("_activationCount"),
                    Member.Field("_canBeActivated"),
                },
            },
            new Anchor
            {
                Id = "M8", What = "which buildings activate",
                Type = Behaviour,
                Bound = false,
                // A behaviour activates by CALLING AddOnActivatedTrigger from its
                // Activate override. No field says so and no signature implies it, so
                // the dumper carries a list of names - and checks at dump time that
                // each still exists, which is the only guard available.
                Why = "a list read from the game's code, not from its data - the dump reports names that vanish",
            },

            // --- autoplay's screens -----------------------------------------------

            new Anchor
            {
                Id = "S1", What = "milestone summary",
                Type = "UI.MilestoneScreen.MilestoneScreen",
                Members = new[] { Member.Property("WaitingForClick"), Member.Method("ProcessClick", 0) },
            },
            new Anchor
            {
                Id = "S2", What = "modal dialogs",
                Type = "Shared.UI.MessageDialog",
                Members = new[] { Member.Property("IsShown"), Member.Method("Close", 0) },
            },
            new Anchor
            {
                Id = "S3", What = "start-of-milestone pack",
                Type = "UI.StartOfMilestonePack",
                Members = new[] { Member.Method("OnPointerClick", 1) },
            },
            new Anchor
            {
                Id = "S4", What = "choosing a council request",
                Type = "UI.Quests.QuestSelectionPanel",
                Members = new[]
                {
                    Member.Method("SelectQuest", 1), Member.Field("_selected"),
                    Member.Method("Confirm", 0), Member.Method("PressRerollButton", 0),
                    Member.Property("Quest", on: "UI.Quests.CouncilQuestOptionButton"),
                },
            },
            new Anchor
            {
                Id = "S5", What = "pack contents",
                Type = "UI.PackSelectionPanel",
                Members = new[]
                {
                    Member.Field("_options"), Member.Method("SkipSelection", 0),
                    Member.Method("OnPointerClick", 1, on: "UI.Shop.ShopItem"),
                },
            },
            new Anchor
            {
                Id = "S6", What = "leaving the shop",
                // Exterior is a FIELD. Asking for it as a property was writing a
                // warning several times a second about something that worked.
                Type = "UI.ShopPanel",
                Members = new[]
                {
                    Member.Field("Exterior"), Member.Method("FinishShopping", 0),
                    Member.Property("IsShown", on: "UI.Shop.ShopExterior"),
                    Member.Field("_currentSkipReward", on: "UI.Shop.ShopExterior"),
                    Member.Method("SkipShop", 0, on: "UI.Shop.ShopExterior"),
                    Member.Method("EnterShop", 0, on: "UI.Shop.ShopExterior"),
                },
            },
            new Anchor
            {
                Id = "S7", What = "claiming a council reward",
                Type = "UI.Quests.CouncilQuestTextArea",
                Members = new[]
                {
                    Member.Field("_currentQuests"), Member.Field("_textBlock"),
                    Member.Method("PressHeaderButton", 0),
                    Member.Method("PressClaimRewardButton", 0),
                    Member.Method("PressSkipRewardButton", 0),
                },
            },
        };
    }
}
