---
layout: default
title: "Anchor catalogue"
description: "What the mod patches, what breaks when the game moves it, and where to fix it."
lang: en
---

# Anchor catalogue

This mod attaches to **type and member signatures inside `Assembly-CSharp.dll`**.
A game update can move any of them. This table collects, in one place, what breaks
and where to fix it.

Verified against Steam build `24989173` / game `v1.0.6` (Unity 6000.0.66f2,
`Assembly-CSharp.dll` sha256 `f95343c0…c07253`). See `generated/fingerprint.json`.

**Every row below resolved against the running game on 2026-09-22: 34 members, 0
broken.** Not read off a decompiler — reflected out of the live Mono domain by the
M0 probe, with `Assembly-CSharp`, `Assembly-CSharp-firstpass` and
`Unity.TextMeshPro` all loaded before the first scene.

## Why these anchors are sturdier than a JS bundle's

The build is **Mono with no obfuscation**. Namespaces, type names, method names and
parameter names all survive into the shipped DLL — there is no minification step to
shorten them and no renamer pass to scramble them. An anchor only breaks when Crux
actually changes the code, not when they rebuild it.

That also sets the real failure mode: not renaming, but **reshaping**. If the
localisation scaffold is replaced with `com.unity.localization`, A1–A4 break
together and the language patch is rewritten rather than repaired.

## Language patch

| # | Anchor | Signature | Breaks | Fix in |
|---|---|---|---|---|
| A1 | translation choke point | `Library.Localization.LocalizedStringAsset.GetText() : string` | **every translation** | `i18n/Patches.cs` |
| A2 | translation key | `LocalizedStringAsset.Key : string` (public field) | every translation | `i18n/Patches.cs`, `i18n/Dump.cs` |
| A3 | asset-name fallback | `Entities.Data._BaseData.Name : string` (getter) | pieces with no `LocalizedString` show English file names | `i18n/Patches.cs` |
| A4 | string dump | `Resources.LoadAll<LocalizedStringAsset>("")` — i.e. the assets staying under `Resources` | extraction; translation still works | `i18n/Dump.cs` |
| A5 | lookup tables | `Interaction.StringLookup` (`ScriptableObjectSingleton`) | categories, rarities, tile types, quest rewards stay English | `i18n/Patches.cs` |
| A6 | description assembly | `Entities.Data.StringProcessor` — the whitespace split and `[`/`{` handling | token rules in `lint-locale.py` become wrong | `tools/lint-locale.py` |

A1 is the one that matters. It is a public no-argument method on a public class and
everything else in the language patch is cleanup around it.

A6 is not patched — it is *depended on*. The linter encodes its parsing rules, so if
`StringProcessor` changes how it splits, the linter starts enforcing a rule the game
no longer has. That failure is silent in both directions; re-read the method when
the game updates.

## Font

| # | Anchor | Signature | Breaks | Fix in |
|---|---|---|---|---|
| F1 | runtime font creation | `TMPro.TMP_FontAsset.CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode, int atlasWidth, int atlasHeight)` | **all Hangul renders as `□□□`** | `i18n/Font.cs` |
| F2 | global fallback | `TMPro.TMP_Settings.fallbackFontAssets : List<TMP_FontAsset>` (public static, get **and set**) | same | `i18n/Font.cs` |
| F3 | per-font fallback | `TMP_FontAsset.fallbackFontAssetTable : List<TMP_FontAsset>` (public) | Hangul missing on fonts that bypass the global list | `i18n/Font.cs` |
| F4 | dynamic atlas | `TMP_FontAsset.atlasPopulationMode` = `AtlasPopulationMode.Dynamic` | glyphs never rasterise | `i18n/Font.cs` |
| F5 | glyph size | `TMP_FontAsset.faceInfo` (`FaceInfo.scale`, settable through the property) | Hangul renders far larger than the game's Latin | `i18n/Font.cs` |

These live in `Unity.TextMeshPro.dll`, which moves with the **Unity version**, not
with Crux's code. It is the anchor group least likely to break on a game patch and
the one most likely to break on an engine upgrade.

F1 is not one method. The build carries **six** `CreateFontAsset` overloads, and
they give the font work two independent routes:

```
CreateFontAsset(string familyName, string styleName, int pointSize = 90)   ← OS font
CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize,
                int atlasPadding, GlyphRenderMode, int atlasWidth, int atlasHeight)
CreateFontAsset(…, AtlasPopulationMode, bool enableMultiAtlasSupport = true)
CreateFontAsset(Font font)                                                 ← ×3 overloads
```

The file-path pair is the plan: ship the TTF, no dependence on the player having a
Korean font installed. The `familyName` overload is the fallback if loading from a
path is refused at runtime, and it would mean falling back to Malgun Gothic on the
player's own machine — a worse look, but not a dead end.

**M1 called one and it worked.** `CreateFontAsset(path, 0, 48, 9, SDFAA, 1024, 1024)`
returned a Galmuri11 asset in `Dynamic` population mode, `TryAddCharacters` added
300 of 300 Hangul syllables with none missing, and the atlas spilled to a second
1024×1024 `Alpha8` texture on its own — so F4 and multi-atlas both behave.

F5 is why the patch is legible. `TMP_Text` sizes each glyph as
`m_currentFontSize / faceInfo.pointSize * faceInfo.scale`, reading `faceInfo` from
whichever asset supplied that glyph — so setting `scale` on the **fallback** asset,
and only there, shrinks Korean without touching the game's own text. At 1.0 Hangul
overflowed buttons; 0.8 matches.

One thing the table cannot express: **F3 must be applied per scene, not once.** Only
five TMP fonts are loaded at the menu; `monogram-extended SDF` and
`THEBOLDFONT-FREEVERSION SDF` arrive with the game scene. A single startup pass
leaves those two without Hangul.

## Cheat

| # | Anchor | Signature | Breaks | Fix in |
|---|---|---|---|---|
| C1 | built-in cheats | `Interaction.CheatsHandler`, `_cheatsEnabled : bool` (private field), live in `level3` | the shortcut; the widget still works | `cheat/Widget.cs` |
| C2 | economy | `GameState.ScoreController` — `Score : long`, `Money : int`, `Rerolls`, `Removes`, `Dismisses`, `Rewinds : int`, `Enchant : float` (private setters) | resource cheats | `cheat/Economy.cs` |
| C3 | economy writes | `ScoreController.ChangeMoney(int, GamePiece, bool)`, `ChangeScore(long)`, `ChangeRerolls(int, GamePiece, bool)`, `ChangeDismisses(int)`, `ChangeRewinds(int)` | resource cheats | `cheat/Economy.cs` |
| C4 | run control | `GameState.GameController.DebugChangeWeeks(int)`, `DebugSetTarget(int)`, `AddExtraWeek()`, `AddGuildToSelectedGuilds(GamePieceCategory)` | week and target cheats | `cheat/Run.cs` |
| C5 | placement bypass | `Entities.BuildingController.CanBuildBuildingAt(Building, int, int, out CantPlaceBuildingReason, bool ignoreAllPlacementRestrictions)` | "build anywhere" | `cheat/Build.cs` |
| C6 | spawning | `BuildingController.InstantiateAndBuildBuildingAt(GameTag, int, int, bool, bool, GamePiece)` | spawning, and autoplay stage 2 | `cheat/Build.cs`, `autoplay/Exec.cs` |
| C7 | unlocks | `Progression.UnlockStateController.DebugUnlockAllGuilds()`, `DebugLevelUpAllGuilds()` | unlock cheats | `cheat/Unlocks.cs` |
| C8 | milestone | `GameState.MilestoneManager.DebugSetCurrentMilestone(int)`, `EndCurrentMilestoneEarly()` | milestone skip | `cheat/Run.cs` |
| C9 | shop | `GameState.ShopManager.DebugShowShop()` | forcing the shop | `cheat/Run.cs` |
| C10 | achievement gate | `External.AchievementsHandler.Achieve(Achievement)` (private, 1 arg) | **achievements would submit while cheating** | `cheat/Integrity.cs` |
| C11 | singleton access | ``Library.MonoSingleton`1`` / ``Library.SerializedMonoSingleton`1`` — `Instance`, `HasInstance` | every cheat, and autoplay's reads | `Singletons.cs` |
| C12 | click guard | `Library.Utils.UiUtils.IsPointerOverUIObject()` | clicks on the panel also place buildings | `cheat/Widget.cs` |

C1 is a **dependency on a developer oversight**, not on an API, and it works:
`RightShift`+`C`+`L` arms it in the retail build, and `+G` and `+B` were confirmed
in game on 2026-09-22. The cheat menu is debug code that shipped; a build that
strips it takes the keyboard shortcuts with it. The widget is deliberately built on C2–C9 instead, so the loss is cosmetic.

C10 is the only anchor whose failure is **silent and outward-facing**. If `Achieve`
moves, cheats keep working and Steam keeps receiving achievements — so the miss is
logged as an error rather than a warning, and it is the one to check first after a
game update.

C11 is reached generically: static members of a generic type are per constructed
type, so `MonoSingleton<ScoreController>.Instance` really does reach the live one.
`HasInstance` is checked first every time, because `Instance` logs a Unity error
when the singleton is missing and a panel drawn on the main menu would otherwise
write one error per control per frame.

C5's `ignoreAllPlacementRestrictions` already being an optional parameter is why a
prefix forcing it true is the whole feature. If that parameter is removed, the
bypass has to be rebuilt against the method body.

## Autoplay

| # | Anchor | Signature | Breaks | Fix in |
|---|---|---|---|---|
| P1 | the board | `Environment.MapController` — `Grid : Grid<Tile>`, `Width`, `Height`, `IsTileInCantBuildArea(int, int)` | **all of autoplay** | `autoplay/Board.cs` |
| P2 | legality | `BuildingController.CanBuildBuildingAt(…)` (= C5) | all of autoplay | `autoplay/Board.cs` |
| P3 | adjacency and range | `BuildingController.GetBuildingsWhoseRangesOverlapTile(Tile)`, `IsTileInRangeOfCategory(Tile, GamePieceCategory)`, `IsTileInRangeOfBuilding(Tile, GameTag)` | valuation quality | `autoplay/Value.cs` |
| P4 | placed pieces | `BuildingController.Buildings : List<Building>` | valuation quality | `autoplay/Board.cs` |
| P5 | state gate | `Interaction.InteractionController.CurrentInteractionState`, `ChangeToState(InteractionState, params object[])`, `MouseCoords`, `CurrentlyTargeting` | stages 2–3 | `autoplay/State.cs` |
| P6 | the 34 states | `Interaction.InteractionStates.*` — `PlacingBuilding`, `Shopping`, `SelectingQuest`, `OpeningPack`, `CompletingMilestone`, … | stage 3 | `autoplay/State.cs` |
| P7 | scoring speed | `CheatsHandler.SpeedUpScoring : bool` (public field) | unattended runs take real time | `cheat/Run.cs` |
| P8 | the held piece | `Interaction.InteractionStates.PlacingBuilding._currentlyPlacing` (private field) | **the helper never has anything to suggest** | `autoplay/State.cs` |
| P9 | tiles | `Environment.Tile` — `IsEmpty`, `CantBuildOn` | the helper suggests occupied tiles | `autoplay/Board.cs` |
| P10 | piece shape | `Entities.Building` — `X`, `Y`, `Range`, `Tag`, `Categories` | valuation quality | `autoplay/Board.cs` |
| P15 | board changed | `Entities.BuildingExtensions.ResetCaches()` | suggestions go stale until the held building changes | `autoplay/Overlay.cs` |
| P16 | what is on offer | `UI.BuildingChoiceBar.Choices` · `UI.BuildingChoiceButton.GameTag` | **scouting stops entirely** | `autoplay/Offers.cs` |
| P17 | base stats, no instance | `_BuildingBehaviour._range` · `_majorCategory` · `_minorCategories` · `BuildingBehaviours.Instance.BuildingBehaviourDict` | scouting stops | `autoplay/Offers.cs` |
| P18 | terrain by tag | `_BuildingBehaviour.GetTileTypesCanBePlacedOn(GameTag)` · `Environment.Tile.Type` | scouting points at ocean | `autoplay/Offers.cs`, `autoplay/Rules.cs` |
| P20 | placing, for real | `PlacingBuilding.OnUpdate(Vector3, Vector2Int, Tile, bool)` · `PlaceCurrentBuilding(Vector2Int)` · `_canPlaceCurrentBuilding` | **autoplay cannot place anything** | `autoplay/Exec.cs` |
| P21 | choosing, for real | `UI.BuildingChoiceButton.OnPointerClick(PointerEventData)` | autoplay places but never picks | `autoplay/Exec.cs` |
| P22 | is it on the board | `Entities.Building.Tile` | autoplay throws whenever the cursor leaves the window | `autoplay/Exec.cs` |
| P19 | piece name | `Entities.GamePieceDataHolder.GetDataFor(GameTag)` → `_BaseData.Name` | the panel lists tags instead of names | `autoplay/Offers.cs` |
| P11 | range shape | ``Library.Grid.GridDrawingAlgorithms.GetFilledCircle`` — `dx*dx + dy*dy < r*r + r` | every highlight is subtly wrong | `autoplay/Snapshot.cs` |
| P12 | declared targets | `GamePiece.TargetTags` · `_GamePieceBehaviour.GetScoreForTag` · `GamePieceLocalValues.TargetCategories` · `GetScoreForTargetCategory` | **the helper stops knowing what a building wants** and falls back to bare adjacency | `autoplay/Board.cs` |
| P13 | same-type rule | `_BuildingBehaviour.HasRangePlacementRestriction(GamePiece)` | the helper suggests tiles the game refuses | `autoplay/Board.cs`, `autoplay/Rules.cs` |
| P14 | rule escape hatch | `CanBuildBuildingAt(..., bool ignoreAllPlacementRestrictions)` gating **only** the same-type check | the helper has to trust a stale cache | `autoplay/Board.cs` |

P12 is what makes a suggestion worth following. Everything else in the valuation is
a proxy; these four are the game telling us, in its own numbers, what a building is
looking for. If they move, the helper keeps working and quietly gets much worse —
which is the failure mode worth watching for, because nothing breaks.

P14 is a dependency on a **precise** fact rather than a general one: that argument
gates one `if` and no others. A refactor that widened it to cover more checks would
silently make the helper suggest illegal tiles, and it would still compile, run and
look fine. Re-read `CanBuildBuildingAt` after a game update.

P17 is a dependency on **private fields**, which is unusual here and deliberate.
The public accessors all take a `GamePiece`, and the overrides dereference it — so
asking them about a building that does not exist yet means passing `null` and hoping.
The fields are the base values, which is exactly what a building not yet on the board
is worth.

P18 is the reason scouting is honest rather than approximate about terrain: it takes
a tag. Nineteen behaviours override `CanBeBuiltOn` with rules that need an instance,
and those stay unevaluated until the player picks the building up.

P11 is the one anchor this mod **copies rather than calls**. Asking the game whether
each of 1,188 tiles is in range of each building would be tens of thousands of
reflection calls a frame, so the shape is reproduced in `Piece.Covers` and pinned by
tests. That makes it the one place where the game can change without anything
failing — the helper would simply start drawing confidently wrong highlights. Re-read
`GetFilledCircle` after a game update; nothing else will tell you.

P1 and P4 are read-only and public. P5 and P6 are where stage 3's difficulty lives —
not because the members are fragile, but because *when* it is safe to call
`ChangeToState` is not expressible as a signature.

P20 is the one anchor whose failure is **destructive rather than merely wrong**, and
that is why it is used at all. The obvious alternative,
`BuildingController.InstantiateAndBuildBuildingAt`, would keep working after a
refactor and quietly stop doing half the job. A missing `PlaceCurrentBuilding` stops
autoplay dead, which is the failure worth having.

## Autoplay's screens

| # | Anchor | Signature | Breaks | Fix in |
|---|---|---|---|---|
| S1 | milestone summary | `UI.MilestoneScreen.MilestoneScreen.WaitingForClick` · `ProcessClick()` | **autoplay stops at every milestone** | `autoplay/Screens.cs` |
| S2 | modal dialogs | `Shared.UI.MessageDialog.IsShown` · `Close()` | autoplay stops at the first dialog | `autoplay/Screens.cs` |
| S3 | start-of-milestone pack | `UI.StartOfMilestonePack` (click handler) | autoplay stops after every milestone | `autoplay/Screens.cs` |
| S4 | council request | `UI.Quests.CouncilQuestOptionButton` (click) · `QuestSelectionPanel.Confirm()` | same | `autoplay/Screens.cs` |
| S5 | pack contents | `UI.PackSelectionPanel._options` · `UI.Shop.ShopItem` (click) | autoplay stops on an open pack | `autoplay/Screens.cs` |
| S6 | leaving the shop | `UI.ShopPanel.Exterior` (a FIELD) · `ShopExterior.IsShown` · `_currentSkipReward` · `SkipShop()` | autoplay stops at the shop | `autoplay/Screens.cs` |

S6 carries a precondition the game does not check for itself. `SkipShop` dereferences
`_currentSkipReward` on its first line and nulls it on its last, while `IsShown` is
cleared later by a coroutine — so calling it twice throws, and once it has thrown the
loop never gets past `Screens.Handle` again. That is why the mod reads the private
field before calling, and why any successful screen action is followed by a settle.

## The hazard that is not in the table

`Entities.Building` redeclares `GamePiece.Behaviour` with `new`, and the game does
this in more than one place. `Type.GetProperty(name, FlattenHierarchy)` finds both
declarations and throws `AmbiguousMatchException` — which, inside a `Log.Guard`,
disabled the placement helper for a whole session while looking on screen like it
simply did nothing.

Every lookup in this mod therefore goes through `Reflect.cs`, which walks the
hierarchy from the most derived type with `DeclaredOnly`. **Do not reach for
`GetProperty`, `GetField` or `GetMethod` directly.** The shapes that broke it are
pinned in `tests/ReflectTests.cs`.

Two more traps of the same kind, both of which disabled a feature in silence:

**A destroyed Unity object is not `null`.** The `==` overload that makes it look null
is chosen by the static type, and reflection returns `object`. Use `Alive.Is(...)`,
never `x == null`, on anything that came out of the game.

**`TargetCategory` is a struct of public FIELDS**, not properties. Asking
`Anchors.Property` for `GamePieceCategory` found nothing, and every
category-targeting building scouted as though it wanted nothing - a warning in the
log and a silently worse answer on screen. When a member is not where you expect,
check which *kind* it is before assuming the name moved.

## What watches this file

Two things, at different costs.

`tests/Anchors` reflects over an installed `Assembly-CSharp.dll` without launching
anything. It skips when no install is pointed at, so CI cannot run it — the same gap
the reference repo's bundle test has, for the same reason: the game's own code cannot
be committed.

```
COMBOLANDS_DIR="C:/Program Files (x86)/Steam/steamapps/common/Combolands" dotnet test
```

The **M0 probe** is the stronger check and the more expensive one. It runs as a
MelonMod inside the live game, so it also proves the loader still boots and the
assemblies are reachable from a mod's perspective — things a static reflection pass
cannot tell you. It writes the table above with the resolved signature beside each
row, which is how you see that a method survived but *changed shape*.

Run the static test after every game update. Run the probe when it disagrees with
you, or when the loader is what you suspect.
