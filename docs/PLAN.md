---
layout: default
title: "Build plan"
description: "What this mod is, what it attaches to, and the order the work happens in."
lang: en
---

# Build plan

An unofficial mod for the Steam roguelike citybuilder *Combolands* (Crux Games,
AppID 4075620): a **Korean language patch**, a **cheat widget**, and an
**autoplay / play helper**.

This document fixes the decisions. `CONVENTIONS.md` says where files go,
`ANCHORS.md` says what breaks when the game updates.

## The game, as measured

Everything here was read out of the installed build, not assumed.

| | |
|---|---|
| Engine | Unity **6000.0.66f2** (Unity 6 LTS) |
| Scripting backend | **Mono** (`MonoBleedingEdge/`) — not IL2CPP |
| Obfuscation | none. Type and member names are intact |
| Game code | `Assembly-CSharp.dll`, 928 KB, 802 types |
| Text UI | TextMeshPro, plus Febucci TextAnimator |
| Serialisation | Odin (`SerializedScriptableObject`) |
| Scenes | `level0` boot · `level3` the game |
| Steam languages | **English only** |

Mono with no obfuscation is the easy case: Harmony can patch anything, and the
decompiled source reads like the original.

## Three decisions, and why

### 1. MelonLoader, not BepInEx

Both ship Harmony and both target Unity Mono. MelonLoader wins on the thing that
dominates this project's cost — the reversing loop. Its console, its log, and the
`UnityExplorer.ML` build are one install, and `MelonPreferences` is a config system
we do not have to write.

BepInEx 5 ships smaller (~3 MB zip against MelonLoader's installer) and is the more
battle-tested Mono loader. It was the fallback, then BepInEx 6 bleeding-edge
(`BepInEx.Unity.Mono`) behind it.

**Settled at M0, in the running game.** MelonLoader v0.7.3 identifies the build as
`MonoBleedingEdge` / `x64`, picks its `net35` runtime path, loads its Mono support
module, and runs a mod:

```
Game Type: MonoBleedingEdge        Unity Version: 6000.0.66f2
Runtime Type: net35                Game Version: v1.0.6
Mono: 6.13.0     CLR: 4.0.30319.42000     Harmony: 2.10.2.0
```

A mod built against `MelonLoader/net472/MelonLoader.dll` loads and runs under it, so
that is what `src/` targets. The fallbacks are not needed and are not carried.

### 2. One Harmony postfix carries the whole translation

The game already has a localisation scaffold. Every translatable string is a
`LocalizedStringAsset` — a ScriptableObject with a `Key` and the English text:

```csharp
// Library/Localization/LocalizedStringAsset.cs
public class LocalizedStringAsset : SerializedScriptableObject {
    public string Key;
    private string _originalString;
    public string GetText() => _originalString;
}
```

`GetText()` is the single choke point. A postfix on it that swaps in `ko.json[Key]`
translates everything routed through localisation, and `Key` — not text, not asset
order — is the join, so the translation survives the game rewording its English.

That the game ships `_isDirty`, `_isNew` and `_ignoreForTranslation` flags on that
asset says Crux built this expecting to localise. We are using the door they left.

### 3. Korean ships first, the layout does not assume it

`locale/<lang>/strings.json`, from the first commit. Korean is the only language in
the repository, but nothing in the code, the docs site, or the release archive names
it as the only possible one. Adding a language later must not be a refactor.

## Language patch

### Three patch sites, not one

The postfix on `GetText()` is the main one. Two others exist because text leaks
around it:

```csharp
// Entities.Data/_BaseData.cs  — a piece with no LocalizedString shows its FILE NAME
public string Name {
    get {
        if (_name != null && !_name.GetText().IsNullOrWhitespace()) return _name.GetText();
        return base.name;                    // ← untranslated, and not a Key
    }
}
```

and `Interaction/StringLookup.cs`, a `ScriptableObjectSingleton` holding the
dictionaries for categories, rarities, tile types and quest rewards. Those route
through `LocalizedString` and should be covered, but they are the first thing to
check when something stays English.

Hardcoded TMP text set directly on scene objects is the residue. UnityExplorer's
scene tree finds it; there is no shortcut.

### Extraction is a runtime dump, not an asset ripper

`LocalizedString.GetAssetList()` calls `Resources.LoadAll<LocalizedStringAsset>("")`,
so the game itself will hand over every string:

```csharp
var all = Resources.LoadAll<LocalizedStringAsset>("");
// → generated/strings.en.json   { Key: englishText }
```

AssetRipper and UABEA both have to guess at Odin's serialised layout. The game does
not have to guess. The dump ships as a dev-only command in the plugin.

**Run at M1, this is the real scale:**

```
1088 assets   1088 unique keys   0 blank   0 duplicate
6552 words    longest string 219 chars
 426 strings (39%) contain [tokens]   236 distinct token names
```

Keys are `<AssetName>.<Field>`: 441 `.Name`, 374 `.Description`, 18 `.Desc`,
12 `.Title`, and a handful of one-offs. A scan of `resources.assets` had suggested
"500–700 strings, ~2,100 words" — that estimate was **low by roughly 3×**, because
the byte scan could not see strings that Odin had laid out differently.

Nothing extracted ever gets committed — see `.gitignore`.

### The font worked

M1 is done. `CreateFontAsset` built a Galmuri11 asset from a TTF **file path** at
runtime, rasterised Hangul into a dynamic atlas, and the game's own fonts fell back
to it. Korean renders in the retail build:

```
family        Galmuri11 / Regular      population  Dynamic
pointSize     48   lineHeight 64       renderMode  SDFAA
requested     300 Hangul from U+AC00   added 300   missing 0
atlasTextures 2                        atlas[0] 1024x1024 Alpha8
fallback      TMP_Settings + all 5 loaded TMP fonts
```

Three things that only running it could tell us:

**Hangul renders too big at 1:1.** TMP sizes every glyph as
`fontSize / faceInfo.pointSize * faceInfo.scale`, reading those from whichever asset
supplied the glyph. Hangul fills its em box and Latin does not, so at 1.0 the Korean
towered over the game's own face and buttons wrapped mid-word. `faceInfo.scale` on
the fallback asset is the dial; **0.8** is the default and `FontScale` exposes it.

**Multi-atlas spill starts early.** 300 syllables at 48 pt already needed a second
1024×1024 texture — about 225 glyphs per atlas. A UI that touches 1,500 distinct
syllables would hold 6–7 atlases, roughly 6–7 MB of `Alpha8`. Acceptable, but
`samplingPointSize` is the dial: Galmuri11 is an 11-pixel face and the game draws it
small, so dropping 48 → 24 quarters the footprint. M2 tunes it against how it looks.

**Fallback registration has to run per scene.** At the menu only five TMP fonts were
loaded — `LiberationSans SDF`, its `- Fallback`, `Ignore 17 SDF`, `m6x11plus SDF`,
`Inconsolata-SemiBold SDF`. `monogram-extended SDF` and `THEBOLDFONT-FREEVERSION SDF`
live in the game scene's assets and were not there yet. Registering once at startup
would leave two fonts with no Hangul. `i18n/Font.cs` re-scans on every scene load.

**The pixel grid reads well.** Galmuri beside `m6x11plus` looks deliberate rather
than patched-in, which is what the font was chosen for. Pretendard and Noto Sans KR
stay unused.

### Why it was the real risk

The game's TMP fonts are `m6x11plus SDF`, `monogram-extended SDF`,
`THEBOLDFONT-FREEVERSION SDF`, `Inconsolata-SemiBold SDF` and `LiberationSans SDF`.
**None has a single Hangul glyph.** Untreated, the entire patch renders as `□□□`.

The shipped `Unity.TextMeshPro.dll` was decompiled to confirm the way out exists:

```csharp
// TMPro.TMP_FontAsset — public, in the build we are patching
public static TMP_FontAsset CreateFontAsset(string fontFilePath, int faceIndex,
    int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode,
    int atlasWidth, int atlasHeight);            // line 485
public List<TMP_FontAsset> fallbackFontAssetTable;   // line 448

// TMPro.TMP_Settings
public static List<TMP_FontAsset> fallbackFontAssets { get; set; }   // line 217
```

A TTF path, a dynamic atlas, and a public static setter. **No Unity Editor, no
version-matched AssetBundle, no reflection.** Ship `Galmuri11.ttf` beside the
plugin, build a font asset from it at startup, append it to the global fallback.

Dynamic population means only the glyphs actually drawn get rasterised, which is
what makes 11,172 Hangul syllables affordable; TMP spills to additional atlases by
itself when one fills.

**Galmuri11** (SIL OFL 1.1) is the font. It is a Nintendo DS-inspired Korean bitmap
face, so it sits beside `m6x11plus` instead of fighting it. Pretendard or Noto Sans
KR are the fallback if the pixel grid reads badly at the game's sizes.

M1 existed to answer that before any translation was written. It did, in the
affirmative, on day one — so the AssetBundle contingency is dropped.

### What the plugin looks like

```
Plugin.cs      lifecycle and wiring, no decisions
Anchors.cs     every game type reached by name, at runtime
Config.cs      MelonPreferences, and where our files live
Log.cs         one format, and Guard() so a dead patch says so once
Json.cs        a flat {string: string} reader that refuses what it cannot parse
i18n/Catalog.cs   key -> Korean
i18n/Font.cs      the fallback font asset, re-registered per scene
i18n/Patches.cs   A1 and A3
i18n/Dump.cs      the English extraction, off unless asked
```

`Assembly-CSharp` is deliberately **not** a reference. Every game type is reached
through `Anchors.cs` by name, so an update that renames one method costs a logged
miss and one dead feature rather than a plugin that will not load at all.

The catalogue takes two kinds of key. Most are the game's own
`LocalizedStringAsset.Key`. The rest are the **English text itself**, which is what
covers `_BaseData.Name` falling back to a ScriptableObject's file name — there is no
key there to look up. A translator who sees stray English adds an entry keyed by
exactly that English and it is fixed either way.

### Translators must not glue particles to tokens

Building descriptions are assembled by `Entities.Data/StringProcessor.cs`, and it
parses **by whitespace**:

```csharp
rawStr = rawStr.Replace("[BREAK]", "@[BREAK]@");
rawStr = rawStr.Replace("@", " @ ");
string[] array = rawStr.Split();              // ← whitespace
foreach (string text in array) {
    if (text.Contains("[")) {
        int num2 = text.IndexOf('[') + 1;
        int num3 = text.IndexOf(']');
        string text2 = text.Substring(num2, num3 - num2);
        ... AppendString(new StringWithTag { String = nameForTag, ... });
        // the rest of the word is never appended
    }
}
```

A word containing `[...]` is replaced by the resolved tag name and **the rest of that
word is discarded**. So `[Farm]에` loses the `에`. Silently.

This is not a corner case. The M1 dump found **426 of 1,088 strings (39%) carry at
least one token, across 236 distinct token names** — `[Adjacent]` 98 times,
`[InRange]` 93, `[Cooldown]` 84, `[BREAK]` 67, `[MultParam]` 46. Two out of five
strings a translator touches are governed by this rule.

The rules that follow:

- `[Tag]`, `{bold}`, `[BREAK]`, `<invalid>` stay **whitespace-separated words**
- Korean particles never touch a token. Restructure the sentence instead
- Token order is close to fixed; noun-stacking reads better here than forcing
  Korean word order and losing a particle

`tools/lint-locale.py` enforces this. A rule this easy to break by hand belongs in a
linter, not in a style guide nobody re-reads — and it earned its place immediately,
catching a `[BaseScore].` where the full stop would have vanished silently.

It also encodes the rest of the grammar: `{ }` must balance or bold runs to the end
of the string, `{0}` arguments must survive, `[Token]` counts must match (order need
not — tokens resolve by name, which is what makes Korean word order possible at
all), and `@` line breaks are counted against the English.

Two rules were written wrong first and corrected against the decompiled parser. `@`
is **not** destructive glued to a word — `StringProcessor` expands it to `" @ "`
before splitting — and `[{1}]` is a legal token, because `string.Format` fills it in
before the parser runs. The game's own English breaks the token rule 13 times, so
those keys are catalogued rather than reported as ours.

## Cheat

### Most of it already shipped

`Interaction/CheatsHandler.cs` is a developer cheat menu **left in the retail
build**, and `level3` and `globalgamemanagers.assets` both carry a live reference to
it. It arms on `RightShift`+`C`+`L`, then reads `RightShift` plus:

| Key | Effect | Key | Effect |
|---|---|---|---|
| `G` | gold +100 | `S` | force the shop open |
| `R` | rerolls +5 | `B`/`I`/`C` | debug spawn panel — buildings / items / consumables |
| `P` | dismisses +5 | `D` | removal mode |
| `W` | 100 weeks, target 1,000,000 | `T` | force-trigger mode |
| `E` | end the milestone now | `A` | add Arcane to the draft pool |
| `Home` | jump to milestone 8 | `PageUp` | `SpeedUpScoring` toggle |
| `L` | force a loss | `U` | grant Barn/Quay target categories |

`Shared.UI/DebugMenu.cs` adds unlock-all-guilds and max-level-all-guilds on top.

This collapses the cheat module from "reverse the economy" to "put a readable UI on
the switches that are already wired". **M0 verifies it in the running game**, because
the whole estimate rests on it.

### The widget does not use the built-in keys

`CheatsHandler` works and stays as it is — arm it with `RightShift`+`C`+`L` and the
shipped shortcuts still respond. The widget is deliberately built on **C2–C9**
instead, so if Crux ever strips their debug code the loss is the keyboard shortcuts
and nothing else.

Everything writes through the game's own `Change*` methods rather than the backing
fields behind their private setters. Those methods are what refresh `MoneyPanel`,
move the milestone bar and raise `MoneyChanged`; a field write leaves the number
right and the UI wrong, which reads as a broken mod rather than as a cheat. Every
call passes `withTrigger: false` — `true` would fire the on-gold-earned chain, so a
"+100 gold" button would silently score points through whatever is on the map.

### Achievements stop the moment you cheat

`AchievementsHandler.Achieve(Achievement)` is the one place the game calls out to
the platform, so a single prefix covers every achievement it has. The first use of
any cheat in a session closes that gate.

This is the default rather than a setting a careful player has to find, because the
failure is one-way: an achievement unlocked by a cheat cannot be taken back, and it
lands on an account the player keeps long after the run. `BlockAchievements` can
turn it off, and the panel says plainly which state it is in.

`SpeedUpScoring` is exempt. It skips an animation, changes no outcome, and M6 needs
it for unattended runs.

### Two things the panel had to learn about its own surroundings

**Clicks fell through.** The game asks `UiUtils.IsPointerOverUIObject()` before
acting on a click, and that only knows about uGUI — an IMGUI window is invisible to
it. Every click on the panel also placed a building on the map behind it. A postfix
that reports the pointer as over UI while it is inside the panel rect fixes it, and
it is not a trick: while the panel is under the cursor, the pointer *is* over UI.

**IMGUI has no idea what DPI is.** At 3440×1440 the default skin drew the panel
about a tenth of the screen wide and the text was unreadable. `GUI.matrix` scales
it, `WidgetScale` overrides, and 0 derives a scale from the window height.

### What the widget adds

Direct writes, where the built-in keys are too coarse:

```
GameState/ScoreController      Score(long) Money Rerolls Removes Dismisses
                               Rewinds Enchant — private setters, so write the
                               backing field; ChangeMoney clamps at 0
GameState/GameController       DebugChangeWeeks(int) DebugSetTarget(int)  [public]
                               AddExtraWeek() AddGuildToSelectedGuilds(…)
Entities/BuildingController    CanBuildBuildingAt(…, bool ignoreAllPlacementRestrictions)
                               ← a prefix forcing true is the clean "build anywhere"
                               SetCanPlaceNextBuildingAnywhere(bool)  [public]
                               InstantiateAndBuildBuildingAt(GameTag, x, y, …)
Progression/UnlockStateController  DebugUnlockAllGuilds() DebugLevelUpAllGuilds()
```

Saves are JSON plus compression (`IO/StateSerializer.cs`, `IO/StringCompressor.cs`)
with no signature, so save editing would work. We do not do it: a runtime write can
be undone by not saving, and a corrupted save cannot.

## Autoplay / play helper

The only part with no existing scaffolding, and the one that can fail on judgement
rather than on plumbing. It ships in three stages, and **stage 1 is useful alone**.

### Stage 1 grew a second question

The helper started by answering "where does this building go?" — which is only the
*second* question a player asks. The first is "which of these three, and where?",
and it is asked while the cards are still on the bar.

Scouting answers it. With nothing in hand, each offered building gets its own hue
and its own best tiles, labelled `A1`, `B1`, `C1`. Picking one up switches back to
the precise single-building mode.

Describing a building that has no instance took some care, and the obvious two
routes are both wrong:

| Route | Why not |
|---|---|
| `InstantiateBuilding` | a **write**. The helper does not write |
| `BuildingData.BuildingPrefab` | works, and lies. Reading `Values.Range` off a prefab runs the live modifier chain against a piece sitting at (0,0), so a `TownBell` near the origin changes the answer |

What works is the **behaviour's own base fields** — what a building is worth before
it is anywhere:

```
_range, _majorCategory, _minorCategories     base stats
GetBehaviourTargetTags()                     parameterless
GetBehaviourTargetCategories()               parameterless
GetScoreForTag(null, tag)                    ignores the piece in the base impl
GetTileTypesCanBePlacedOn(tag)               takes a TAG, not an instance
```

That last one is what keeps a scouted suggestion off the ocean.

**Two limits, stated rather than hidden.** Nineteen behaviours override
`CanBeBuiltOn` with a rule of their own ("only next to `[Trees]`"), and
`HasRangePlacementRestriction` dereferences the piece. Neither can be answered
without an instance, so scouting filters on tile type and the same-type rule only,
and the exact rules apply the moment the building is picked up. The restriction is
assumed **on** while scouting, which hides a legal tile rather than offering an
illegal one.

### Refreshing when the board actually changes

The shortlist used to be recomputed when the held piece changed or the building
*count* changed. That misses every change which leaves the count alone — a paint
applied, a mult or range altered, a transformation, a council vote landing.

`BuildingExtensions.ResetCaches()` is the game's own announcement that the board
moved, called from seventeen places, and it is exactly the set of events that
invalidate a shortlist. A postfix on it is exact invalidation rather than polling.
The rate limit stays: a trigger chain can fire it several times in one frame.

### Only suggesting what can be seen

Highlights are drawn in world space, so a suggestion off the edge of the screen is
invisible and the shortlist merely looks short. The search is clamped to the camera
viewport plus a tile of slack.

The window narrows where a piece may be **placed**, never what the valuation may
**see** — a building just off screen is often exactly what makes an on-screen tile
good, and `WindowTests.ABuildingOUTSIDETheWindowStillCounts` pins that. It also
makes the read cheaper, which matters now that it happens more often.

```
Stage 1  Play helper — an overlay
         Walk every legal tile for the piece being held, score it, highlight the
         best few. Reads game state, writes nothing. Cannot corrupt anything.

Stage 2  Auto-place — DONE 2026-09-23
         Through the player's own path, NOT InstantiateAndBuildBuildingAt.
         F10 steps once, F11 runs. Picks the building as well as the tile.

Stage 3  Autoplay — unattended
         A supervisor coroutine over InteractionState: shops, quests, packs,
         milestones. State-transition timing is where this gets hard.
```

### What stage 1 turned out to be

Six files, and the split is the design rather than tidiness:

```
Snapshot.cs   a board as plain data. No Unity types, no game types
Board.cs      the only file that touches MapController / BuildingController
Value.cs      what one placement on one tile is worth. Pure
Plan.cs       the ranked shortlist. Pure
State.cs      when it is meaningful to say anything
Overlay.cs    draws the shortlist. Reads, never writes
```

`Snapshot` carrying no Unity or game types is what lets `tests/` link `Value.cs` and
`Plan.cs` as **source** and run them on a CI runner where neither Unity nor the game
exists. A board written by hand in a test is indistinguishable from a real one. That
is also why tags and categories cross the seam as `int` — naming those enums would
mean referencing `Assembly-CSharp`, and the valuation only needs to know whether two
pieces share a category, never what it is called.

### The range shape had to be exact

`Grid.GetFilledCircle` is not a square and not quite a circle:

```csharp
dx*dx + dy*dy < r*r + r
```

Range 1 covers the four orthogonal neighbours and **not** the diagonals; range 2
covers all eight but not the corners at (±2, ±2). A Chebyshev square — the obvious
approximation — is wrong for exactly the cases a player checks first. It is
reproduced rather than approximated, and pinned by tests.

Crane and Stable extend a building's range through other buildings. Stage 1 ignores
that and says so here rather than in a comment nobody reads.

### A reflection bug that disabled the feature silently

`Entities.Building` redeclares `GamePiece.Behaviour` with `new` and a narrower type.
`Type.GetProperty(name, FlattenHierarchy)` finds **both** and throws
`AmbiguousMatchException` — inside a `Log.Guard`, which caught it, said so once, and
turned the helper off for the rest of the session. On screen it looked like the
overlay simply did nothing.

Two changes came out of it. `Reflect.cs` walks the hierarchy from the most derived
type with `DeclaredOnly`, so shadowing resolves the way the game's own code binds
and cannot be ambiguous between levels; it is pure, and linked into `tests/` with
the exact shape that broke. And `Log.HasFailed` lets a dead module say so in the
panel — before, "nothing to suggest" and "this crashed an hour ago" looked identical.

### Stage 2 takes the player's path, not the shortcut

`BuildingController.InstantiateAndBuildBuildingAt` is the obvious way to place a
building and the wrong one. It builds the building and skips everything around it -
the placement count, clearing the choice bar, multiple placement, the
consumable-on-create, the state transition at the end. The state machine would be
left believing the player is still placing.

So autoplay performs the player's own actions through the player's own methods, with
coordinates instead of a mouse:

```
choose  ->  BuildingChoiceButton.OnPointerClick(left)
place   ->  PlacingBuilding.OnUpdate(...)  then  PlaceCurrentBuilding(coords)
```

`OnUpdate` is the same method `InteractionController` calls every frame: it moves the
ghost, revalidates the tile, and refreshes the highlight caches the placement effects
read. `LMBDown` is false while we call it, so it will not place on its own - and then
we invoke the method its LMB branch would have.

It cannot break a rule. `PlaceAt` reads the game's own `_canPlaceCurrentBuilding`,
computed by that call, and gives up when it says no. **Autoplay cannot put a building
where the player could not.**

`Exec.cs` is the only file in `autoplay/` that writes. That everything else only
reads is what made stage 1 shippable unfinished.

### Two ways a Unity mod lies to itself

Both of these disabled a feature in silence, and both were found by running it rather
than by reading it.

**A destroyed object is not null.** Unity overloads `==` on `UnityEngine.Object` so a
destroyed object compares equal to null - but the overload is picked by the *static*
type, and everything reflection hands back is typed `object`. `building == null` is
therefore a plain reference comparison, which a destroyed object passes; the next
property read throws. The game destroys a placed building's ghost with
`Object.Destroy(go, 0.01f)`, so for a few frames after every placement
`BuildingController.Buildings` holds something that is neither null nor there.
`Alive.cs` exists for exactly this.

**The held ghost follows the cursor, and the cursor leaves the window.** Off the map
`Building.Tile` is null, and the game's accessors walk from it without checking:

```
Values.Range                 -> GetBehaviourRange -> GetCountOfBuildingsOfTypeAdjacent
HasRangePlacementRestriction -> GetAdjacentTiles  -> Get4Neighbours(null)
```

Simply *reading* the piece throws. That is why autoplay stopped the instant the
cursor left a windowed game, and why it looked like a click problem. The loop now
walks the ghost back onto the board - through `OnUpdate`, the same path a mouse move
takes - before it reads anything.

### Silence was the real bug

Both of those hid for the same reason: `Log.Guard` reported a module's first failure
and then never spoke again. The helper had been throwing every frame for an hour and
the only evidence was one line near the top of the log.

It now re-reports every thirty seconds **with a running count**, so "it happened
once" and "it is happening every frame" look different. That change produced the
stack trace for the off-map ghost within a minute of asking for it.

### The valuation problem, stated honestly

Combolands scores through cascading triggers (`GameState/TriggerController.cs`,
`TriggerQueue.cs`, `Entities/BehavioursController.cs`). There is **no way to
simulate a placement without committing it** — the trigger chain mutates live state.

So the ranking is a heuristic over adjacency and range overlap, using what is
already public:

```
Environment/MapController        Grid<Tile> Grid, Width 44 × Height 27,
                                 IsTileInCantBuildArea(x, y)
Entities/BuildingController      CanBuildBuildingAt(…out CantPlaceBuildingReason)
                                 GetBuildingsWhoseRangesOverlapTile(Tile)
                                 IsTileInRangeOfCategory(Tile, GamePieceCategory)
Interaction/InteractionController MouseCoords CurrentlyTargeting
                                 CurrentInteractionState  ChangeToState(…)
```

Re-implementing `PointsScorer` as a pure function would be exact and would break on
every game update. Stage 1 exists to find out whether the cheap heuristic is already
good enough to follow, by putting it on screen where a human can disagree with it.

**Buildings declare what they want.** That turned out to be the whole game:

```csharp
GamePiece.TargetTags                     // Woodcutter -> Trees
Behaviour.GetScoreForTag(piece, tag)     // 30
Values.TargetCategories                  // Barn -> Husbandry
Values.GetScoreForTargetCategory(cat)    // 12
```

All four are pure lookups into tables the behaviour already holds — nothing is
triggered and no state is touched. So the valuation does not have to guess at
affinity: it reads the building's own declaration and counts what it would actually
find nearby. Hold a Woodcutter and the tiles with the most Trees in reach light up.

The scores are **normalised by the largest one the candidate declares**. A building
paying 30 a tree and one paying 6 a tree are both simply doing their best; without
normalising, the first looks five times better placed than the second.

| Term | Weight | Why |
|---|---|---|
| declared targets found | **2.5** | the real rule, normalised. This decides the ranking |
| pieces this one would reach | 0.35 | for buildings whose effect is not "score per target" |
| pieces that would reach it | 0.45 | the same relation the other way round, and not the same thing |
| pieces touching it | 0.30 | adjacency is the game's other main verb |
| pairs sharing a category | 0.60 | a weak proxy, now only a tiebreaker |
| neighbours of the same type | **−1.0** | see below |
| room to grow | +0.05 | see below |

The generic terms are what is left for buildings whose effect is not expressible as
"score per target nearby". They separate ties; they no longer decide anything.

### Four things only playing it could find

**Same-type neighbours were ranked highest.** Two copies of a building share every
category by construction, so the affinity term loved them — while the game's own
text keeps excluding them: "Does not affect other `[Fisher]`", "excluding other
`[HerbGarden]`". They now carry a penalty instead.

**The game's own legality check leaked.** `CanBuildBuildingAt` rejects a tile where
another of the same building is in range, and the helper was suggesting those
anyway. The cause is in `BuildingExtensions`:

```csharp
_rangeCache is Dictionary<Building, HashSet<Tile>>       // keyed by Building ALONE
GetTilesInRange(overrideTile)  ->  returns the cached set on a hit
```

Probing thirty tiles in one frame gets one answer thirty times. The fix is not to
fight the cache: `ignoreAllPlacementRestrictions` gates **exactly that one check**
and nothing else, so the helper passes `true` — turning it off — and `Rules.cs`
evaluates it properly, per tile, in pure code. Everything else `CanBuildBuildingAt`
knows stays authoritative.

**On an empty board it pointed at the map edge.** With every synergy term at zero
the only signal left was a *penalty* on open neighbours, so corners won — the worst
possible advice for a first placement. The term is now positive and means "room to
grow". Keeping suggestions apart is the shortlist's job, not this one's.

**The top five were one suggestion.** Every term is an absolute count of nearby
pieces, so tiles beside a building always outscore open ground and the whole
shortlist landed in one cluster. `Plan` now requires picks to be `PlayHelperSpread`
tiles apart, which costs ranking fidelity on purpose: #2 is not the second-best
tile, it is the best tile **somewhere else**. That is the question a player holding a
building is actually asking.

`CheatsHandler.SpeedUpScoring` is already there for skipping scoring animation
during unattended runs.

## Milestones

```
M0  Ground truth                                      DONE 2026-09-22
    [x] MelonLoader v0.7.3 boots 6000.0.66f2 MonoBleedingEdge and runs a mod
    [x] all 34 anchors in ANCHORS.md resolve in the live Mono domain, 0 broken
    [x] RightShift+C+L arms the shipped cheats; +G, +B confirmed in game

M1  Hangul on screen                                  DONE 2026-09-22
    [x] Galmuri11.ttf → CreateFontAsset(path) → dynamic atlas, 300/300 glyphs
    [x] TMP_Settings.fallbackFontAssets + all loaded fonts' fallback tables
    [x] Korean renders in the retail build. Not □□□ (screenshot, menu)
    [x] Resources.LoadAll dump → 1088 keys / 6552 words
    [x] GetText() postfix proven end to end

M2  Translation complete                              DONE 2026-09-23
    [x] src/ plugin: Anchors, Catalog, Font, Patches, Dump, Json, Config, Log
    [x] tools/lint-locale.py encodes the StringProcessor grammar
    [x] 1071/1071 translatable strings - names, descriptions, UI
    [x] _BaseData.Name patched (A3), literal keys for text with no key
    [x] FontScale 0.8 - Hangul was towering over the game's Latin at 1.0
    [ ] hardcoded TMP text swept with UnityExplorer

M3  Cheat widget                                      DONE 2026-09-23
    [x] IMGUI panel on F8, over ScoreController/GameController/etc directly
    [x] achievement gate: one prefix on AchievementsHandler.Achieve
    [x] build-anywhere via the game's own ignoreAllPlacementRestrictions
    [x] panel clicks no longer fall through to the map
    [x] DPI scaling - IMGUI is unreadable above 1080p untouched

M4  Play helper (stages 1-2)                          DONE 2026-09-23
    [x] Snapshot / Board / Value / Plan / State / Overlay
    [x] the game's own range shape reproduced exactly
    [x] 14 unit tests over hand-written boards, no game, no Unity
    [x] judged by eye over a real run - and corrected four times because of it

M5  Ship                                              week 5
    release.yml · README · docs site · NOTICE/THIRD-PARTY
    GitHub Release with the plugin, locale, font and licences

M6+ Autoplay stages 2–3                               open-ended
```

## Distribution

`myso-kr/combolands-mod`, MIT, mirroring `now-thats-a-big-dragon-mod`.

A tag push builds and attaches the archive:

```
combolands-mod-vX.Y.Z.zip
├─ Mods/CombolandsMod.dll
├─ UserData/Combolands/ko/strings.json
├─ UserData/Combolands/fonts/Galmuri11.ttf
├─ licenses/OFL-Galmuri.txt
└─ README.md LICENSE NOTICE THIRD-PARTY.md
```

The font is redistributed inside that archive, so the OFL text is a **condition, not
a courtesy** — the release job copies it without `|| true`, the same as the reference
repo does.

MelonLoader is not bundled. It is the user's install step, and vendoring someone
else's loader into our archive makes their bug reports ours.

### What does not ship

No original game text, no game assets. `locale/ko/strings.json` is keys and Korean.
A user without the game has nothing to run it against.

### Telling Crux Games

Worth doing, before release rather than after. Two people, no EULA on the store page,
no Workshop — and a localisation scaffold already in their code. A Korean patch
landing as official support is a better outcome than a mod, and the way to find out
is to ask.

## Risks

| Risk | Kills | Status |
|---|---|---|
| ~~MelonLoader will not boot Unity 6000 Mono~~ | ~~everything~~ | **retired at M0** |
| ~~A patch target does not exist as documented~~ | ~~some module~~ | **retired at M0** — 34/34 |
| ~~`CreateFontAsset` fails at runtime~~ | ~~the whole language patch~~ | **retired at M1** |
| Future game updates move a patch target | some module | `ANCHORS.md` + `tests/Anchors` |
| Token parser eats Korean particles | translation quality | `tools/lint-locale.py` |
| Heuristic valuation plays badly | autoplay only | open, **M4**, visibly |

Every risk that could have changed the plan is retired, on day one rather than in
week three. What is left is work, not uncertainty — with one correction already
paid for: the translation is **1,088 strings, not the 500–700 the byte scan
suggested**, so M2 is a longer week than it was written to be.
