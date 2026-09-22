#!/usr/bin/env python3
"""Build the release archive.

This runs where the game is, and that is not an oversight to apologise for. The
plugin compiles against `Combolands_Data/Managed` - UnityEngine, uGUI and
TextMeshPro - and those are not ours to commit or to approximate. A runner could be
made to compile against *some* Unity of *some* version, and the DLL it produced
would be built against different metadata than the game ships. For a mod that binds
to signatures for a living, that is the worst kind of green tick.

So: CI checks everything that can be checked without the game, and the archive is
built on a machine that has one.

    python tools/package.py                  # build and verify, leaves dist/
    python tools/package.py --publish v0.4.0 # ...and upload it to that release

The font is redistributed inside the archive, so copying its OFL text is a condition
rather than a courtesy. Nothing here is copied with `|| true`; a missing licence
fails the build.

MelonLoader is deliberately not bundled. It is the user's install step, and
vendoring someone else's loader makes their bug reports ours.
"""

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
PROJECT = os.path.join(ROOT, "src", "Combolands.Mod", "Combolands.Mod.csproj")
BUILT = os.path.join(ROOT, "src", "Combolands.Mod", "bin", "Release", "CombolandsMod.dll")

# What goes in, and where it lands relative to the game folder. A user unpacks this
# over their install, which is why the layout is the game's and not ours.
LAYOUT = [
    ("src/Combolands.Mod/bin/Release/CombolandsMod.dll", "Mods/CombolandsMod.dll"),
    ("locale/ko/strings.json",     "UserData/Combolands/locale/ko/strings.json"),
    ("locale/fonts/Galmuri11.ttf", "UserData/Combolands/fonts/Galmuri11.ttf"),
    ("locale/fonts/OFL-Galmuri.txt", "licenses/OFL-Galmuri.txt"),
    ("README.md",        "README.md"),
    ("LICENSE",          "LICENSE"),
    ("NOTICE",           "NOTICE"),
    ("THIRD-PARTY.md",   "THIRD-PARTY.md"),
    ("CHANGELOG.md",     "CHANGELOG.md"),
]

# The ones whose absence is a licence problem rather than an inconvenience, listed
# separately so the check reads as what it is.
REQUIRED = [
    "Mods/CombolandsMod.dll",
    "UserData/Combolands/locale/ko/strings.json",
    "UserData/Combolands/fonts/Galmuri11.ttf",
    "licenses/OFL-Galmuri.txt",
    "LICENSE",
    "NOTICE",
    "THIRD-PARTY.md",
]


def run(command, **kwargs):
    print("$", " ".join(command))
    return subprocess.run(command, cwd=ROOT, check=True, **kwargs)


def version():
    out = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "version.py")],
                         cwd=ROOT, check=True, capture_output=True, text=True)
    return out.stdout.strip()


def build():
    """Release configuration, and the warnings-as-errors the csproj already sets."""
    run(["dotnet", "build", PROJECT, "-c", "Release", "--nologo"])
    if not os.path.isfile(BUILT):
        sys.exit("::error::no build output at %s" % BUILT)


def assemble(name):
    out = os.path.join(DIST, name)
    if os.path.isdir(out):
        shutil.rmtree(out)

    for source, target in LAYOUT:
        src = os.path.join(ROOT, source)
        if not os.path.isfile(src):
            sys.exit("::error::%s is not here, so the archive would be incomplete" % source)

        dst = os.path.join(out, target)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)

    for required in REQUIRED:
        path = os.path.join(out, required)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            sys.exit("::error::%s is missing from the distribution" % required)

    return out


def archive(folder, name):
    """A zip built by hand rather than shutil.make_archive.

    Deterministic order, forward slashes, and no absolute paths - so two builds of
    the same commit produce the same bytes and the published sha256 means something.
    """
    path = os.path.join(DIST, name + ".zip")
    if os.path.exists(path):
        os.remove(path)

    entries = []
    for base, _, files in os.walk(folder):
        for filename in files:
            full = os.path.join(base, filename)
            entries.append((os.path.relpath(full, folder).replace(os.sep, "/"), full))
    entries.sort()

    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
        for arcname, full in entries:
            info = zipfile.ZipInfo(name + "/" + arcname, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(full, "rb") as handle:
                zf.writestr(info, handle.read())

    return path


def checksum(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)

    line = "%s  %s\n" % (digest.hexdigest(), os.path.basename(path))
    with open(path + ".sha256", "w", encoding="utf-8", newline="\n") as handle:
        handle.write(line)
    return line.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", metavar="TAG",
                        help="upload the archive to that GitHub release once it is built")
    parser.add_argument("--skip-build", action="store_true",
                        help="package whatever is already in bin/Release")
    args = parser.parse_args()

    tag = args.publish
    run([sys.executable, os.path.join(ROOT, "tools", "version.py"), "--check"]
        + ([tag] if tag else []))

    if not args.skip_build:
        build()

    name = "combolands-mod-v%s" % version()
    os.makedirs(DIST, exist_ok=True)

    folder = assemble(name)
    path = archive(folder, name)
    print("\n" + checksum(path))
    print(os.path.relpath(path, ROOT).replace(os.sep, "/"))

    if tag:
        # --clobber so a re-upload after a fix replaces rather than fails, which is
        # what you want at the one moment you are least patient.
        run(["gh", "release", "upload", tag, path, path + ".sha256", "--clobber"])
        print("\nuploaded to %s - publish the draft when the release notes look right" % tag)

    return 0


if __name__ == "__main__":
    sys.exit(main())
