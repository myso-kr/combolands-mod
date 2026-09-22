#!/usr/bin/env python3
"""The one version number, and everywhere it has to agree.

A mod that cannot say which build it is cannot be supported. MelonLoader prints
`MelonInfo` into every log, so that is the number a bug report will carry - but it is
a string literal in an attribute, which MSBuild cannot supply and nothing checks. It
had already drifted: the csproj said 0.1.0 while the log said 0.3.0.

So the csproj is the source, and this holds the rest to it:

    python tools/version.py              # print it
    python tools/version.py --check      # csproj, MelonInfo and CHANGELOG agree
    python tools/version.py --check v0.4.0   # ...and the tag being released agrees too

Exit 1 and a GitHub-annotated message on any disagreement, so CI and the release
workflow can both use it.
"""

import argparse
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSPROJ = os.path.join(ROOT, "src", "Combolands.Mod", "Combolands.Mod.csproj")
PLUGIN = os.path.join(ROOT, "src", "Combolands.Mod", "Plugin.cs")
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")

SEMVER = r"\d+\.\d+\.\d+"


def read(path):
    with open(path, encoding="utf-8-sig") as handle:
        return handle.read()


def error(path, message):
    rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
    print("::error file=%s::%s" % (rel, message), file=sys.stderr)


def declared():
    """The version in the csproj. This is the source; everything else follows it."""
    found = re.search(r"<Version>(%s)</Version>" % SEMVER, read(CSPROJ))
    if not found:
        error(CSPROJ, "no <Version>X.Y.Z</Version> - nothing can be released without one")
        sys.exit(1)
    return found.group(1)


def melon_info():
    """The version MelonLoader prints, which is the one a user will quote at you."""
    found = re.search(r'MelonInfo\(\s*typeof\(\w+\)\s*,\s*"[^"]*"\s*,\s*"(%s)"' % SEMVER, read(PLUGIN))
    return found.group(1) if found else None


def changelog_top():
    """The newest released heading, ignoring an Unreleased section."""
    for line in read(CHANGELOG).splitlines():
        found = re.match(r"^##\s+\[?(%s)\]?" % SEMVER, line)
        if found:
            return found.group(1)
    return None


def check(tag):
    version = declared()
    ok = True

    found = melon_info()
    if found is None:
        error(PLUGIN, "no MelonInfo version found - the log would not say what it is running")
        ok = False
    elif found != version:
        error(PLUGIN, "MelonInfo says %s, the csproj says %s" % (found, version))
        ok = False

    found = changelog_top()
    if found is None:
        error(CHANGELOG, "no released version heading found")
        ok = False
    elif found != version:
        error(CHANGELOG, "the newest entry is %s, the csproj says %s" % (found, version))
        ok = False

    if tag is not None:
        # A tag is how a release starts, so a tag that disagrees is a release of
        # something other than what it claims.
        wanted = tag[1:] if tag.startswith("v") else tag
        if wanted != version:
            print("::error::tag %s does not match version %s" % (tag, version), file=sys.stderr)
            ok = False

    if not ok:
        return 1

    print("%s - csproj, MelonInfo and CHANGELOG agree%s"
          % (version, " with the tag" if tag else ""))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", nargs="?", const="", metavar="TAG",
                        help="verify every declaration agrees, optionally with a tag")
    args = parser.parse_args()

    if args.check is None:
        print(declared())
        return 0

    return check(args.check or None)


if __name__ == "__main__":
    sys.exit(main())
