"""Build this repository's SAM GH icons from manifest.json.

  python build.py  -> svg/ (canonical), png/24/ (production), review/contact_sheet.png (+.html),
                      review/REVIEW.md (collisions + shared icons)
Deterministic: output depends only on manifest.json + icons.py + icons_ext.py.
Fails (exit 1) if two different icon ids rasterise to identical pixels.
"""
import collections
import hashlib
import html
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import icons  # noqa: E402
import icons_ext  # noqa: E402,F401  (registers the SAM-BIM extension glyphs)
import render  # noqa: E402

DESIGN = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_NAME = os.path.basename(os.path.dirname(os.path.dirname(DESIGN)))
SVG = os.path.join(DESIGN, "svg")
PNG = os.path.join(DESIGN, "png", "24")
REVIEW = os.path.join(DESIGN, "review")

CSS = """
*{box-sizing:border-box}body{margin:0;background:#fff;color:#111;font:12px/1.3 'Segoe UI',system-ui,sans-serif;padding:20px 24px;width:%dpx}
h1{font-size:18px;margin:0 0 2px;text-transform:uppercase;letter-spacing:.02em}.bar{height:5px;width:120px;background:linear-gradient(90deg,#D32F2F 33%%,#F6C90E 33%% 66%%,#1565C0 66%%);margin:6px 0 10px}
h2{font-size:13px;margin:16px 0 6px;border-bottom:2px solid #111;padding-bottom:3px;text-transform:uppercase;letter-spacing:.03em}
.sub{color:#424242;margin:0 0 8px}.grid{display:grid;grid-template-columns:repeat(%d,1fr);gap:6px}
.c{border:1px solid #BDBDBD;padding:6px;display:flex;gap:8px;align-items:center;min-height:84px}.c.new{border-color:#277A45;border-width:2px}
.n{display:flex;flex-direction:column;gap:3px;align-items:center}
.gh{width:30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:3px}
.l{background:linear-gradient(#e6e6e6,#c9c9c9);border:1px solid #8a8a8a}.o{background:linear-gradient(#ffc680,#f0962a);border:1px solid #9a5a10}
.d{background:linear-gradient(#4a4a4a,#2e2e2e);border:1px solid #111}
img.px{image-rendering:pixelated}.t{font-size:10.5px;word-break:break-word;line-height:1.25}.t b{font-size:11px}.t i{color:#424242;font-style:normal}
"""


def display_name(r):
    n = r["name"] or ""
    m = re.match(r"^\{typeof\((?:[\w:.]+\.)?(\w+)\)\.Name\}$", n)
    return (m.group(1) if m else n) + (" (param)" if r["kind"] == "param" else "")


def label(rows):
    r = rows[0]
    names = "<br>".join(html.escape(display_name(x)) + (" <i>(hidden/obsolete)</i>" if x["obsolete"] or "hidden" in (x["exposure"] or "") else "") for x in rows)
    ext = " · <b style='color:#277A45'>new glyph</b>" if r["glyph_origin"] != "SAM" else ""
    ext += " · <b style='color:#277A45'>new verb</b>" if r["badge_origin"] not in (None, "SAM") else ""
    return (f"{names}<br><i>{r['icon_id']} · {r['object']} · {r['op'] or 'param'}{' · plural' if r['plural'] else ''}"
            f"{' · library' if r['container'] else ''}{ext}</i>")


def sheet(title, subtitle, groups, out_png, cols=4, big=72, width=1500):
    """groups: [(heading, [(png24, label_html, is_new)])]; native 24 px on GH normal / warning / dark bodies + 3x."""
    base = os.path.dirname(os.path.abspath(out_png))
    parts = [f"<h1>{html.escape(title)}</h1><div class='bar'></div><p class='sub'>{subtitle}</p>"]
    rows = 0
    for head, items in groups:
        parts.append(f"<h2>{html.escape(head)} <span style='font-weight:400;color:#424242'>({len(items)})</span></h2><div class='grid'>")
        for png, lab, new in items:
            rel = os.path.relpath(png, base).replace("\\", "/")
            tiles = "".join(f"<span class='gh {k}'><img src='{rel}' width=24 height=24></span>" for k in "lod")
            parts.append(f"<div class='c{' new' if new else ''}'><div class='n'>{tiles}</div>"
                         f"<img class='px' src='{rel}' width={big} height={big}><div class='t'>{lab}</div></div>")
        parts.append("</div>")
        rows += (len(items) + cols - 1) // cols
    doc = (f"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{html.escape(title)}</title>"
           f"<style>{CSS % (width - 48, cols)}</style></head><body>{''.join(parts)}</body></html>")
    html_path = os.path.splitext(out_png)[0] + ".html"
    open(html_path, "w", encoding="utf-8", newline="\n").write(doc)
    render._shot(os.path.abspath(html_path), os.path.abspath(out_png), width, 90 + len(groups) * 44 + rows * 118, scale=1)
    render._trim(out_png)


def main():
    rows = json.load(open(os.path.join(DESIGN, "manifest.json"), encoding="utf-8"))
    ids = {}
    for r in rows:
        ids.setdefault(r["icon_id"], r)
    os.makedirs(SVG, exist_ok=True)
    for fn in os.listdir(SVG):  # svg/ mirrors the manifest exactly
        if fn.endswith(".svg") and fn[:-4] not in ids:
            os.remove(os.path.join(SVG, fn))
    svgs, pngs = [], []
    for iid, r in sorted(ids.items()):
        svg = icons.icon_svg(r["object"], r["op"], plural=r["plural"], container=r["container"], comment=f"SAM GH icon {iid}")
        p = os.path.join(SVG, iid + ".svg")
        open(p, "w", encoding="utf-8", newline="\n").write(svg)
        svgs.append(p)
        pngs.append(os.path.join(PNG, iid + ".png"))
    if os.path.isdir(PNG):
        for fn in os.listdir(PNG):
            if fn[:-4] not in ids:
                os.remove(os.path.join(PNG, fn))
    render.rasterise(svgs, pngs)

    # ---- collision check: identical 24 px pixels for different icon ids
    by_hash = collections.defaultdict(list)
    for iid in ids:
        by_hash[hashlib.sha1(open(os.path.join(PNG, iid + ".png"), "rb").read()).hexdigest()].append(iid)
    dup = sorted(v for v in by_hash.values() if len(v) > 1)

    # ---- contact sheet: one tile per icon (objects sharing it listed together), grouped by object family
    by_icon = collections.OrderedDict()
    for r in sorted(rows, key=lambda r: (r["kind"] == "param", r["object"], r["op"] or "", r["icon_id"], r["name"] or "")):
        by_icon.setdefault(r["icon_id"], []).append(r)
    groups = collections.OrderedDict()
    for iid, rs in by_icon.items():
        head = "Parameters: object glyph only, no badge" if rs[0]["kind"] == "param" else rs[0]["object"]
        groups.setdefault(head, []).append((os.path.join(PNG, iid + ".png"), label(rs),
                                            rs[0]["glyph_origin"] != "SAM" or rs[0]["badge_origin"] not in (None, "SAM")))
    os.makedirs(REVIEW, exist_ok=True)
    n_new = sum(1 for r in ids.values() if r["glyph_origin"] != "SAM")
    sheet(f"{REPO_NAME} - SAM GH icons", f"{len(rows)} Grasshopper objects · {len(ids)} distinct icons ({n_new} on new SAM-BIM glyphs, green frame). "
          "Native 24 px on GH normal / orange-warning / dark bodies, plus 3x nearest-neighbour for inspection.",
          list(groups.items()), os.path.join(REVIEW, "contact_sheet.png"))

    # ---- review report
    shared = [(iid, rs) for iid, rs in by_icon.items() if len(rs) > 1]
    lines = [f"# {REPO_NAME} - icon review", "",
             f"- Objects: **{len(rows)}** ({sum(r['kind'] == 'component' for r in rows)} components, {sum(r['kind'] == 'param' for r in rows)} params)",
             f"- Distinct icons: **{len(ids)}** ({len(ids) - n_new} on SAM glyphs, {n_new} on SAM-BIM extension glyphs)",
             f"- Identical-pixel groups (different icon ids): **{len(dup)}** {dup if dup else ''}",
             f"- Icons shared by several objects (intentional: qualifier variants / same noun+verb): **{len(shared)}**", ""]
    for iid, rs in shared:
        lines.append(f"  - `{iid}`: " + ", ".join(display_name(r) for r in rs))
    open(os.path.join(REVIEW, "REVIEW.md"), "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")

    for r in rows:
        r["status"] = "redesigned" if r["status"] == "classified" else r["status"]
    json.dump(rows, open(os.path.join(DESIGN, "manifest.json"), "w", encoding="utf-8"), indent=1)
    import csv
    with open(os.path.join(DESIGN, "manifest.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    print(f"{len(rows)} objects, {len(ids)} icons ({n_new} new-glyph), identical-pixel groups: {dup}")
    return 1 if dup else 0


if __name__ == "__main__":
    sys.exit(main())
