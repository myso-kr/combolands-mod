# Combolands — mod

Korean language patch, cheat widget, and autoplay for the Steam roguelike citybuilder
*Combolands: Roguelike Citybuilder* (Crux Games, AppID 4075620).

> **Status: M0 passed.** Nothing shippable is built yet. MelonLoader boots the game,
> and all 34 patch targets in [ANCHORS.md](docs/ANCHORS.md) were confirmed in the
> live Mono domain. See the documents below.

| Document | What it settles |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | what this is, what it attaches to, and the order the work happens in |
| [docs/CONVENTIONS.md](docs/CONVENTIONS.md) | where files go |
| [docs/ANCHORS.md](docs/ANCHORS.md) | what breaks when the game updates, and where to fix it |

## What it will do

| | |
|---|---|
| **Language patch** | Korean, through the game's own `LocalizedStringAsset` keys, with a Hangul TMP fallback font built at runtime |
| **Cheat widget** | A panel over the developer cheats Crux left in the retail build, plus direct economy and placement writes |
| **Play helper → autoplay** | Highlights the best placements first; acts on them later |

## How it attaches

Unity 6000.0.66f2, **Mono**, no obfuscation. A MelonLoader plugin with Harmony
patches. Game files are not modified — the loader and the plugin are added beside
them and uninstall by deletion.

The two assumptions everything rests on are tested before anything is written:
that MelonLoader boots this build, and that `TMP_FontAsset.CreateFontAsset` can make
a Korean font asset at runtime. Both are M0/M1 in the plan.

---

> **Unofficial fan-made mod.** Not affiliated with or endorsed by Crux Games or
> Valve. Requires a legitimately purchased copy of the game — this repository
> contains no original game text or assets. Cheats and autoplay can corrupt your
> save; back it up first. Provided as is, with no warranty.
