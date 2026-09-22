# Third-party software

## Redistributed in this repository and in every release

### Galmuri11 — `locale/fonts/Galmuri11.ttf`

A Korean bitmap-style typeface by Lee Minseo (quiple), chosen because it is drawn on
a pixel grid and therefore sits beside the game's own `m6x11plus` rather than
fighting it.

| | |
|---|---|
| Version | 2.40.4 |
| Copyright | © 2019–2025 Lee Minseo (quiple@quiple.dev) |
| Licence | SIL Open Font License 1.1 — `locale/fonts/OFL-Galmuri.txt` |
| Source | https://github.com/quiple/galmuri |
| sha256 | `e24256f42e43713d2ea086a1e1669d78b968f5b3cc547e5c157f0606ffa5def1` |

The OFL permits bundling and redistribution; it does **not** permit selling the font
on its own. The full licence text ships in every release archive, which is a
condition of that permission rather than a courtesy — the release job copies it
without `|| true`.

The font is not modified. It is loaded at runtime from the file, so the bytes in a
release are the bytes quiple published; the hash above is what to check against.

## Required at runtime, not redistributed

### MelonLoader

The mod is a MelonLoader plugin and is useless without it, but the loader is the
player's own install step and no part of it ships here. Vendoring someone else's
loader into our archive would make their bug reports ours.

| | |
|---|---|
| Version | 0.7.3 (verified) |
| Licence | Apache-2.0 |
| Source | https://github.com/LavaGang/MelonLoader |

### Harmony

Reached through MelonLoader, which ships it. Nothing is redistributed here.

| | |
|---|---|
| Licence | MIT |
| Source | https://github.com/pardeike/Harmony |

## Not redistributed, and deliberately so

The game's own English text is read out of an installed copy at runtime and written
to `generated/`, which is git-ignored. `locale/ko/strings.json` holds keys and
Korean only. A release archive contains no original game text, art, audio or code —
see [NOTICE](NOTICE).
