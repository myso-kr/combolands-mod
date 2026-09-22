#!/usr/bin/env python3
"""Check a locale file against the game's own description parser.

Entities.Data/StringProcessor.cs assembles every building and item description by
splitting the string on WHITESPACE and looking at each word. That makes several
things that look like formatting into hard syntax, and getting one wrong does not
raise anything - the text just comes out missing a piece.

The grammar, read out of the decompiled parser:

    "[BREAK]"      ->  "@[BREAK]@"          before anything else
    "@"            ->  " @ "                so every @ becomes its own word
    split on whitespace, then per word:
      contains "@"         -> a line break. THE WORD ITSELF IS DISCARDED
      contains "{"         -> bold on, from this word
      contains "<invalid>" -> invalid styling on
      contains "["         -> a token. The text between the first [ and the first ]
                              is resolved to a game name, and THE REST OF THE WORD
                              IS NEVER APPENDED
      otherwise            -> plain text, with { } <invalid> </invalid> stripped
      contains "}"         -> bold off, after this word
      contains "</invalid>"-> invalid off

The rule that costs Korean translators the most is the token one. "[Farm]에" is a
single whitespace-delimited word, so the parser takes "Farm", resolves it, and drops
the "에" without a word. The particle simply vanishes from the sentence.

The game's own English breaks this rule 13 times (trailing commas and braces that
never render). Those are catalogued as KNOWN_ENGLISH_DEFECTS so the linter can be
strict about our text without failing on theirs.

Usage:
    python tools/lint-locale.py locale/ko/strings.json
    python tools/lint-locale.py locale/ko/strings.json --english generated/strings.en.json
"""

import argparse
import json
import re
import sys
from collections import Counter

# A bare token, or a token whose name is a string.Format argument filled in before
# the parser runs - "[{1}]" becomes "[Farm]" and is perfectly legal.
TOKEN_WORD = re.compile(r"^\[(?:[A-Za-z0-9_]+|\{\d+\})\]$")
TOKEN_ANY = re.compile(r"\[([A-Za-z0-9_]+)\]")
FORMAT_ARG = re.compile(r"\{(\d+)\}")

# Keys where the game's own English already loses characters to the token rule.
# Present so the linter can be strict without reporting Crux's bugs as ours.
KNOWN_ENGLISH_DEFECTS = {
    "BaitHook.Description", "BluePaint.Description", "Category.Frontier.Desc",
    "Fertilizer.Description", "GoldPaint.Description", "GreenPaint.Description",
    "HousingMandate.Description", "Pontoon.Description", "PurplePaint.Description",
    "RedPaint.Description", "Scythe.Description",
    "WhitePaint.Description", "Workshop.Description",
}


def words(text):
    """Split exactly the way StringProcessor does."""
    return text.replace("[BREAK]", "@[BREAK]@").replace("@", " @ ").split()


def check(key, ko, en):
    """Yield (severity, message) for one entry. en may be None."""

    # 2. The token rule. This is the one.
    for w in words(ko):
        if "[" in w and not TOKEN_WORD.match(w):
            yield "error", (
                "%r is not a bare token. Everything after the ']' is dropped - "
                "put Korean particles in their own word" % w
            )

    # 3. Braces switch bold on and off. Unbalanced means bold runs to the end of
    #    the string, or never starts.
    if ko.count("{") != ko.count("}"):
        yield "error", "unbalanced { } (%d open, %d close)" % (ko.count("{"), ko.count("}"))

    if ko.count("<invalid>") != ko.count("</invalid>"):
        yield "error", "unbalanced <invalid> </invalid>"

    if en is None:
        return

    # 4. "@" is an authored line break. It is safe to write glued to a word - the
    #    parser expands it to " @ " before splitting - but adding or dropping one
    #    changes where the description wraps.
    if ko.count("@") != en.count("@"):
        yield "warn", ("%d '@' line break(s) against %d in the English"
                       % (ko.count("@"), en.count("@")))

    # 5. Tokens are resolved by NAME, not by position, so reordering them for Korean
    #    word order is safe - but losing or inventing one is not.
    ken, kko = Counter(TOKEN_ANY.findall(en)), Counter(TOKEN_ANY.findall(ko))
    for name, n in (ken - kko).items():
        yield "error", "[%s] appears %d time(s) in English and is missing here" % (name, n)
    for name, n in (kko - ken).items():
        yield "warn", "[%s] appears %d extra time(s) - it is not in the English" % (name, n)

    # 6. {0} and friends are string.Format arguments, filled in before the parser
    #    ever sees them. A missing one throws; an invented one throws harder.
    fen, fko = set(FORMAT_ARG.findall(en)), set(FORMAT_ARG.findall(ko))
    for i in sorted(fen - fko):
        yield "error", "format argument {%s} is missing" % i
    for i in sorted(fko - fen):
        yield "error", "format argument {%s} does not exist in the English" % i


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("locale", help="locale/<lang>/strings.json")
    ap.add_argument("--english", help="generated/strings.en.json, for cross-checks")
    ap.add_argument("--quiet", action="store_true", help="only print problems")
    args = ap.parse_args()

    with open(args.locale, encoding="utf-8") as fh:
        ko = json.load(fh)

    en = None
    if args.english:
        with open(args.english, encoding="utf-8") as fh:
            en = json.load(fh)

    errors = warns = 0
    unknown = []

    for key in sorted(ko):
        source = None
        if en is not None:
            if key in en:
                source = en[key]
            elif key not in en.values() and key not in en:
                # Literal-English keys are legitimate (see i18n/Patches.cs), so an
                # unknown key is a note rather than a failure.
                unknown.append(key)

        for severity, message in check(key, ko[key], source):
            if key in KNOWN_ENGLISH_DEFECTS and severity == "error":
                severity = "warn"
            if severity == "error":
                errors += 1
            else:
                warns += 1
            print("%s: %s: %s" % (severity.upper(), key, message))

    if unknown and not args.quiet:
        print("\nnote: %d key(s) are not in the English dump - literal keys, or stale:"
              % len(unknown))
        for key in unknown[:10]:
            print("  " + key)

    if not args.quiet:
        print("\n%d entries, %d error(s), %d warning(s)" % (len(ko), errors, warns))

    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
