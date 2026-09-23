#!/usr/bin/env python3
# Copyright (c) You-Ri, 2026
"""Adds a shipped sample project's scenes to the legacy-compatibility fixtures.

    python update.py <archive.zip> [<archive.zip> ...]
    python update.py ../../../../../../dist/*Sample*@*.zip      # Virgo's own build output

Takes the `*.live.json` / `*.scene.json` out of each archive and writes them under a folder
named after the archive, which is how a release is addressed from
`LegacyProjectCompatibilityTests`. Everything else in the archive -- the avatars, the bundles,
the thumbnails -- is left where it is: it comes to tens of megabytes, and a migration is text in
and text out, so nothing in the tests would read it. To run a release against its real assets,
unpack the archive itself and open it in the app.

The scenes are rewritten as LF with two-space indent. They are kept as fixtures to be diffed
against each other, and a release saved by a different build otherwise differs in whitespace
everywhere, which buries the change that matters.

Adding a release is two steps: run this, then add a row to `kShipped` in the test saying what
that release had on stage. The test is what says a release is covered; a file on its own says
nothing.
"""

import json
import os
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE_SUFFIXES = (".live.json", ".scene.json")


def add(archive):
    name = os.path.basename(archive)
    if name.lower().endswith(".zip"):
        name = name[:-4]

    with zipfile.ZipFile(archive) as z:
        scenes = [n for n in z.namelist() if n.endswith(SCENE_SUFFIXES)]
        if not scenes:
            print("  no scenes in %s -- skipped" % name)
            return 0

        out = os.path.join(HERE, name)
        os.makedirs(out, exist_ok=True)
        for entry in scenes:
            # utf-8-sig: some releases wrote a BOM, and it is not part of the scene.
            parsed = json.loads(z.read(entry).decode("utf-8-sig"))
            text = json.dumps(parsed, ensure_ascii=False, indent=2) + "\n"
            with open(os.path.join(out, os.path.basename(entry)), "w",
                      encoding="utf-8", newline="\n") as f:
                f.write(text)
            version = (parsed.get("metadata") or {}).get("packageVersion", "?")
            print("  %s/%s  (saved by %s)" % (name, os.path.basename(entry), version))
        return len(scenes)


def main(argv):
    if not argv:
        print(__doc__)
        return 2

    total = 0
    for archive in argv:
        if not os.path.isfile(archive):
            print("not a file: %s" % archive, file=sys.stderr)
            return 1
        total += add(archive)

    print("%d scene(s) written. Now add the matching rows to kShipped." % total)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
