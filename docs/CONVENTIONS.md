---
layout: default
title: "Directory conventions"
description: "Where code goes in this repository and why — one responsibility per file, one place per decision."
lang: en
---

# Directory conventions

Where a file lives should tell you what it is responsible for.
When you are unsure where a new file goes, ask **who edits it** first.

| Directory | Who edits it | Rule |
|---|---|---|
| `locale/` | translators | Translated output and fonts. **No code** |
| `src/` | developers | C# that runs **inside the game's Mono runtime** |
| `tools/` | developers | things a human runs by hand |
| `tests/` | developers | runs **without the game** |
| `generated/` | **nobody** | produced by `tools/`. Edit it and the next extraction erases you |
| `docs/` | developers | documentation, and the GitHub Pages source |

Most of `generated/` is in `.gitignore`. Anything extracted from the game does not
go in the repository. `generated/fingerprint.json` is the exception and the reason
is written next to it.

## One project, three places code runs

Unlike a CDP mod, everything here is C# — which makes it easy to forget that these
three fail in completely different ways.

```
src/      inside the game       a thrown exception in a Harmony patch can
                                silently disable the patch and nothing else
tools/    run by hand           some need the game installed, some don't
tests/    CI                    never sees the game; anchors read metadata only
```

A patch that throws does not crash the game. It leaves the feature quietly dead,
which is worse. Every patch body catches and logs; the logging goes through `Log.cs`
so there is one format to grep.

## One responsibility per file

If you cannot say what a file does in one sentence, split it.
If the sentence needs an "and", it is already two.

### Plugin (`src/`)

```
Plugin.cs              the entry point: MelonMod lifecycle and module wiring. Nothing else
Config.cs              MelonPreferences binding
Log.cs                 log formatting, and Guard() so a dead patch says so once
Reflect.cs             member lookup that survives `new` shadowing. Pure, and tested
Alive.cs               whether something from the game still exists. Unity's == lies
Anchors.cs             every game type reached by name, and what to say when one is gone
Singletons.cs          resolving the game's MonoSingleton<T> instances, in one place
Json.cs                a flat {string: string} reader that refuses what it cannot parse

i18n/Catalog.cs        locale/<lang>/strings.json → Key lookup
i18n/Patches.cs        Harmony patches on LocalizedStringAsset and _BaseData
i18n/Font.cs           TMP font asset creation and fallback registration
i18n/Dump.cs           Resources.LoadAll extraction (dev only)

cheat/Widget.cs        the panel: layout and input. No game writes
cheat/Economy.cs       money, score, rerolls, removes, dismisses, rewinds
cheat/Run.cs           weeks, target score, milestone
cheat/Build.cs         placement restriction bypass, spawning
cheat/Unlocks.cs       guild unlock and level

autoplay/Snapshot.cs   a board as plain data. No Unity types, no game types
autoplay/Board.cs      the only code that touches MapController/BuildingController
autoplay/State.cs      InteractionState gating — when it is safe to act
autoplay/Rules.cs      the placement rules we must evaluate ourselves. Pure
autoplay/Offers.cs     describing a building from the choice bar, with no instance
autoplay/Value.cs      what one placement on one tile is worth. Pure
autoplay/Plan.cs       the ranked shortlist. Pure
autoplay/Overlay.cs    drawing the shortlist. Reads, never writes
autoplay/Pace.cs       how much hurry the milestone is in. Pure
autoplay/Screens.cs    the screens a run stops on, and how to get past them
autoplay/Shop.cs       whether to go in, what to buy, when to leave
autoplay/Quests.cs     which council request to take, and collecting its reward
autoplay/Items.cs      spending what the run has picked up
autoplay/Offers.cs     describing an offered building, with no instance to ask
autoplay/Supervisor.cs the loop: WHEN to act, never what
autoplay/Exec.cs       the only file in autoplay/ that WRITES to the game
```

`Board.cs` being the only file that touches the game's controllers is the point.
Reading game state and deciding what to do with it are different jobs; entangled,
neither is testable. Split, `Value.cs` takes a plain board snapshot and CI can run it.

`Overlay.cs` never writing is what makes stage 1 of the play helper safe to ship
before the valuation is any good.

`Snapshot.cs`, `Rules.cs`, `Value.cs` and `Plan.cs` carry **no Unity and no game
types**, which is what lets `tests/` link them as source and run them where neither
Unity nor the game exists. That constraint is the reason `Board.cs` exists at all.
Adding a `UnityEngine` using to any of those four takes the tests with it.

### Translations (`locale/`)

```
locale/ko/strings.json     Key → Korean. Nothing else
locale/fonts/Galmuri11.ttf
locale/fonts/OFL-Galmuri.txt
```

`strings.json` is keyed by the game's own `LocalizedStringAsset.Key`, so a game
update that rewords English does not move our translations — it only reveals which
keys are new. The English is never stored here; it lives in `generated/`, which is
ignored, because it is the game's text.

## Testing what only the real game can show

Two suites, answering two different questions.

```
dotnet test tests/Combolands.Mod.Tests    # is the reasoning right?   no game, no Unity
dotnet test tests/Combolands.Anchors      # is the game still there?  metadata only
```

The first runs against hand-written board fixtures and hand-written type
hierarchies. That is the only way to state a case precisely — and also why none of
them would notice a patch target drifting in the real game.

The second closes exactly that gap. It reads an installed `Assembly-CSharp.dll` as
metadata — nothing is loaded, constructed or executed — and checks every bound row
of `ANCHORS.md` in one pass, naming the row that broke. With no game installed it
**skips rather than passes**: a green run that checked nothing would be worse than
no test at all, so CI sees yellow and says so. The checks that hold `ANCHORS.md` and
the catalogue to each other still run there, since those need no game.

Both link the plugin's own files as source rather than referencing the built
assembly, for the same reason: the plugin targets net472 and binds Unity and
MelonLoader, neither of which exists on a runner. The anchor suite links
`Reflect.cs` in particular because binding a member *the way the mod binds it* is
the whole point — a check written with `GetProperty` would pass on overloads the mod
cannot actually resolve.

One gap is still open, and it is worth naming rather than implying away.

**Nothing watches the valuation's judgement.** Whether a suggestion is *good* is not
a property any fixture can assert — every correction to it so far came from playing
a run and disagreeing with what was on screen. The tests pin the mechanics the
judgement is built from; they cannot pin the judgement.

## Commit and branch

`main` is releasable. Tags are `vX.Y.Z` and a tag push is what publishes — see
`release.yml`. Anything extracted from the game stays out of history; it cannot be
removed later without a rewrite.
