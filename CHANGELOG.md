# Changelog

Versions follow [semantic versioning](https://semver.org/). Until 1.0 the autoplay
valuation may change how it plays between minor versions; the language patch and the
cheat widget will not change shape without a note here.

Every release records the game build it was verified against. This mod attaches to
type and member signatures inside `Assembly-CSharp.dll`, so "which game" is not
background information — it is the first question any bug report has to answer. See
[`docs/ANCHORS.md`](docs/ANCHORS.md).

## 0.5.0

Verified against Steam build `24989173` / game `v1.0.6` (Unity 6000.0.66f2).

Autoplay stopped guessing what a placement is worth and started computing it.

### The simulator

The plan document used to say a placement's score could not be known without
committing to it, because Combolands scores through cascading triggers that mutate
live state. That was wrong, and missed milestones were what it cost.
`_BuildingBehaviour.GetScorePreview` is a pure function, the game's own
`MaxPossibleScores` already calls it for every building, and end of turn scores all
of them - so a week is arithmetic.

Reproducing it found four errors in the old valuation, every one of them making a
target look closer than it was:

- **One neighbourhood, not two.** `_scorePreviewMode` is `Adjacent` **or** `InRange`
  **or** `SelfOnly`. The old code counted targets in range *or* adjacent for every
  building, crediting adjacency scorers with their whole range
- **No week multiplier.** A placement with eight weeks left is worth eight times the
  same placement on the last one. The old ranking treated them as equal
- **Cooldowns ignored.** Woodcutter pays eighty a tree every *third* week; read as
  weekly income that is a threefold overstatement
- **Target precedence.** A tag match precludes a category match, which precludes a
  rarity match, and a tile pays once

It also models what a placement is worth beyond its own score: **blueprints**, which
place without ending the turn, and **trigger cascades** - the eighteen buildings that
wake their neighbours, whose woken targets score again ignoring their own cooldown.
Because the game chooses who to wake at random, that is computed as an expectation.

The rule set is read out of a running game once (`DumpRules = true`) into
`generated/rules.json`, which is **not** distributed. 167 buildings; 137 reproduced
exactly, 30 marked approximate rather than quietly modelled wrong. The overlay prints
real points now - `+840/wk`, checkable against the game's own display - and says
`[simulated]` or `[estimated]` so you know which you are looking at.

### Rerolling

Autoplay spends rerolls on a bad draw. A reroll refills the choice bar **without
ending the turn**, so one left unspent at the end of a run bought nothing.

Four rounds of fitting the placement weights failed to beat the hand-written ones on
milestones they had not been fitted to. Rerolling, which changes the draw rather than
the placement, moved the same measurement by four points of clear rate and four of
margin. The threshold is learned per run - what a good draw is worth depends entirely
on the board - and is `AutoplayRerollBelow`.

### Council requests

Autoplay chooses a request it can actually finish rather than the first one offered,
takes the free first reroll when all three are hopeless, and **always collects the
reward** - the end-of-milestone routine disables input and waits on an unclaimed one
forever, so not claiming ends the run.

### Under it

- `tools/train` fits the policy against simulated milestones with no game involved,
  and refuses to ship weights that are not meaningfully better on held-out milestones
- `tests/Combolands.Anchors` checks all 61 anchors against an installed
  `Assembly-CSharp.dll` as metadata in about fifty milliseconds
- 91 unit tests, including the scoring reproduction and the cascade expectation

## 0.4.0

Verified against Steam build `24989173` / game `v1.0.6` (Unity 6000.0.66f2).

First public release. Three features, all removable by deleting the files you added.

### Language patch

- Korean for all 1,071 translatable strings, through a single Harmony postfix on
  `LocalizedStringAsset.GetText`
- Galmuri11 bundled as a TextMeshPro fallback font, registered per scene rather than
  once at startup — two of the game's seven fonts only load with the game scene, and
  a single startup pass left those without Hangul
- Glyph scale 0.8, because TMP sizes a fallback glyph from the *supplying* asset's
  `faceInfo` and Hangul at 1.0 overflowed the game's buttons
- `tools/lint-locale.py` encodes the game's own `StringProcessor` grammar, so a
  translation that glues a particle to a `[token]` fails before it ships

### Cheat widget

- **F8**. Gold, score, rerolls, removes, dismisses, rewinds, enchant; weeks and score
  target; unlock and level up all guilds; skip the milestone; force the shop; build
  anywhere
- Every write goes through the game's own `Change*` methods rather than the backing
  fields, so the UI, the triggers and the save all see it
- **Achievements are blocked the moment anything is cheated**, by a prefix on
  `AchievementsHandler.Achieve`. This is the one guard whose failure would be silent
  and outward-facing, so it logs an error rather than a warning if its anchor moves

### Autoplay

- **F9** highlights where to place what you are holding; **F11** plays on its own;
  **F10** takes one step
- Ranks tiles by what the game itself declares — `TargetTags` with `GetScoreForTag`,
  `TargetCategories` with `GetScoreForTargetCategory` — rather than by proximity.
  A Woodcutter says "Trees, 30 points each", and the valuation counts what it would
  actually find
- Scouts the offered buildings it is *not* holding, from base behaviour values, so
  the choice between three cards is made before one is picked up
- Plays toward the milestone: with weeks to spare it builds the engine, and with two
  weeks left it takes the points
- Gets past every screen between milestones — the summary, packs, the shop, council
  requests — and spends blueprint consumables
- Chooses a council request it can actually finish, takes the free first reroll when
  all three are hopeless, and always collects the reward. An unclaimed reward is not
  a missed reward: the end-of-milestone routine disables input and waits on it
  forever
- Uses the shop or takes the skip reward, ranked by rarity adjusted by how well a
  building would sit on the real board
- Never breaks a rule. Every placement goes through the game's own legality check and
  is abandoned when the answer is no
- Does **not** aim paints, stat mods or potions. Those apply through a path that
  begins `if (InputKeys.LMBDown)`, and synthesising that would mean patching the left
  mouse button globally

### Under it

- 91 unit tests over the valuation, the range shape, the placement rules and the
  reflection helper, running without Unity and without the game
- `tests/Combolands.Anchors` reads an installed `Assembly-CSharp.dll` as metadata and
  checks every bound anchor in about fifty milliseconds, naming the row that broke.
  Writing it found six documented anchors the mod never actually touched
- Nothing extracted from the game is in this repository. No original text, no assets
