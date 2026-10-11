"""Inventory every Grasshopper document object (components + params) in this repository.

Same parser as SAM/design/grasshopper-icons/tools/inventory.py (SAM#166), generalised to any
repository layout: every .cs file under a project whose .csproj references Grasshopper is scanned.
An object is inventoried when a non-abstract class declares `ComponentGuid` in a file the project compiles
(files excluded by `<Compile Remove="..."/>` are skipped).
Writes ../manifest_raw.json.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(DESIGN))
SKIP_DIRS = {"obj", "bin", ".git", "packages", "design", "build"}

CLASS_RE = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*((?:public|internal|private|protected|abstract|sealed|static|partial|\s)+)class\s+(\w+)(?:<[^>{]*>)?\s*(?::\s*([^{\n]+))?", re.M)
GUID_RE = re.compile(r"ComponentGuid\s*(?:=>|\{\s*get\s*\{\s*return)\s*new\s*(?:Guid\s*)?\(\s*\"([0-9a-fA-F-]+)\"")  # new Guid("..") and target-typed new("..")
ICON_RE = re.compile(r"override\s+(?:System\.Drawing\.)?Bitmap\s+Icon\b(.{0,300})", re.S)
ICON_TOKEN_RE = re.compile(r"\b(?:Properties\.)?Resources\.(\w+)|\b(\w+Icon)\.(\w+)")
EXPOSURE_RE = re.compile(r"override\s+GH_Exposure\s+Exposure\s*=>\s*([^;]+);")


def long_path(p):
    p = os.path.abspath(p)
    return "\\\\?\\" + p if os.name == "nt" and not p.startswith("\\\\") else p


def base_args(text, pos):
    """Split the argument list of the `base(` call starting at/after pos (string- and paren-aware)."""
    i = text.index("(", pos) + 1
    args, depth, cur, instr = [], 0, "", False
    while i < len(text):
        ch = text[i]
        if instr:
            cur += ch
            if ch == "\\":
                cur += text[i + 1]; i += 1
            elif ch == '"':
                instr = False
        elif ch == '"':
            instr = True; cur += ch
        elif ch in "([{":
            depth += 1; cur += ch
        elif ch in ")]}":
            if depth == 0:
                args.append(cur.strip()); return args
            depth -= 1; cur += ch
        elif ch == "," and depth == 0:
            args.append(cur.strip()); cur = ""
        else:
            cur += ch
        i += 1
    return args


def lit(a):
    a = a.strip()
    if re.fullmatch(r'"(?:[^"\\]|\\.)*"', a):
        return a[1:-1]
    return "{" + a + "}"


def compile_removed(pdir):
    """Files excluded from compilation by the project's `<Compile Remove="..."/>` globs (SDK-style csproj)."""
    import fnmatch
    csproj = [f for f in os.listdir(pdir) if f.endswith(".csproj")]
    pats = []
    for f in csproj:
        txt = open(long_path(os.path.join(pdir, f)), encoding="utf-8-sig", errors="replace").read()
        pats += [m.replace("\\", "/") for m in re.findall(r'<Compile\s+Remove="([^"]+)"', txt)]
    def removed(rel):
        rel = rel.replace("\\", "/")
        return any(fnmatch.fnmatch(rel, p) or fnmatch.fnmatch(rel, p.replace("**/", "")) for p in pats)
    return removed


def gh_projects():
    """{project dir (abs): project name} for every non-test .csproj that references Grasshopper."""
    out = {}
    for root, dirs, files in os.walk(REPO):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
        for f in files:
            if f.endswith(".csproj") and "Test" not in f:
                txt = open(long_path(os.path.join(root, f)), encoding="utf-8-sig", errors="replace").read()
                if re.search(r"Grasshopper", txt, re.I) and re.search(r'Include="Grasshopper"|Grasshopper\.dll|GH_IO|"Grasshopper"', txt):
                    out[root] = f[:-7]
    return out


def main():
    items = []
    for pdir, proj in sorted(gh_projects().items(), key=lambda kv: kv[1]):
        removed = compile_removed(pdir)
        for root, dirs, files in os.walk(pdir):
            dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
            for f in sorted(files):
                if not f.endswith(".cs") or f.endswith(".Designer.cs"):
                    continue
                path = os.path.join(root, f)
                if removed(os.path.relpath(path, pdir)):
                    continue  # not compiled (<Compile Remove>): not a GH object of this plugin
                src = open(long_path(path), encoding="utf-8-sig").read()
                if "ComponentGuid" not in src:
                    continue
                # strip // comments to avoid template/commented code
                code = re.sub(r"(?m)(^|[\s;{}])//[^\n]*", r"\1", src)
                classes = list(CLASS_RE.finditer(code))
                for i, cm in enumerate(classes):
                    body = code[cm.end(): classes[i + 1].start() if i + 1 < len(classes) else len(code)]
                    g = GUID_RE.search(body)
                    if not g or "abstract" in cm.group(1):
                        continue
                    ic = ICON_RE.search(body)
                    tok = ICON_TOKEN_RE.search(ic.group(1)) if ic else None
                    ex = EXPOSURE_RE.search(body)
                    ctor = re.search(r"\b" + cm.group(2) + r"\s*\(\s*\)\s*(?=:\s*base\()", body)
                    b = base_args(body, ctor.end()) if ctor else None
                    if b and b[-1].startswith("GH_ParamAccess"):
                        b = b[:-1]
                    obsolete = bool(re.search(r"\[Obsolete", code[max(0, cm.start() - 200): cm.end()])) or "_Obsolete" in cm.group(2)
                    items.append({
                        "project": proj,
                        "project_dir": os.path.relpath(pdir, REPO).replace("\\", "/"),
                        "class": cm.group(2),
                        "base_type": (cm.group(3) or "").strip(),
                        "guid": g.group(1).lower(),
                        "name": lit(b[0]) if b else None,
                        "nickname": lit(b[1]) if b and len(b) > 1 else None,
                        "category": lit(b[-2]) if b and len(b) >= 4 else None,
                        "subcategory": lit(b[-1]) if b and len(b) >= 4 else None,
                        "icon_expr": (tok.group(1) or tok.group(3)) if tok else None,
                        "icon_holder": (tok.group(2) if tok and tok.group(2) else ("Resources" if tok else None)),
                        "icon_via_bytes": bool(ic and ("ToBitmap" in ic.group(1) or "MemoryStream" in ic.group(1))),
                        "exposure": ex.group(1).strip() if ex else None,
                        "obsolete": obsolete,
                        "source": os.path.relpath(path, REPO).replace("\\", "/"),
                    })
    out = os.path.join(DESIGN, "manifest_raw.json")
    json.dump(items, open(out, "w", encoding="utf-8"), indent=1)
    print(len(items), "objects ->", out)


if __name__ == "__main__":
    sys.exit(main())
