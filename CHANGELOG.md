# Changelog

Versions follow [semantic versioning](https://semver.org/). Until 1.0 the autoplay
valuation may change how it plays between minor versions; the language patch and the
cheat widget will not change shape without a note here.

Every release records the game build it was verified against. This mod attaches to
type and member signatures inside `Assembly-CSharp.dll`, so "which game" is not
background information — it is the first question any bug report has to answer. See
[`docs/ANCHORS.md`](docs/ANCHORS.md).

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

- 81 unit tests over the valuation, the range shape, the placement rules and the
  reflection helper, running without Unity and without the game
- `tests/Combolands.Anchors` reads an installed `Assembly-CSharp.dll` as metadata and
  checks every bound anchor in about fifty milliseconds, naming the row that broke.
  Writing it found six documented anchors the mod never actually touched
- Nothing extracted from the game is in this repository. No original text, no assets
