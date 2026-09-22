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

Verified against Steam build `24989173` (Unity 6000.0.66f2,
`Assembly-CSharp.dll` sha256 `f95343c0…c07253`). See `generated/fingerprint.json`.

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

These live in `Unity.TextMeshPro.dll`, which moves with the **Unity version**, not
with Crux's code. It is the anchor group least likely to break on a game patch and
the one most likely to break on an engine upgrade.

F1 was confirmed present in the shipped DLL by decompilation, not assumed from
documentation. Whether it *works* at runtime in this build is M1's job.

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

C1 is a **dependency on a developer oversight**, not on an API. The cheat menu is
debug code that shipped; a build that strips it takes the keyboard shortcuts with
it. The widget is deliberately built on C2–C9 instead, so the loss is cosmetic.

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
| P7 | scoring speed | `CheatsHandler.SpeedUpScoring : bool` (public field) | unattended runs take real time | `autoplay/Supervisor.cs` |

P1 and P4 are read-only and public. P5 and P6 are where stage 3's difficulty lives —
not because the members are fragile, but because *when* it is safe to call
`ChangeToState` is not expressible as a signature.

## What watches this file

`tests/Anchors` reflects over an installed `Assembly-CSharp.dll` and asserts every
row above still resolves. It skips when no install is pointed at, so CI cannot run
it — the same gap the reference repo's bundle test has, for the same reason: the
game's own code cannot be committed.

```
COMBOLANDS_DIR="C:/Program Files (x86)/Steam/steamapps/common/Combolands" dotnet test
```

Run it after every game update, before opening a `game_update` issue.
