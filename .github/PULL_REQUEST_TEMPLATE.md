## What does this change

<!-- A line or two. If it fixes an issue, "Fixes #12". -->

## Kind

- [ ] Translation (`locale/`)
- [ ] Plugin (`src/`)
- [ ] Tooling, tests or docs

## If this is a translation PR

- [ ] `python tools/lint-locale.py locale/ko/strings.json` passes
- [ ] Every `[token]`, `{variable}` and `<tag>` survives exactly as it is — a Korean
      particle glued to a `[token]` does not fail loudly, the game renders the
      token's *name* at the player
- [ ] Length checked **on screen**. The game's buttons are sized for English and
      Hangul is wider; the short strings are where this bites

## If this is a code PR

- [ ] `dotnet test tests/Combolands.Mod.Tests` passes
- [ ] `dotnet test tests/Combolands.Anchors` passes — needs the game installed
- [ ] `dotnet build src/Combolands.Mod -c Release` is clean, warnings included
      (the csproj treats them as errors)
- [ ] I ran it against the real game, not only the tests
- [ ] If it binds a new game member, `docs/ANCHORS.md` has a row for it and
      `tests/Combolands.Anchors/Catalogue.cs` checks it — CI fails if one has the
      other's ids

## If it touches autoplay

- [ ] Nothing outside `autoplay/Exec.cs` writes to the game
- [ ] `Snapshot.cs`, `Value.cs`, `Plan.cs`, `Rules.cs` and `Pace.cs` still carry no
      Unity and no game types — the tests link them as source, so one `using
      UnityEngine` takes the whole suite with it
- [ ] Any game method called has had its **preconditions** checked. Nearly every
      autoplay bug so far has been a method whose precondition was not: a destroyed
      object, a building not on the map, a shop skip called twice

## Game version this was checked on

<!-- The first lines of MelonLoader/Latest.log, or `python tools/fingerprint.py` -->
