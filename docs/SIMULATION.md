---
layout: default
title: "The simulator"
description: "How the helper computes what a placement is worth instead of guessing, how faithfully, and what training the weights actually found."
lang: en
permalink: /SIMULATION/
---

# The simulator

Until this existed, the placement helper ranked tiles by proximity: how many pieces
a candidate would reach, how many would reach it, how many categories they shared.
Every one of those is a **proxy** for the only question that matters — how many
points does this tile add — and the plan document said the real answer was
unobtainable, because Combolands scores through cascading triggers that mutate live
state as they run.

That was wrong, and milestone misses were what it cost.

## The week is arithmetic

`_BuildingBehaviour.GetScorePreview(Building)` is a pure function. Given a building
on a board it returns which tiles it scores off and for how much, and it touches
nothing. The game's own `UI.MaxPossibleScores` calls it for every building on the
board and multiplies each sum by that building's `Multiplier`.

That display is not an estimate of the week's score. It **is** the week's score:
`ScoreController.ScorePoints` hands the same numbers to `PointsScorer.ExecuteScoring`,
which ends at `round(sum * originMultiplier)`.

And a placement is a week: putting a building down from the choice bar calls
`GameController.EndTurn` immediately, and end of turn scores **every** building on
the board — `TriggerController.AddEndOfTurnTriggersToAll` walks all of them.

So the value of a placement with `W` weeks remaining is its change to the weekly
total, times `W`. Which is computable, and is what `autoplay/sim/` computes.

## Four things the old valuation had wrong

Every one of them in the direction that makes a target look closer than it is.

**One neighbourhood, not two.** `_scorePreviewMode` is `Adjacent` or `InRange` or
`SelfOnly` — exactly one. The old code counted a target that was in range **or**
adjacent, for every building, so every adjacency scorer was credited with the whole
of its range.

**No week multiplier at all.** A placement made with eight weeks left is worth eight
times the same placement on the last week. Ranking on this week's delta treats them
as equal, which is how a long milestone ends one week short.

**Cooldowns ignored.** Woodcutter pays eighty a tree and pays it every third week —
`CountIsReadyToActivate` counts down and only fires on zero. Read as weekly income,
that is a threefold overstatement on some of the strongest-looking pieces in the
game.

**Target precedence.** A tag match precludes a category match, which precludes a
rarity match, and a tile pays at most once. A building that is both a named target
and a member of a targeted category pays once, at the tag rate.

## Two things a placement is worth beyond its own score

**Blueprints are placements that cost no week.** `PlacingBuilding` jumps straight
past `EndTurn` for a consumable, so a blueprint is a free building — the same board,
the same scoring, one fewer week spent. A milestone with three blueprints in hand is
three placements ahead of one without, and a planner that valued them at zero would
hold them.

**Activation is most of what eighteen buildings are for.** They wake their
neighbours, and a woken building scores again out of turn *ignoring its own
cooldown* — which is precisely why they are worth building around. The game's shape
is always the same: collect what is activatable in reach, drop yourself and whoever
woke you, then take some of the rest.

"Some" turns out to be two different rules, and the difference is the activation
count:

| count | what happens | who |
|---|---|---|
| more than zero | that many, **chosen at random** | Conduit takes two, Sawmill one |
| zero | **all of them** | FishTrap wakes every Fishing building in range, EffigyPyre everything adjacent |

Eight of the eighteen are the second kind, and reading their zero as "activates
nothing" valued them all at nought — which is what the first version of this did.

The random half is why the simulator takes an **expectation** rather than sampling.
A candidate is chosen with probability `min(count, n) / n`, and the expected extra
is that times what it scores. Over a ten-week milestone the expectation is the right
quantity; one sampled outcome would be noise dressed as precision.

An activator that declares target categories wakes only those — Sawmill wakes
Engineering and Manufacturers, Marketplace wakes Stalls. One that declares none
wakes anything. Leaving that filter out would over-value exactly the buildings that
are fussiest about what they sit beside, and over-valuing is the direction that
makes a plan miss.

Cascades are followed two deep. The game has no depth limit; two is a statement
about diminishing returns rather than about the game, since each step multiplies the
cost by the number of candidates and divides the expected value by roughly the same.
It also cannot loop, where the game's own guard — excluding whoever woke you — only
stops cycles of length two.

## Room is a modifier, not a value

Worth recording because it was got wrong twice in the same way.

Elbow room started as an independent quantity in points, scaled by what the
milestone still needed per week. On any milestone with a large target that term
swamped the points term completely: the planner built for empty space, scored
nothing, and because scoring nothing kept the board's own rate at zero, it never
recovered. Rescaling it against the board's rate instead had the identical failure
whenever that rate was still zero.

Both were fixing a scale when the error was the shape. What elbow room is worth is
"this placement is good **and** it extends" — a modifier on a value, not a value. It
multiplies now, so zero points with room beside it is worth zero, which is also the
right answer: space next to nothing is nothing. On an empty board the one-ply
lookahead is what discriminates, and that is in points already.

A blueprint test found it: three free buildings scored exactly as much as none,
because all of them went somewhere empty.

## How faithful it is

The rule set is dumped from a running game once, into `generated/rules.json`, which
is **not** committed — it is derived from the game, like the string dumps, and
anyone with a copy can make it again.

Of 167 buildings, **137 are reproduced exactly** and 30 are marked `approximate`,
which means one of:

- the behaviour overrides `GetScorePreview` and has its own idea of what it scores
- it needs a live building to answer at all — Shrine pays fifty per reroll held,
  which has no static value
- it scores without declaring a preview mode, so the reach was **inferred** from its
  range and targets and the piece carries `reachInferred`
- it activates its neighbours, which is modelled as an expectation over a random
  choice — the right quantity, and not the same as reproducing it

An approximate piece is still modelled and its score is still a lower bound. Saying
which is which is the difference between a simulator and a guess in a simulator's
clothes. A piece that could not be read at all scores zero, which is the safe
direction: the planner under-rates it rather than building a milestone plan around a
building it does not understand.

`docs/ANCHORS.md` rows M1–M8 are what the dump reads. **M8 is the only one not read
out of the game at all**: a behaviour activates by *calling*
`AddOnActivatedTrigger`, which no field records and no signature implies, so the
dumper carries a list of eighteen names and checks at dump time that each still
exists. A building missing from that list is valued at nothing extra, which is safe;
one wrongly on it is over-valued, which is not — so when in doubt it is left off.

 **M1 is the one no test can
defend**: `GetScorePreview` is *reproduced* in `sim/Preview.cs`, not called, because
calling it needs a live building on a live tile. It can keep its name, change its
arithmetic, and every check will pass while every number the helper prints is
quietly wrong. Ten tests pin the reproduction; nothing pins the original.

## What training actually found

`tools/train` fits the four remaining weights by cross-entropy method against
simulated milestones, scored on whether the target was cleared. It runs with no game
anywhere near it.

Two results are worth recording, and the second is the important one.

**The first formulation could not be trained at all.** Every sampled policy played
identically, to the placement, across every generation. An argmax is invariant to a
positive rescaling of the whole weight vector, and with the points term an order of
magnitude larger than the others the argmax was simply "most points" whatever the
other weights said. Fixing the points term at one removes the redundant degree of
freedom and makes the rest answerable: `potentialFlat` is how many points of future
set-up are worth one point scored today.

**And then the fitting did not beat the hand-written weights.** On the 150 milestones
it was fitted to, the search found 55 clears against 52 — and on 150 it had never
seen, both cleared 45, with the fitted policy fractionally worse on margin. That is
overfitting, not improvement, and `tools/train` refuses to write a policy that fails
the held-out comparison. So the shipped weights are the hand-written ones.

This is not a failed experiment; it is the experiment answering. Two things follow
from it:

- **The four corrections above are what fixes the misses**, not the weights. The
  weights are a refinement on an answer that is now correct rather than a patch over
  one that was not.
- **The search switched the room term off** — `roomFlat` fell from 0.03 to about
  zero. Room was a proxy for "there will be somewhere to build the rest of this", and
  the one-ply lookahead now measures that directly. A redundant proxy is exactly what
  a fitted weight should discover, and it did.

Which of a milestone's three offered cards you are given dominates where you put
them. A placement policy cannot out-play a bad draw, and a fitted one cannot
out-play it either.

## Running it

```
# once, from inside the game: set DumpRules = true and start a run
python tools/version.py                  # anything, just to have the repo root
dotnet run --project tools/train         # needs generated/rules.json
dotnet run --project tools/train -- --episodes 300 --generations 40
```

The trainer prints both numbers every run — fitted-to and held-out — and writes
nothing unless the second one improves.
