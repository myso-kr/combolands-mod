#!/usr/bin/env python3
"""Check the documents against what the repository actually does.

Only claims a machine can settle. The things worth checking are the ones a reader
acts on and cannot verify for themselves:

  - the archive layout, because a user reads it standing in front of their game
    folder with a zip open
  - the keys in locale/ko/strings.json against the count the README publishes
  - that every anchor id the README or CHANGELOG cites exists in docs/ANCHORS.md

Usage:
    python tools/check-docs.py
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8-sig") as handle:
        return handle.read()


def error(path, message):
    print("::error file=%s::%s" % (path, message), file=sys.stderr)


def archive_layout_is_documented():
    """Everything package.py ships is named in the README."""
    tool = read("tools", "package.py")
    readme = read("README.md")

    targets = set(re.findall(r'\("[^"]+",\s*"([^"]+)"\)', tool))
    if not targets:
        error("tools/package.py", "no archive layout found - this check is not reading it")
        return False

    missing = sorted(t for t in targets
                     if t not in readme and t.split("/")[-1] not in readme)
    for target in missing:
        error("README.md", "%s ships in the archive but the README never mentions it" % target)

    if not missing:
        print("%d archive entries, all named in the README" % len(targets))
    return not missing


def string_count_is_honest():
    """The number the README publishes is the number of keys that ship."""
    readme = read("README.md")

    actual = len(json.loads(read("locale", "ko", "strings.json")))
    claimed = [int(n.replace(",", "")) for n in re.findall(r"([\d,]{3,})\s+(?:strings|entries)", readme)]

    if not claimed:
        print("the README publishes no string count - nothing to check")
        return True

    wrong = [n for n in claimed if n != actual]
    for number in wrong:
        error("README.md", "says %d strings; locale/ko/strings.json has %d" % (number, actual))

    if not wrong:
        print("%d strings, and the README agrees" % actual)
    return not wrong


def cited_anchors_exist():
    """An anchor id in prose is a pointer. A pointer to nothing is worse than none."""
    catalogue = set(re.findall(r"^\|\s*([A-Z]\d{1,2})\s*\|", read("docs", "ANCHORS.md"), re.M))
    if not catalogue:
        error("docs/ANCHORS.md", "no anchor rows found - this check is not reading the table")
        return False

    ok = True
    for name in ("README.md", "CHANGELOG.md", "docs/PLAN.md", "docs/CONVENTIONS.md"):
        text = read(*name.split("/"))
        # "row P17", "anchor C10", "(A1)" - cited, not merely a word that looks like one.
        for cited in set(re.findall(r"(?:row|anchor|anchors)\s+([A-Z]\d{1,2})\b", text)):
            if cited not in catalogue:
                error(name, "cites anchor %s, which has no row in docs/ANCHORS.md" % cited)
                ok = False

    if ok:
        print("%d anchors documented, every citation resolves" % len(catalogue))
    return ok


def main():
    checks = [archive_layout_is_documented, string_count_is_honest, cited_anchors_exist]
    return 0 if all([check() for check in checks]) else 1


if __name__ == "__main__":
    sys.exit(main())
