"""Rasterise SVGs to exact 24x24 PNG and build review sheets, using headless Edge + PIL.

Headless Edge is the reference renderer (same engine family as the WebView used to review
the sheets). Output PNGs are 24x24 RGBA with straight alpha.
"""
import html
import os
import subprocess
import tempfile

from PIL import Image

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
PITCH = 32  # sprite cell (icons placed at integer offsets -> no resampling)


def _shot(html_path, png_path, w, h, scale=1):
    profile = tempfile.mkdtemp(prefix="edge-prof-")  # isolated profile: safe to run in parallel
    try:
        _shot_with(html_path, png_path, w, h, scale, profile)
    finally:
        import shutil
        shutil.rmtree(profile, ignore_errors=True)


def _shot_with(html_path, png_path, w, h, scale, profile):
    subprocess.run([
        EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--user-data-dir={profile}",
        f"--force-device-scale-factor={scale}", "--default-background-color=00000000",
        "--allow-file-access-from-files", f"--window-size={w},{h}", "--virtual-time-budget=5000",
        f"--screenshot={png_path}", "file:///" + html_path.replace("\\", "/"),
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def rasterise(svg_paths, out_paths, size=24, workers=8):
    """svg_paths[i] -> out_paths[i] at size x size.

    Each icon is rendered alone at the same page position (0,0), so its pixels depend only on its
    own SVG. (A shared sprite made antialiasing depend on the icon's position in the sprite.)
    """
    from concurrent.futures import ThreadPoolExecutor

    def one(pair):
        svg, out = pair
        with tempfile.TemporaryDirectory() as tmp:
            page = os.path.join(tmp, "one.html")
            src = "file:///" + os.path.abspath(svg).replace("\\", "/")
            open(page, "w", encoding="utf-8").write(
                "<!DOCTYPE html><html><body style='margin:0;background:transparent'>"
                f"<img src=\"{src}\" width=\"{size}\" height=\"{size}\" style=\"position:absolute;left:0;top:0\"></body></html>")
            shot = os.path.join(tmp, "one.png")
            _shot(page, shot, 64, 64)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            Image.open(shot).convert("RGBA").crop((0, 0, size, size)).save(out, optimize=True)

    with ThreadPoolExecutor(max_workers=workers) as ex:
        list(ex.map(one, zip(svg_paths, out_paths)))


def rasterise_sprite(svg_paths, out_paths, size=24):
    """Legacy sprite rasteriser (position-sensitive antialiasing; kept for reference only)."""
    cols = 40
    rows = (len(svg_paths) + cols - 1) // cols
    with tempfile.TemporaryDirectory() as tmp:
        page = os.path.join(tmp, "sprite.html")
        imgs = []
        for i, p in enumerate(svg_paths):
            x, y = (i % cols) * PITCH, (i // cols) * PITCH
            src = "file:///" + os.path.abspath(p).replace("\\", "/")
            imgs.append(f'<img src="{src}" width="{size}" height="{size}" style="position:absolute;left:{x}px;top:{y}px">')
        open(page, "w", encoding="utf-8").write(
            "<!DOCTYPE html><html><body style='margin:0;background:transparent'>" + "".join(imgs) + "</body></html>")
        shot = os.path.join(tmp, "sprite.png")
        _shot(page, shot, cols * PITCH, max(rows * PITCH, 64))
        sheet = Image.open(shot).convert("RGBA")
        for i, out in enumerate(out_paths):
            x, y = (i % cols) * PITCH, (i // cols) * PITCH
            os.makedirs(os.path.dirname(out), exist_ok=True)
            sheet.crop((x, y, x + size, y + size)).save(out, optimize=True)


CSS = """
:root{--ink:#111;--mut:#424242;--line:#BDBDBD}
*{box-sizing:border-box}body{margin:0;background:#fff;color:var(--ink);font:12px/1.3 'Segoe UI',system-ui,sans-serif;padding:20px 24px;width:%dpx}
h1{font-size:18px;margin:0 0 2px;text-transform:uppercase;letter-spacing:.02em}.bar{height:5px;width:120px;background:linear-gradient(90deg,#D32F2F 33%%,#F6C90E 33%% 66%%,#1565C0 66%%);margin:6px 0 10px}
h2{font-size:13px;margin:16px 0 6px;border-bottom:2px solid #111;padding-bottom:3px;text-transform:uppercase;letter-spacing:.03em}
.sub{color:var(--mut);margin:0 0 8px}.grid{display:grid;grid-template-columns:repeat(%d,1fr);gap:6px}
.c{border:1px solid var(--line);padding:6px;display:flex;gap:8px;align-items:center;min-height:%dpx}
.n{display:flex;flex-direction:column;gap:4px;align-items:center}
.gh{width:30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:3px}
.l{background:linear-gradient(#e6e6e6,#c9c9c9);border:1px solid #8a8a8a}.d{background:linear-gradient(#4a4a4a,#2e2e2e);border:1px solid #111}
img.px{image-rendering:pixelated}.t{font-size:10.5px;word-break:break-word;line-height:1.25}.t b{font-size:11px}.t i{color:var(--mut);font-style:normal}
"""


def sheet(title, subtitle, groups, out_png, cols=4, big=72, width=1400, mini=False):
    """groups: [(heading, [(png24, label_html)])]. Writes an HTML next to out_png and screenshots it."""
    base = os.path.dirname(os.path.abspath(out_png))
    parts = [f"<h1>{html.escape(title)}</h1><div class='bar'></div><p class='sub'>{subtitle}</p>"]
    n = 0
    for head, items in groups:
        parts.append(f"<h2>{html.escape(head)} <span style='font-weight:400;color:#424242'>({len(items)})</span></h2><div class='grid'>")
        for png, label in items:
            rel = os.path.relpath(png, base).replace("\\", "/")
            if mini:
                parts.append(f"<div class='c' title='{html.escape(label)}'><div class='n'><span class='gh l'><img src='{rel}' width=24 height=24></span>"
                             f"<span class='gh d'><img src='{rel}' width=24 height=24></span></div><img class='px' src='{rel}' width={big} height={big}>"
                             f"<div class='t'>{label}</div></div>")
            else:
                parts.append(f"<div class='c'><div class='n'><span class='gh l'><img src='{rel}' width=24 height=24></span>"
                             f"<span class='gh d'><img src='{rel}' width=24 height=24></span></div>"
                             f"<img class='px' src='{rel}' width={big} height={big}><div class='t'>{label}</div></div>")
            n += 1
        parts.append("</div>")
    doc = f"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{html.escape(title)}</title><style>{CSS % (width - 48, cols, big + 12)}</style></head><body>{''.join(parts)}</body></html>"
    html_path = os.path.splitext(out_png)[0] + ".html"
    open(html_path, "w", encoding="utf-8").write(doc)
    rows = sum((len(items) + cols - 1) // cols for _, items in groups)
    est = 90 + len(groups) * 40 + rows * (big + 24)
    _shot(os.path.abspath(html_path), os.path.abspath(out_png), width, est, scale=1)
    _trim(out_png)
    return html_path


def _trim(png):
    im = Image.open(png).convert("RGBA")
    bbox = im.getchannel("A").getbbox()
    if bbox:
        im = im.crop((0, 0, im.width, min(im.height, bbox[3] + 16)))
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    bg.convert("RGB").save(png, optimize=True)
