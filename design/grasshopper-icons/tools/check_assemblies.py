"""Verify the built Grasshopper assemblies embed every SAM_GH_* icon their manifest rows need.

Format-agnostic (byte[] resx, Bitmap resx via System.Resources.Extensions, or manifest resources):
  * every required resource name must occur in the assembly (UTF-16LE resx name table or UTF-8 manifest name), and
  * the assembly must embed at least as many 24x24 PNGs as required icons (legacy odd-size PNGs are reported).
Usage: python check_assemblies.py <dir> [<dir> ...]   (searched recursively; newest .gha/.dll per assembly wins)
"""
import io
import json
import os
import re
import sys

from PIL import Image

DESIGN = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(os.path.dirname(DESIGN))
SIG, IEND = b"\x89PNG\r\n\x1a\n", b"IEND\xaeB`\x82"


def pngs(blob):
    i = 0
    while True:
        i = blob.find(SIG, i)
        if i < 0:
            return
        j = blob.find(IEND, i)
        yield Image.open(io.BytesIO(blob[i: j + len(IEND)]))
        i = j


def assembly_name(project_dir):
    pdir = os.path.join(REPO, project_dir)
    csproj = [f for f in os.listdir(pdir) if f.endswith(".csproj")][0]
    m = re.search(r"<AssemblyName>([^<]+)</AssemblyName>", open(os.path.join(pdir, csproj), encoding="utf-8-sig").read())
    return m.group(1).strip() if m else csproj[:-7]


def find(dirs, name):
    hits = []
    for d in dirs:
        for root, subdirs, files in os.walk(d):
            subdirs[:] = [x for x in subdirs if x not in ("obj", ".git", "packages")]  # skip reference assemblies (obj/*/ref, refint)
            hits += [os.path.join(root, f) for f in files if f in (name + ".gha", name + ".dll")]
    return max(hits, key=os.path.getmtime) if hits else None


def main(dirs):
    rows = json.load(open(os.path.join(DESIGN, "manifest.json"), encoding="utf-8"))
    need = {}
    for r in rows:
        need.setdefault(r["project_dir"], set()).add(r["resource"])
    fail = 0
    for pdir, names in sorted(need.items()):
        asm = assembly_name(pdir)
        path = find(dirs, asm)
        if not path:
            print(f"{asm}: NOT FOUND in {dirs} -> FAIL")
            fail += 1
            continue
        blob = open(path, "rb").read()
        missing = [n for n in names if n.encode("utf-16-le") not in blob and n.encode("utf-8") not in blob]
        imgs = list(pngs(blob))
        bad = [im.size for im in imgs if im.size != (24, 24)]
        ok = not missing and sum(im.size == (24, 24) for im in imgs) >= len(names)
        fail += not ok
        print(f"{asm} ({os.path.relpath(path, REPO) if path.startswith(REPO) else path}): {len(names)} required icons, "
              f"names missing {len(missing)}, embedded PNGs {len(imgs)} (legacy non-24x24: {len(bad)}) -> {'OK' if ok else 'FAIL'}")
    print("RESULT:", "OK" if not fail else f"FAIL ({fail} assemblies)")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or [os.path.join(REPO, "build")]))
