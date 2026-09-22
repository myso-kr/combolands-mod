---
layout: default
title: "Combolands mod"
description: "A Korean language patch, an in-game cheat widget, and an autoplay that plays a run on its own. Unofficial, MelonLoader-based, and it modifies no game file."
lang: en
---

# Korean, cheats, and an autoplay

An unofficial mod for **Combolands: Roguelike Citybuilder** (Crux Games).
It adds three files to your game folder and changes none of the ones already there.

<a class="get" href="https://github.com/myso-kr/combolands-mod/releases/latest">Download the latest release</a>
<a class="get quiet" href="https://github.com/myso-kr/combolands-mod">Source on GitHub</a>

<div class="note" markdown="1">
Verified against game **v1.0.6**, Steam build `24989173`. After a game update the
translation can revert to English or autoplay can stop — that is expected, and
[the anchor catalogue](ANCHORS) is where it gets fixed.
</div>

## The three

**한국어.** Every one of the game's 1,071 translatable strings, joined on the key
rather than on the English, so Crux can reword their own text without moving ours.
None of the game's fonts has a single Hangul glyph, so the mod builds a
[Galmuri11](https://github.com/quiple/galmuri) font asset at runtime and hangs it off
every font as a fallback.

**Cheats,** on <kbd>F8</kbd>. Gold, score, rerolls, removes, dismisses, rewinds and
enchantment; weeks and score target; skip the milestone, force the shop, build
anywhere. Every write goes through the game's own methods rather than the fields
behind them, so the UI, the triggers and the save all agree.

Using any of them **stops Steam achievements for the rest of the session**. That is
the default rather than a setting to find, because an achievement unlocked by a cheat
cannot be taken back.

**Autoplay,** on <kbd>F11</kbd>. It plays: picking which of the offered buildings to
take, where to put it, whether the shop is worth entering, which council request it
can actually finish, and past every screen between milestones. <kbd>F9</kbd> is the
quieter version — it highlights where it *would* place what you are holding and
leaves the clicking to you.

## How it decides

It does not guess what a tile is worth. The game exposes `GetScorePreview`, a
pure function giving what a building scores and off which tiles, and end of turn
scores every building on the board — so a week is arithmetic. Dump the rules once
and the overlay prints real points, checkable against the game's own display. Then
it leans that answer toward the milestone: with weeks to spare it builds the engine,
and with two weeks left it takes the points.

It also cannot break a rule. Every placement goes through the game's own legality
check and is abandoned when the answer is no.

## Installing

You need [MelonLoader](https://melonloader.co/) **v0.7.3** first — it is not bundled
here, because vendoring somebody else's loader would make their bug reports ours.

Then unpack the release over your game folder:

```
Combolands/
├─ Mods/CombolandsMod.dll
├─ UserData/Combolands/locale/ko/strings.json
└─ UserData/Combolands/fonts/Galmuri11.ttf
```

Uninstalling is deleting those. Nothing in the game folder was changed, so there is
nothing to restore.

<div class="note" markdown="1">
**Back up your save before using cheats or autoplay.** They put a run into states
ordinary play cannot reach.
</div>

## For people reading the code

| | |
|---|---|
| [Anchor catalogue](ANCHORS) | every signature the mod binds, what breaks when the game moves it, and where to fix it |
| [The simulator](SIMULATION) | how a placement's worth is computed rather than guessed, and how faithfully |
| [Directory conventions](CONVENTIONS) | where code goes, and why the valuation has no Unity types in it |
| [The plan](PLAN) | what this is, what it attaches to, and the order the work happened in |

The parts that decide anything — the board snapshot, the valuation, the placement
plan, the rules, the pace — carry no Unity and no game types at all. That is what
lets 81 tests run on a machine that has never seen the game, and lets the policy
trainer play tens of thousands of simulated milestones there. A separate suite reads
an installed `Assembly-CSharp.dll` as metadata and checks every anchor in about fifty
milliseconds, naming the row that broke.
