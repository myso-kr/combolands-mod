#!/usr/bin/env python3
"""Which game build a release was checked against.

`generated/fingerprint.json` is our own record of what was verified and when - not
anything taken out of the game - which is why it is the one thing under `generated/`
that is committed. Release notes have to carry it: this mod binds to signatures
inside `Assembly-CSharp.dll`, so "it stopped working" and "the game updated" look
identical from the outside unless the notes say which build was the good one.

    python tools/fingerprint.py              # v1.0.6 24989173 6000.0.66f2
    python tools/fingerprint.py --markdown   # the table row the release notes use
    python tools/fingerprint.py --field game_version
"""

import argparse
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, "generated", "fingerprint.json")

MISSING = "not recorded"


def newest():
    """The most recently observed build marked verified.

    Not simply the last entry: the file is a record and entries can be added out of
    order, while `observed` is a date and cannot be.
    """
    try:
        with open(PATH, encoding="utf-8") as handle:
            builds = json.load(handle).get("builds", [])
    except (OSError, ValueError) as error:
        print("::error file=generated/fingerprint.json::%s" % error, file=sys.stderr)
        return {}

    verified = [b for b in builds if b.get("status") == "verified"]
    if not verified:
        return {}
    return max(verified, key=lambda b: str(b.get("observed", "")))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--markdown", action="store_true", help="a one-row table")
    parser.add_argument("--field", help="print a single field")
    args = parser.parse_args()

    build = newest()
    game = build.get("game_version", MISSING)
    steam = build.get("steam_buildid", MISSING)
    unity = build.get("unity", MISSING)

    if args.field:
        print(build.get(args.field, MISSING))
    elif args.markdown:
        print("| Game | Steam buildid | Unity |")
        print("|---|---|---|")
        print("| %s | %s | %s |" % (game, steam, unity))
    else:
        print("%s %s %s" % (game, steam, unity))

    # A release whose notes cannot say what it was checked against is one nobody can
    # triage, so this is an error rather than a blank.
    return 1 if not build else 0


if __name__ == "__main__":
    sys.exit(main())
