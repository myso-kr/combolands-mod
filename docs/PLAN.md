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

Scale, estimated from a string scan of `resources.assets`: **768 candidates,
~2,100 words**, including shader property names and other noise. Expect **500–700
real strings**. One person can translate that.

Nothing extracted ever gets committed — see `.gitignore`.

### The font is the real risk

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

M1 exists to prove this before any translation is written. If `CreateFontAsset` does
not work at runtime here, the plan changes to building a TMP font AssetBundle in
Unity 6000.0.66f2, and that is a different week.

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

The rules that follow:

- `[Tag]`, `{bold}`, `[BREAK]`, `<invalid>` stay **whitespace-separated words**
- Korean particles never touch a token. Restructure the sentence instead
- Token order is close to fixed; noun-stacking reads better here than forcing
  Korean word order and losing a particle

`tools/lint-locale.py` enforces this in CI. A rule this easy to break by hand
belongs in a linter, not in a style guide nobody re-reads.

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

```
Stage 1  Play helper — an overlay
         Walk every legal tile for the piece being held, score it, highlight the
         best few. Reads game state, writes nothing. Cannot corrupt anything.

Stage 2  Auto-place — one keypress
         Call InstantiateAndBuildBuildingAt() on stage 1's top tile. Bypasses the
         PlacingBuilding state, so UI consistency needs checking.

Stage 3  Autoplay — unattended
         A supervisor coroutine over InteractionState: shops, quests, packs,
         milestones. State-transition timing is where this gets hard.
```

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

`CheatsHandler.SpeedUpScoring` is already there for skipping scoring animation
during unattended runs.

## Milestones

```
M0  Ground truth                                      DONE 2026-09-22
    [x] MelonLoader v0.7.3 boots 6000.0.66f2 MonoBleedingEdge and runs a mod
    [x] all 34 anchors in ANCHORS.md resolve in the live Mono domain, 0 broken
    [x] RightShift+C+L arms the shipped cheats; +G, +B confirmed in game

M1  Hangul on screen                                  week 1
    Galmuri11.ttf → CreateFontAsset(path) → TMP_Settings.fallbackFontAssets
    A hardcoded Korean string renders. Not □□□
    Resources.LoadAll dump → generated/strings.en.json
    GetText() postfix wired to locale/ko/strings.json

M2  Translation complete                              week 2
    _BaseData.Name patched · StringLookup verified
    tools/lint-locale.py in CI
    Hardcoded TMP text swept with UnityExplorer

M3  Cheat widget                                      week 3
    IMGUI panel over CheatsHandler + direct writes
    MelonPreferences config · achievement-safety decision

M4  Play helper (stage 1)                             week 4
    Board reader · valuation · overlay
    Unit tests over fixture boards, no game required

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
| `CreateFontAsset` fails at runtime | the whole language patch | open, **M1** |
| Future game updates move a patch target | some module | `ANCHORS.md` + `tests/Anchors` |
| Token parser eats Korean particles | translation quality | `tools/lint-locale.py` |
| Heuristic valuation plays badly | autoplay only | open, **M4**, visibly |

The two cheapest to test and most expensive to be wrong about are gone on day one
rather than in week three. The font is the last one of that kind left, which is why
M1 is a proof and not a feature.
