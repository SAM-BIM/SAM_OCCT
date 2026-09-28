"""Guard rails for the icon redesign diff.

  python check_source.py [base]      (default base: origin/sow/2026-Q3)

1. Vendored design-system files are byte-identical to SAM-BIM/SAM#166 @ cf4d924a (SHA-256).
2. Every changed line in a component .cs file is an icon-token swap inside an `Icon` getter
   (`<holder>.OLD` -> `<holder>.SAM_GH_*`); generated Resources.Designer.cs / holder blocks are excluded.
3. The set of ComponentGuid values, names, nicknames, categories and exposures is unchanged vs base.
"""
import hashlib
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

VENDORED = {  # file -> SHA-256 (LF line endings) of SAM/design/grasshopper-icons/tools/<source> at SAM#166 head cf4d924a
    "icons.py": "5ca1bfee3c0f19e55babff06120eee62c4a7468b141b608e8c386deaa998f0e7",
    "render.py": "8a87f66c45e0bb3e04cc613f2b0f28a8cb8df951a7daad95bdec8e3851060a3f",
    "sam_classify.py": "317064816c964e2268e9406aee9ca5f7d82c2bdb34da9c3225a344ca2464b864",  # SAM classify.py
}
TOKEN = re.compile(r"\b(?:\w+\.)+\w+")  # any dotted chain, e.g. Properties.Resources.SAM_Small3
KEEP_RE = re.compile(r'ComponentGuid|base\(\s*"|GH_Exposure|Category|NickName|new Guid\(|new\("')


def git(*args):
    return subprocess.run(["git", "-C", REPO, *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def main(base):
    ok = True
    for f, h in VENDORED.items():
        got = hashlib.sha256(open(os.path.join(HERE, f), "rb").read().replace(b"\r\n", b"\n")).hexdigest()  # autocrlf-safe
        print(f"vendored {f}: {'OK' if got == h else 'MODIFIED'}")
        ok &= got == h
    files = [f for f in git("diff", "--name-only", base, "--", "*.cs").split("\n") if f and not f.startswith("design/")]
    swaps, bad = 0, []
    for f in files:
        if f.endswith("Resources.Designer.cs") or f.startswith("design/"):
            continue
        diff = git("diff", "-U0", base, "--", f)
        if "// <sam-gh-icons>" in diff:  # generated holder block: only additions inside the markers
            block = re.findall(r"^[-+](?![-+]).*$", diff, re.M)
            inside, outside = False, []
            for line in block:
                if "// <sam-gh-icons>" in line:
                    inside = True
                if not inside and line.strip() not in ("+", "-"):
                    outside.append(line)
                if "// </sam-gh-icons>" in line:
                    inside = False
            if outside:
                bad.append((f, outside[:4]))
            continue
        minus = re.findall(r"^-(?!--)(.*)$", diff, re.M)
        plus = re.findall(r"^\+(?!\+\+)(.*)$", diff, re.M)
        if len(minus) != len(plus):
            bad.append((f, "added/removed lines"))
            continue
        now = open(os.path.join(REPO, f), encoding="utf-8-sig").read()
        getters = [m.end() for m in re.finditer(r"Bitmap\s+Icon\b", now)]

        def in_icon_getter(line):  # the line sits inside an `Icon` getter (single- or multi-line form)
            i = now.find(line.strip())
            return i >= 0 and any(0 <= i - g <= 300 for g in getters)
        for a, b in zip(minus, plus):
            if ("Icon" not in a and not in_icon_getter(b)) or TOKEN.sub("X", a) != TOKEN.sub("X", b) or "SAM_GH_" not in b or KEEP_RE.search(a):
                bad.append((f, a.strip(), b.strip()))
            else:
                swaps += 1
    print(f"component .cs files changed: {len(files)}; icon-token swaps: {swaps}; non-icon changes: {len(bad)}")
    for b in bad[:20]:
        print("  NON-ICON CHANGE:", b)
    ok &= not bad

    def guids(rev):
        out = set()
        listing = git("ls-tree", "-r", "--name-only", rev) if rev else git("ls-files")
        for f in listing.split("\n"):
            if f.endswith(".cs") and not f.startswith("design/"):
                try:
                    t = git("show", f"{rev}:{f}") if rev else open(os.path.join(REPO, f), encoding="utf-8-sig").read()
                except (subprocess.CalledProcessError, FileNotFoundError):
                    continue
                out |= {(f, m.group(0)) for m in re.finditer(r'ComponentGuid[^"\n]*"[0-9a-fA-F-]+"', t)}
        return out
    g0, g1 = guids(base), guids(None)
    print(f"ComponentGuid declarations: base {len(g0)}, now {len(g1)} -> {'UNCHANGED' if g0 == g1 else 'CHANGED'}")
    ok &= g0 == g1
    print("RESULT:", "OK" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "origin/sow/2026-Q3"))
