#!/usr/bin/env python3
"""Report how much of the game is translated, by the group a player would notice.

Counts against generated/strings.en.json, which is the game's own text and is not in
the repository - run the dump first (Config.DumpStrings, see docs/PLAN.md).

The grouping is by key shape rather than by count, because "1088 strings" says
nothing about whether the menu is readable. UI chrome is a hundred strings and all
of the first impression; building descriptions are the long tail.

Usage:
    python tools/status.py
    python tools/status.py --markdown      # the table README.md carries
"""

import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENGLISH = os.path.join(ROOT, "generated", "strings.en.json")
LOCALES = os.path.join(ROOT, "locale")

# (label, predicate). First match wins, so order is the priority to translate in.
GROUPS = [
    ("Names", lambda k: k.endswith(".Name")),
    ("Descriptions", lambda k: k.endswith(".Description") or k.endswith(".Desc")),
    ("UI", lambda k: True),
]


# A value with no letters outside its [tokens] has nothing for a translator to do -
# "[MoneyCount]" is the same string in every language. Counting those against
# coverage puts 100% out of reach and makes the percentage mean nothing.
TOKEN = re.compile(r"\[[^\]]*\]|\{[^}]*\}|<[^>]*>")


def translatable(value):
    return bool(re.search(r"[A-Za-z]", TOKEN.sub("", value)))


def group_of(key):
    for label, match in GROUPS:
        if match(key):
            return label
    return "UI"


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--markdown", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(ENGLISH):
        print("no %s - run the in-game dump first (Config.DumpStrings)" % ENGLISH,
              file=sys.stderr)
        return 2

    english = load(ENGLISH)
    languages = sorted(
        name for name in os.listdir(LOCALES)
        if os.path.isfile(os.path.join(LOCALES, name, "strings.json"))
    ) if os.path.isdir(LOCALES) else []

    if not languages:
        print("no locale/<lang>/strings.json found", file=sys.stderr)
        return 2

    labels = [label for label, _ in GROUPS]
    source = {k: v for k, v in english.items() if translatable(v)}
    skipped = len(english) - len(source)

    totals = {label: 0 for label in labels}
    for key in source:
        totals[group_of(key)] += 1

    rows = []
    for lang in languages:
        translated = load(os.path.join(LOCALES, lang, "strings.json"))
        done = {label: 0 for label in labels}
        for key, value in translated.items():
            if key in source and value.strip():
                done[group_of(key)] += 1
        covered = sum(done.values())
        rows.append((lang, done, covered))

    if args.markdown:
        print("| Language | " + " | ".join(labels) + " | Total |")
        print("|---" * (len(labels) + 2) + "|")
        for lang, done, covered in rows:
            cells = ["%d/%d" % (done[l], totals[l]) for l in labels]
            pct = 100.0 * covered / max(1, len(source))
            print("| `%s` | %s | %d/%d (%.1f%%) |"
                  % (lang, " | ".join(cells), covered, len(source), pct))
        return 0

    print("English source: %d keys (%d translatable, %d token-only)"
          % (len(english), len(source), skipped))
    for label in labels:
        print("  %-14s %d" % (label, totals[label]))
    print()
    for lang, done, covered in rows:
        print("%s: %d/%d (%.1f%%)"
              % (lang, covered, len(source), 100.0 * covered / max(1, len(source))))
        for label in labels:
            print("  %-14s %d/%d" % (label, done[label], totals[label]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
