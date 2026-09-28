"""SAM Grasshopper icon grammar: object glyphs + operation badges -> 24x24 SVG.

Deterministic: the same (object, op, modifiers) always yields byte-identical SVG.
See ../ICON_DESIGN_SYSTEM.md for the rules this file implements.
"""
import math

# ---------------------------------------------------------------- palette
# Taken from https://sambim.xyz (CSS custom properties + logo pixels, 2026-09-28).
INK = "#111111"        # --text / --line
EMBLEM = "#81BC44"     # SAM_BIM_logo.png tile green (averaged)
DEEP = "#277A45"       # --green
BLUE = "#1565C0"       # --blue
YELLOW = "#F6C90E"     # --yellow
RED = "#D32F2F"        # --red
MUTED = "#424242"      # --muted
WHITE = "#FFFFFF"      # --bg

# Derived tones (flat, no gradients). Green = subject, neutral = context.
G_TOP, G_LEFT, G_RIGHT = "#ADD485", EMBLEM, "#659335"
N_TOP, N_LEFT, N_RIGHT = WHITE, "#E3E3E3", "#BDBDBD"   # #BDBDBD = site border grey
N_FLAT = "#EDEDED"
SW = 1.1  # the one outline weight (px at 24x24)


def tones(subject):
    return (G_TOP, G_LEFT, G_RIGHT) if subject else (N_TOP, N_LEFT, N_RIGHT)


def f(v):
    s = ("%.2f" % v).rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def pts(p):
    return " ".join(f(x) + "," + f(y) for x, y in p)


def poly(p, fill, stroke=INK, sw=SW, extra=""):
    return f'<polygon points="{pts(p)}" fill="{fill}" stroke="{stroke}" stroke-width="{f(sw)}" stroke-linejoin="round"{extra}/>'


def path(d, fill="none", stroke=INK, sw=SW, extra=""):
    return f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{f(sw)}" stroke-linecap="round" stroke-linejoin="round"{extra}/>'


def circle(cx, cy, r, fill, stroke=INK, sw=SW):
    s = f' stroke="{stroke}" stroke-width="{f(sw)}"' if stroke else ""
    return f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="{fill}"{s}/>'


def rect(x, y, w, h, fill, rx=0, stroke=INK, sw=SW, extra=""):
    r = f' rx="{f(rx)}"' if rx else ""
    s = f' stroke="{stroke}" stroke-width="{f(sw)}"' if stroke else ""
    return f'<rect x="{f(x)}" y="{f(y)}" width="{f(w)}" height="{f(h)}"{r} fill="{fill}"{s}{extra}/>'


# ---------------------------------------------------------------- isometric helpers (2:1 pixel isometric)
def iso(ox, oy):
    return lambda x, y, z: (ox + 2 * x - 2 * y, oy + x + y - z)


def box(P, x0, y0, z0, dx, dy, dz, t, dashed=False):
    x1, y1, z1 = x0 + dx, y0 + dy, z0 + dz
    ex = ' stroke-dasharray="1.6 1.1"' if dashed else ""
    return "".join([
        poly([P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)], t[0], extra=ex),
        poly([P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0), P(x0, y1, z0)], t[1], extra=ex),
        poly([P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0), P(x1, y0, z0)], t[2], extra=ex),
    ])


def prism(P, plan, z0, z1, t):
    """Extruded convex plan polygon (counter-clockwise in plan). Visible sides + top."""
    out = []
    n = len(plan)
    sides = []
    for i in range(n):
        a, b = plan[i], plan[(i + 1) % n]
        ex, ey = b[0] - a[0], b[1] - a[1]
        nx, ny = ey, -ex  # outward for CCW
        if nx + ny > 1e-6:
            depth = (a[0] + b[0] + a[1] + b[1])
            sides.append((depth, a, b, t[1] if ny > nx else t[2]))
    for _, a, b, tone in sorted(sides, key=lambda s: s[0]):
        out.append(poly([P(a[0], a[1], z1), P(b[0], b[1], z1), P(b[0], b[1], z0), P(a[0], a[1], z0)], tone))
    out.append(poly([P(x, y, z1) for x, y in plan], t[0]))
    return "".join(out)


def hexplan(r, rot=0.0):
    return [(r * math.cos(math.radians(rot + 60 * i)), r * math.sin(math.radians(rot + 60 * i))) for i in range(6)]


# ---------------------------------------------------------------- object glyphs
# Each returns SVG elements in the 24x24 frame. The bottom-right (â‰ˆ x,y > 14) is kept
# light because the operation badge lives there. subject=True -> SAM green.
G = {}


def glyph(name):
    def deco(fn):
        G[name] = fn
        return fn
    return deco


@glyph("space")
def _space(s=True):
    return box(iso(10, 10), 0, 0, 0, 4, 4, 8, tones(s))


@glyph("floor")
def _floor(s=True):
    # approved exploratory icon: cutaway room corner, floor = subject
    return (poly([(9, 8), (9, 2), (1, 6), (1, 12)], "#EFEFEF") + poly([(9, 8), (17, 12), (17, 6), (9, 2)], N_RIGHT)
            + poly([(9, 8), (17, 12), (9, 16), (1, 12)], G_LEFT if s else N_TOP))


@glyph("externalSpace")
def _external_space(s=True):
    P = iso(10, 10)
    return box(P, 0, 0, 0, 4, 4, 8, (N_TOP, N_LEFT, N_RIGHT), dashed=True)


@glyph("cluster")
def _cluster(s=True):
    P = iso(7, 9)
    t = (N_TOP, N_LEFT, N_RIGHT)
    return box(P, 0, 0, 0, 6, 3, 7, t) + path(
        "M13 5L7 8V15", stroke=DEEP if s else MUTED, sw=2.4)


@glyph("model")
def _model(s=True):
    P = iso(9, 9)
    t = tones(s)
    out = [box(P, 0, 0, 0, 4.5, 3.5, 5.5, t)]
    # gable roof, ridge along x at y=1.75
    out.append(poly([P(0, 3.5, 5.5), P(4.5, 3.5, 5.5), P(4.5, 1.75, 8.5), P(0, 1.75, 8.5)], t[0]))
    out.append(poly([P(4.5, 0, 5.5), P(4.5, 3.5, 5.5), P(4.5, 1.75, 8.5)], t[2]))
    return "".join(out)


@glyph("case")
def _case(s=True):
    return compose_plural(G["model"], s)


@glyph("panel")
def _panel(s=True):
    return poly([(2, 4), (14, 10), (14, 18), (2, 12)], G_LEFT if s else N_LEFT)


@glyph("aperture")
def _aperture(s=True):
    return poly([(2, 4), (14, 10), (14, 18), (2, 12)], N_LEFT) + poly(
        [(5, 7), (11, 10), (11, 14), (5, 11)], G_LEFT if s else N_TOP)


@glyph("openingProperties")
def _opening(s=True):
    return (poly([(2, 4), (14, 10), (14, 18), (2, 12)], N_LEFT)
            + poly([(5, 7), (11, 10), (11, 14), (5, 11)], MUTED)
            + poly([(5, 7), (11, 10), (8, 15.5), (2, 12.5)], G_TOP if s else N_TOP))


@glyph("shade")
def _shade(s=True):
    return (poly([(7, 6), (17, 11), (17, 17), (7, 12)], N_LEFT)
            + poly([(7, 6), (17, 11), (11, 14), (1, 9)], G_TOP if s else N_TOP))


@glyph("zone")
def _zone(s=True):
    P = iso(10, 7)
    out = [poly([P(0, 0, 0), P(4.5, 0, 0), P(4.5, 4.5, 0), P(0, 4.5, 0)], G_LEFT if s else N_FLAT, sw=1.3)]
    n = (N_TOP, N_LEFT, N_RIGHT)
    out.append(box(P, 0.5, 0.5, 0, 1.6, 1.6, 5, n))
    out.append(box(P, 2.4, 2.4, 0, 1.6, 1.6, 5, n))
    return "".join(out)


@glyph("level")
def _level(s=True):
    return (path("M2 13H20", sw=1.6) + path("M2 6.5H20", sw=1, extra=' stroke-dasharray="2 1.5"')
            + poly([(3, 8), (9, 8), (6, 13)], G_LEFT if s else N_TOP))


@glyph("shell")
def _shell(s=True):
    return prism(iso(10, 13.5), hexplan(3.2, 15), 0, 8, tones(s))


@glyph("face")
def _face(s=True):
    P = iso(10, 7)
    plan = [(0, 0), (5, 0), (5, 3), (3, 5), (0, 5)]
    return prism(P, plan, 0, 1, tones(s))


@glyph("geometry")
def _geometry(s=True):
    h = [(10 + 7 * math.cos(math.radians(60 * i)), 10 + 7 * math.sin(math.radians(60 * i))) for i in range(6)]
    out = [poly(h, G_TOP if s else N_FLAT)]
    out += [circle(x, y, 1.5, G_RIGHT if s else MUTED, stroke=None) for x, y in h[3:6]]
    return "".join(out)


@glyph("geometry2D")
def _geometry2d(s=True):
    p = [(3, 4), (16, 4), (16, 10), (10, 10), (10, 17), (3, 17)]
    return poly(p, G_TOP if s else N_FLAT) + "".join(circle(x, y, 1.4, G_RIGHT if s else MUTED, stroke=None) for x, y in p[:5:2])


@glyph("plane")
def _plane(s=True):
    P = iso(10, 9)
    return (poly([P(0, 0, 0), P(4, 0, 0), P(4, 4, 0), P(0, 4, 0)], G_TOP if s else N_TOP)
            + path("M10 13V3M8 5L10 3L12 5", sw=1.5))


@glyph("transform3D")
def _transform(s=True):
    return (path("M9 12V2.5M7.3 4.2L9 2.5L10.7 4.2", sw=1.5) + path("M9 12L17 16", sw=1.5)
            + path("M9 12L1.5 15.8", sw=1.5) + circle(9, 12, 2.2, G_LEFT if s else N_TOP))


@glyph("mesh")
def _mesh(s=True):
    P = iso(10, 7)
    a, b, c, d, m = P(0, 0, 0), P(5, 0, 0), P(5, 5, 0), P(0, 5, 0), P(2.5, 2.5, 0)
    t1, t2 = (G_TOP, G_LEFT) if s else (N_TOP, N_LEFT)
    return poly([a, b, m], t1) + poly([b, c, m], t2) + poly([c, d, m], t1) + poly([d, a, m], t2)


@glyph("polyloop")
def _polyloop(s=True):
    p = [(3, 5), (12, 2.5), (17, 9), (11, 16), (4, 13)]
    return poly(p, G_TOP if s else "none", stroke=DEEP if s else INK, sw=1.8) + "".join(
        circle(x, y, 1.3, INK, stroke=None) for x, y in p)


@glyph("segment")
def _segment(s=True):
    return path("M3 15L16 4", stroke=DEEP if s else INK, sw=2) + circle(3, 15, 2, WHITE) + circle(16, 4, 2, WHITE)


@glyph("points")
def _points(s=True):
    fill = G_LEFT if s else N_TOP
    return circle(5, 13, 2.4, fill) + circle(11, 4.5, 2.4, fill) + circle(15, 11, 2.4, fill)


@glyph("vector")
def _vector(s=True):
    return path("M3 16L15 4M8.5 4H15V10.5", stroke=DEEP if s else INK, sw=2.2)


@glyph("sectionBox")
def _section_box(s=True):
    P = iso(12, 12)
    n = (N_TOP, N_LEFT, N_RIGHT)
    out = [poly([P(0, 0, 3), P(4, 0, 3), P(4, 4, 3), P(0, 4, 3)], G_LEFT if s else N_TOP)]
    out.append(poly([P(0, 4, 3), P(4, 4, 3), P(4, 4, 0), P(0, 4, 0)], n[1]))
    out.append(poly([P(4, 0, 3), P(4, 4, 3), P(4, 4, 0), P(4, 0, 0)], n[2]))
    out.append(box(P, 0, 0, 7, 4, 4, 3, n))
    return "".join(out)


def _slab(P, x0, dx, t, dy=3.5, dz=9):
    return box(P, x0, 0, 0, dx, dy, dz, t)


@glyph("construction")
def _construction(s=True):
    # one wall block, three adjoining layers: neutral | green core | neutral (ink only at seams)
    P = iso(8, 11)
    n = (N_TOP, N_LEFT, N_RIGHT)
    return (box(P, 0, 0, 0, 1.3, 3.5, 9, n) + box(P, 1.3, 0, 0, 2.0, 3.5, 9, tones(s))
            + box(P, 3.3, 0, 0, 1.3, 3.5, 9, n))


@glyph("constructionLayer")
def _construction_layer(s=True):
    return _slab(iso(9, 11), 1.6, 1.1, tones(s))


@glyph("materialLayer")
def _material_layer(s=True):
    P = iso(9, 11)
    h = "".join(f"M{f(P(2.7, y, 8.2)[0])} {f(P(2.7, y, 8.2)[1])}L{f(P(2.7, y - 1.4, 0.8)[0])} {f(P(2.7, y - 1.4, 0.8)[1])}" for y in (2, 3.3))
    return _slab(P, 1.6, 1.1, tones(s)) + path(h, sw=0.9)


@glyph("apertureConstruction")
def _aperture_construction(s=True):
    # double-glazing unit: rear pane + front pane with SAM-green glass
    rear = [(6, 2), (16, 7), (16, 15), (6, 10)]
    front = [(2, 5), (12, 10), (12, 18), (2, 13)]
    return (poly(rear, N_TOP) + poly(front, N_LEFT)
            + poly([(4, 7.6), (10, 10.6), (10, 15.4), (4, 12.4)], G_LEFT if s else N_TOP))


@glyph("material")
def _material(s=True):
    # material sample sphere: body + shaded crescent + highlight
    return (circle(10, 10, 7.5, G_LEFT if s else N_TOP)
            + path("M4.2 13.6A7.5 7.5 0 0 0 17.5 10A7.5 6 0 0 1 4.2 13.6Z", fill=G_RIGHT if s else N_RIGHT, stroke="none")
            + circle(10, 10, 7.5, "none") + circle(7.2, 6.8, 1.6, WHITE, stroke=None))


@glyph("transparentMaterial")
def _transparent_material(s=True):
    return (circle(10, 10, 7.5, "#F3F8EC" if s else WHITE) + path("M6.5 6.8A4.8 4.8 0 0 1 11 4.8", stroke=DEEP, sw=1.4)
            + path("M5.5 14.5L14.5 5.5", stroke=G_LEFT if s else N_RIGHT, sw=1.2, extra=' stroke-dasharray="1.6 1.2"'))


@glyph("gasMaterial")
def _gas_material(s=True):
    out = [circle(10, 10, 7.5, WHITE).replace("/>", ' stroke-dasharray="2 1.4"/>')]
    out += [circle(x, y, 1.3, G_RIGHT if s else MUTED, stroke=None) for x, y in ((7, 7), (12.5, 6.5), (6.5, 12.5), (11, 11), (14, 13))]
    return "".join(out)


@glyph("profile")
def _profile(s=True):
    return (path("M3 13.5H6.5V6H11V10H16.5V16H3Z", fill=G_TOP if s else N_FLAT, stroke="none")
            + path("M3 2.5V16H17", sw=1.3) + path("M3 13.5H6.5V6H11V10H16.5", stroke=DEEP if s else INK, sw=1.8))


@glyph("internalCondition")
def _person(s=True):
    fill = G_LEFT if s else N_TOP
    return circle(10, 5.5, 3.2, fill) + path("M3.5 17.5a6.5 6.5 0 0 1 13 0z", fill=fill)


@glyph("degreeOfActivity")
def _activity(s=True):
    return _person(s) + path("M1 8.5h2.2M0.8 11.5h2", sw=1.3) + path("M17 8.5h2.2M17.2 11.5h2", sw=1.3)


@glyph("lighting")
def _bulb(s=True):
    return (path("M10 2.5a5.5 5.5 0 0 1 3.2 10v2.3h-6.4v-2.3A5.5 5.5 0 0 1 10 2.5z", fill=G_TOP if s else N_TOP)
            + rect(7, 15.8, 6, 2.2, MUTED, rx=0.6, stroke=None))


@glyph("equipment")
def _equipment(s=True):
    return rect(2.5, 3, 15, 10, G_TOP if s else N_TOP, rx=1) + path("M10 13V16.5M6 17H14", sw=1.5)


@glyph("infiltration")
def _wind(s=True):
    c = DEEP if s else INK
    return (path("M2 7H12a2.4 2.4 0 1 0-2.4-2.4", stroke=c, sw=1.8) + path("M2 11H16", stroke=c, sw=1.8)
            + path("M2 15H11a2.4 2.4 0 1 1-2.4 2.4", stroke=c, sw=1.8))


@glyph("pollutant")
def _pollutant(s=True):
    return (path("M5 15a3.5 3.5 0 0 1 0-7a4.5 4.5 0 0 1 8.6-1.2A3.8 3.8 0 0 1 15 15z", fill=G_TOP if s else N_TOP)
            + circle(7.5, 11.5, 1.1, INK, stroke=None) + circle(11, 10, 1.1, INK, stroke=None) + circle(12.5, 13, 1.1, INK, stroke=None))


@glyph("thermometer")
def _thermo(s=True):
    return (path("M8 11.5V4a2 2 0 0 1 4 0v7.5a3.8 3.8 0 1 1-4 0z", fill=WHITE)
            + path("M10 7.5V13", stroke=G_RIGHT if s else MUTED, sw=2.2) + circle(10, 14.5, 2.1, G_LEFT if s else MUTED, stroke=None)
            + path("M13.5 5h2M13.5 8h2", sw=1))


@glyph("fan")
def _fan(s=True):
    out = [circle(10, 10, 7.5, G_TOP if s else N_TOP)]
    for a in (0, 120, 240):
        r = math.radians(a)
        c, sn = math.cos(r), math.sin(r)
        tip = (10 + 5.8 * c, 10 + 5.8 * sn)
        side = (10 + 4.2 * math.cos(r + 0.9), 10 + 4.2 * math.sin(r + 0.9))
        out.append(poly([(10, 10), tip, side], G_RIGHT if s else MUTED, sw=0.8))
    out.append(circle(10, 10, 1.4, WHITE))
    return "".join(out)


@glyph("airflow")
def _airflow(s=True):
    c = DEEP if s else INK
    return "".join(path(f"M2 {y}H14M11 {y - 2.5}L14 {y}L11 {y + 2.5}", stroke=c, sw=1.8) for y in (5, 10, 15))


@glyph("izam")
def _izam(s=True):
    P = iso(9, 12)
    n = (N_TOP, N_LEFT, N_RIGHT)
    return (box(P, 0, 1, 0, 2.2, 2.2, 5, n) + box(P, 3.4, -1.2, 0, 2.2, 2.2, 5, n)
            + path("M4 5.2Q9 0.6 14.5 3.2M11.8 1.6L14.5 3.2L12.3 5.2", stroke=DEEP if s else INK, sw=1.7))


@glyph("ahu")
def _ahu(s=True):
    P = iso(9, 11)
    cx, cy = P(2.5, 2.5, 3)
    return box(P, 0, 0, 0, 5, 2.5, 6, tones(s)) + circle(cx, cy, 2.4, WHITE) + path(
        f"M{f(cx - 1.3)} {f(cy - 1.3)}L{f(cx + 1.3)} {f(cy + 1.3)}M{f(cx + 1.3)} {f(cy - 1.3)}L{f(cx - 1.3)} {f(cy + 1.3)}", sw=0.9)


@glyph("system")
def _system(s=True):
    return (rect(2.5, 6, 15, 8, G_TOP if s else N_TOP, rx=1) + path("M2.5 10H0.8M17.5 10H19.2", sw=2)
            + circle(10, 10, 2.8, WHITE) + path("M8.2 8.2L11.8 11.8M11.8 8.2L8.2 11.8", sw=1))


@glyph("location")
def _pin(s=True):
    return (path("M3 17.5H17", sw=1.3) + path("M10 17C6 12.5 4.2 9.8 4.2 7.2a5.8 5.8 0 0 1 11.6 0C15.8 9.8 14 12.5 10 17z", fill=G_LEFT if s else N_TOP)
            + circle(10, 7.2, 2, WHITE))


@glyph("address")
def _address(s=True):
    return rect(1.5, 9.5, 12, 8, WHITE, rx=1) + path("M4 12.5h7M4 15h4.5", sw=1.1) + path(
        "M13 12C10.5 9.2 9.5 7.6 9.5 6a3.5 3.5 0 0 1 7 0C16.5 7.6 15.5 9.2 13 12z", fill=G_LEFT if s else N_TOP) + circle(13, 6, 1.1, WHITE, stroke=None)


def _sun(cx, cy, r, rays=True):
    out = ""
    if rays:
        d = "".join(f"M{f(cx + (r + 1.3) * math.cos(math.radians(a)))} {f(cy + (r + 1.3) * math.sin(math.radians(a)))}"
                    f"L{f(cx + (r + 3) * math.cos(math.radians(a)))} {f(cy + (r + 3) * math.sin(math.radians(a)))}" for a in range(0, 360, 45))
        out += path(d, sw=1.2)
    return out + circle(cx, cy, r, YELLOW)


@glyph("weather")
def _weather(s=True):
    return _sun(7.5, 7, 3.2) + path("M7 17.5a3.4 3.4 0 0 1 0-6.8a4.4 4.4 0 0 1 8.4-0.8A3.6 3.6 0 0 1 16 17.5z", fill=G_TOP if s else N_TOP)


@glyph("weatherData")
def _weather_data(s=True):
    return _sun(6.5, 6, 2.6) + path("M2 17.5H18", sw=1.3) + "".join(
        rect(x, y, 2.4, 17.5 - y, G_LEFT if s else N_TOP, stroke=INK, sw=0.9) for x, y in ((9, 11), (12.2, 8), (15.4, 12)))


@glyph("weatherYear")
def _weather_year(s=True):
    return (rect(2.5, 4, 15, 13.5, WHITE, rx=1) + rect(2.5, 4, 15, 3.5, G_LEFT if s else N_LEFT, rx=1)
            + "".join(circle(x, y, 0.9, INK, stroke=None) for x in (6, 10, 14) for y in (10.5, 14)) + _sun(15.5, 3.5, 2.2, rays=False))


@glyph("designDay")
def _design_day(s=True):
    return (path("M2 16H18", sw=1.3) + path("M3.5 16a6.5 6.5 0 0 1 13 0", stroke=DEEP if s else INK, sw=1.4, extra=' stroke-dasharray="1.8 1.3"')
            + _sun(10, 9.5, 2.6, rays=True))


@glyph("clock")
def _clock(s=True):
    return circle(10, 10, 7.5, G_TOP if s else N_TOP) + path("M10 5.5V10L13.5 12", sw=1.7)


@glyph("calendar")
def _calendar(s=True):
    return (rect(2.5, 4, 15, 13.5, WHITE, rx=1) + rect(2.5, 4, 15, 3.5, G_LEFT if s else N_LEFT, rx=1)
            + "".join(circle(x, y, 0.9, INK, stroke=None) for x in (6, 10, 14) for y in (10.5, 14)) + path("M6.5 2.5v3M13.5 2.5v3", sw=1.4))


@glyph("result")
def _result(s=True):
    fill = G_LEFT if s else N_TOP
    return path("M2 17.5H18", sw=1.3) + rect(3, 11, 3.4, 6.5, fill) + rect(8, 6, 3.4, 11.5, fill) + rect(13, 2.5, 3.4, 15, fill)


@glyph("table")
def _table(s=True):
    return (rect(2, 3, 16, 14, WHITE, rx=1) + rect(2, 3, 16, 4, G_LEFT if s else N_LEFT, rx=1)
            + path("M2 11.5H18M7.5 3V17M13 3V17", sw=0.9))


@glyph("textMap")
def _text_map(s=True):
    return (rect(1.5, 3, 6, 14, WHITE, rx=1) + rect(12.5, 3, 6, 14, G_TOP if s else N_TOP, rx=1)
            + path("M3.5 7h2M3.5 10h2M3.5 13h2M14.5 7h2M14.5 10h2M14.5 13h2", sw=1.1) + path("M8.5 10H11.5M10.3 8.8L11.5 10L10.3 11.2", sw=1.1))


@glyph("value")
def _tag(s=True):
    return poly([(2, 10), (7, 4.5), (17.5, 4.5), (17.5, 15.5), (7, 15.5)], G_LEFT if s else N_TOP) + circle(7, 10, 1.4, WHITE)


@glyph("name")
def _name(s=True):
    return rect(1.5, 5, 16, 10, WHITE, rx=2) + path("M4.5 8.5h10M4.5 11.5h6", stroke=DEEP if s else INK, sw=1.8)


@glyph("json")
def _json(s=True):
    c = DEEP if s else INK
    return (path("M7 3C4.5 3 5.5 9 3 10C5.5 11 4.5 17 7 17", stroke=c, sw=1.8) + path("M13 3C15.5 3 14.5 9 17 10C14.5 11 15.5 17 13 17", stroke=c, sw=1.8)
            + circle(10, 10, 1.4, G_LEFT if s else INK, stroke=None))


@glyph("file")
def _file(s=True):
    return (path("M3.5 2H11.5L16 6.5V18H3.5Z", fill=WHITE) + path("M11.5 2V6.5H16", fill=G_LEFT if s else N_LEFT)
            + path("M6 10h7.5M6 13h7.5M6 16h4", stroke=DEEP if s else INK, sw=1.1))


@glyph("log")
def _log(s=True):
    return (path("M3.5 2H16V18H3.5Z", fill=WHITE) + path("M6 5.5h7.5M6 8.5h7.5M6 11.5h7.5M6 14.5h4", sw=1.1)
            + rect(3.5, 2, 2, 16, G_LEFT if s else N_LEFT))


@glyph("colour")
def _colour(s=True):
    return rect(2, 2, 6.5, 6.5, RED) + rect(6.5, 6.5, 6.5, 6.5, YELLOW) + rect(11, 11, 6.5, 6.5, BLUE)


@glyph("number")
def _number(s=True):
    return (rect(1.5, 7, 17, 7, G_TOP if s else N_TOP, rx=1)
            + path("M4.5 7v3M7.5 7v2M10.5 7v3M13.5 7v2M16.5 7v3", sw=1))


@glyph("text")
def _text(s=True):
    fill = G_LEFT if s else INK
    return (path("M3 12.5a3 3 0 1 1 3 3c0 0 0 2.5-2.5 3.5c1-1.5 1-2 0.8-2.5A3 3 0 0 1 3 12.5z", fill=fill, sw=0.9)
            + path("M10 12.5a3 3 0 1 1 3 3c0 0 0 2.5-2.5 3.5c1-1.5 1-2 0.8-2.5A3 3 0 0 1 10 12.5z", fill=fill, sw=0.9)
            + path("M3 5.5h14", sw=1.3))


@glyph("list")
def _list(s=True):
    return "".join(circle(3.5, y, 1.5, G_LEFT if s else INK, stroke=None) + path(f"M7 {y}H17", sw=1.6) for y in (4.5, 10, 15.5))


@glyph("tree")
def _tree(s=True):
    fill = G_LEFT if s else N_TOP
    return (path("M4 10H8M8 4V16M8 4H12M8 10H12M8 16H12", sw=1.4) + circle(3.5, 10, 2, fill)
            + circle(14, 4, 2, fill) + circle(14, 10, 2, fill) + circle(14, 16, 2, fill))


@glyph("folder")
def _folder(s=True):
    return path("M2 5.5a1 1 0 0 1 1-1H8l1.8 2H17a1 1 0 0 1 1 1V16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z", fill=G_LEFT if s else N_TOP)


@glyph("object")
def _object(s=True):
    # echo of the SAM-BIM emblem: the green rounded tile (no lettering)
    return rect(2.5, 2.5, 14, 14, EMBLEM if s else N_TOP, rx=2.5) + path("M6 8h7M6 11h4.5", stroke=WHITE if s else INK, sw=1.6)


@glyph("relation")
def _relation(s=True):
    fill = G_LEFT if s else N_TOP
    return (path("M5 14L10 4.5L15.5 13Z", sw=1.3) + circle(10, 4.5, 2.6, fill)
            + circle(4.5, 14.5, 2.6, fill) + circle(15.5, 13, 2.6, fill))


@glyph("group")
def _group(s=True):
    return (rect(1.5, 2.5, 17, 15, "none", rx=2.5, extra=' stroke-dasharray="2 1.4"')
            + rect(4, 5, 5.5, 5.5, G_LEFT if s else N_TOP, rx=1) + rect(10.5, 9, 5.5, 5.5, G_LEFT if s else N_TOP, rx=1))


@glyph("indexed")
def _indexed(s=True):
    return "".join(rect(2 + 5.5 * i, 7, 4.6, 6, G_LEFT if s and i == 1 else N_TOP, rx=0.8) for i in range(3)) + path(
        "M4.3 4.5v1M9.8 4.5v1M15.3 4.5v1", sw=1.2)


@glyph("type")
def _type(s=True):
    return (circle(5.5, 6, 3.5, N_TOP) + rect(10, 2.5, 7, 7, G_LEFT if s else N_TOP, rx=0.5)
            + poly([(3, 17.5), (7.5, 10.5), (12, 17.5)], N_TOP))


@glyph("settings")
def _settings(s=True):
    return (path("M2 6H18M2 14H18", sw=1.4) + circle(12.5, 6, 2.6, G_LEFT if s else N_TOP)
            + circle(6.5, 14, 2.6, G_LEFT if s else N_TOP))


@glyph("compass")
def _compass(s=True):
    return circle(10, 10, 7.5, WHITE) + poly([(10, 3.5), (12.4, 10), (7.6, 10)], G_LEFT if s else INK, sw=0.9) + poly(
        [(10, 16.5), (12.4, 10), (7.6, 10)], N_LEFT, sw=0.9)


@glyph("heat")
def _heat(s=True):
    c = DEEP if s else INK
    return (rect(8, 2, 4, 16, N_LEFT) + path("M1.5 10H17.5M14.5 7L17.5 10L14.5 13", stroke=c, sw=1.8)
            + path("M3 4.5q1.2 1.2 0 2.4t0 2.4", stroke=c, sw=1.1))


@glyph("daylight")
def _daylight(s=True):
    P = iso(11, 8)
    return (poly([P(0, 0, 0), P(4, 0, 0), P(4, 4, 0), P(0, 4, 0)], G_LEFT if s else N_TOP)
            + path("M5 5.5L9 11.5M8 3.5L13 11", stroke=INK, sw=1.1, extra=' stroke-dasharray="1.5 1"') + _sun(3.5, 3.5, 2.3, rays=False))


@glyph("grid")
def _grid(s=True):
    P = iso(10, 7)
    out = [poly([P(0, 0, 0), P(5, 0, 0), P(5, 5, 0), P(0, 5, 0)], G_TOP if s else N_TOP)]
    out += [circle(*P(i, j, 0), 0.9, INK, stroke=None) for i in (1, 2.5, 4) for j in (1, 2.5, 4)]
    return "".join(out)


@glyph("normal")
def _normal(s=True):
    return poly([(6, 4), (16, 9), (16, 17), (6, 12)], N_LEFT) + path("M11 10.5L2.5 14.8M3.4 12.2L2.5 14.8L5.2 15.4", stroke=DEEP if s else INK, sw=1.8)


@glyph("matrix")
def _matrix(s=True):
    return (path("M4.5 3H2.5V17H4.5M15.5 3H17.5V17H15.5", sw=1.4)
            + "".join(circle(x, y, 1.3, G_RIGHT if s and x == y else INK, stroke=None) for x in (6, 10, 14) for y in (6, 10, 14)))


@glyph("info")
def _info(s=True):
    return rect(2.5, 2.5, 15, 15, EMBLEM, rx=2.5) + circle(10, 6.3, 1.4, WHITE, stroke=None) + path("M10 9.5V14.5", stroke=WHITE, sw=2.2)


@glyph("constructionManager")
def _manager(s=True):
    return scaled(G["apertureConstruction"](False), 0.72, 6.5, -2.5) + scaled(G["construction"](s), 0.78, -2, 3)


# ---------------------------------------------------------------- composition modifiers
def scaled(svg, k, tx, ty):
    """Place a glyph scaled by k while keeping the outline weight constant (SW at 24 px)."""
    import re
    svg = re.sub(r'stroke-width="([\d.]+)"', lambda m: f'stroke-width="{f(float(m.group(1)) / k)}"', svg)
    return f'<g transform="translate({f(tx)} {f(ty)}) scale({f(k)})">{svg}</g>'


def compose_plural(fn, s):
    return scaled(fn(s), 0.78, 5, -1.5) + scaled(fn(s), 0.78, -0.5, 3.6)


def compose_add(fn, s, plural=False):
    """ADD: an existing (neutral) instance with the new one(s) in SAM green in front of it."""
    if plural:
        return scaled(fn(False), 0.66, 7, -2) + scaled(fn(s), 0.66, 3.2, 1.2) + scaled(fn(s), 0.66, -0.6, 4.6)
    return scaled(fn(False), 0.78, 5, -1.5) + scaled(fn(s), 0.78, -0.5, 3.6)


def compose_library(fn, s, add=False):
    cards = (rect(7.5, 1.5, 12, 12, WHITE, rx=1.2) + rect(5.5, 3.5, 12, 12, WHITE, rx=1.2)
             + path("M9.5 4.5h7M9.5 6.5h5", sw=1))
    if add:  # a new green item going into an existing (neutral) library
        return cards + scaled(fn(False), 0.74, -0.5, 4.2) + scaled(fn(s), 0.5, 10, -0.5)
    return cards + scaled(fn(s), 0.74, -0.5, 4.2)


def effect_split(fn, s):
    a = '<clipPath id="ca"><polygon points="-2,-2 13,-2 7,26 -2,26"/></clipPath>'
    b = '<clipPath id="cb"><polygon points="13,-2 26,-2 26,26 7,26"/></clipPath>'
    return (f'<defs>{a}{b}</defs><g transform="translate(-1.6 0.6)"><g clip-path="url(#ca)">{fn(s)}</g></g>'
            f'<g transform="translate(1.6 -0.6)"><g clip-path="url(#cb)">{fn(s)}</g></g>')


def effect_intersect(fn, s):
    return (f'<defs><clipPath id="ci"><g transform="translate(-0.5 3.6) scale(0.78)">{_silhouette(fn)}</g></clipPath></defs>'
            + scaled(fn(False), 0.78, 5, -1.5) + scaled(fn(False), 0.78, -0.5, 3.6)
            + f'<g clip-path="url(#ci)">{scaled(fn(True), 0.78, 5, -1.5)}</g>')


def effect_difference(fn, s):
    return scaled(fn(True), 0.78, -0.5, 3.6) + scaled(_cutter(fn), 0.78, 5, -1.5)


def _silhouette(fn):
    import re
    return re.sub(r' stroke="[^"]*"', "", fn(False))


def _cutter(fn):
    import re
    s = fn(False)
    s = re.sub(r'fill="(?!none)[^"]*"', 'fill="#FFFFFF"', s)
    return s.replace("<polygon ", '<polygon stroke-dasharray="1.6 1.1" ')


EFFECTS = {"split": effect_split, "intersect": effect_intersect, "difference": effect_difference}


# ---------------------------------------------------------------- operation badges
FAMILIES = {
    #  family      disc        glyph colour   ring
    "construct": (BLUE, WHITE, None),
    "query": (DEEP, WHITE, None),
    "change": (YELLOW, INK, None),
    "remove": (RED, WHITE, None),
    "evaluate": (INK, WHITE, None),
    "transfer": (WHITE, INK, INK),
    "value": (WHITE, INK, INK),
}

_A = 'stroke-linecap="round" stroke-linejoin="round"'


def _g(d, c, sw=1.5, fill="none"):
    return f'<path d="{d}" fill="{fill}" stroke="{c}" stroke-width="{f(sw)}" {_A}/>'


BADGES = {
    # op: (family, glyph(c))
    "create": ("construct", lambda c: _g("M19 16.6v4.8M16.6 19h4.8", c, 1.7)),
    "add": ("construct", lambda c: _g("M19 16.6v4.8M16.6 19h4.8", c, 1.7)),
    "copy": ("construct", lambda c: _g("M16.9 17.8h2.7v3.3h-2.7zM18.4 17.8v-1.2h2.7v3.3h-1.5", c, 1.1)),
    "get": ("query", lambda c: _g("M17 21L21 17M18.2 17H21V19.8", c)),
    "filter": ("query", lambda c: _g("M16.5 17H21.5L19.8 19.2V21.4L18.2 20.6V19.2Z", c, 1.1, fill=c)),
    "inspect": ("query", lambda c: _g("M18.4 18.4m-1.7 0a1.7 1.7 0 1 0 3.4 0a1.7 1.7 0 1 0-3.4 0M19.7 19.7L21.2 21.2", c, 1.3)),
    "list": ("query", lambda c: _g("M16.8 17.3h4.4M16.8 19h4.4M16.8 20.7h4.4", c, 1.1)),
    "display": ("query", lambda c: _g("M16.2 19Q19 15.6 21.8 19Q19 22.4 16.2 19Z", c, 1.1) + f'<circle cx="19" cy="19" r="0.9" fill="{c}"/>'),
    "set": ("change", lambda c: _g("M21.2 16.8L17.3 20.7M16.9 17.8V21.1H20.2", c, 1.7)),
    "modify": ("change", lambda c: _g("M19 16.2L21.6 21H16.4Z", c, 1.6)),
    "update": ("change", lambda c: _g("M21.3 18.6A2.4 2.4 0 1 1 20.1 16.9", c, 1.7) + '<polygon points="19.4,15.5 21.6,16.3 19.9,17.9" fill="' + c + '"/>'),
    "merge": ("change", lambda c: _g("M16.6 16.6L19 19.2L21.4 16.6M19 19.2V21.6", c, 1.8)),
    "split": ("change", lambda c: _g("M19 16.4V18.8L16.6 21.4M19 18.8L21.4 21.4", c, 1.8)),
    "section": ("change", lambda c: _g("M16.9 16.9h4.2v4.2h-4.2z", c, 1.2) + _g("M15.9 19h6.2", c, 1.7)),
    "union": ("change", lambda c: _g("M16.9 16.6v2.4a2.1 2.1 0 0 0 4.2 0v-2.4", c, 1.7)),
    "intersect": ("change", lambda c: _g("M16.9 21.4v-2.4a2.1 2.1 0 0 1 4.2 0v2.4", c, 1.7)),
    "difference": ("change", lambda c: _g("M16.6 16.6h4.8v2.4h-2.4v2.4h-2.4z", c, 1.1, fill=c)),
    "offset": ("change", lambda c: _g("M16.5 16.5h5v5h-5z", c, 1.3) + '<rect x="18" y="18" width="2" height="2" fill="' + c + '"/>'),
    "transform": ("change", lambda c: _g("M17.6 19h2.8", c, 1.7) + '<polygon points="18,17.1 16.1,19 18,20.9" fill="' + c + '"/><polygon points="20,17.1 21.9,19 20,20.9" fill="' + c + '"/>'),
    "snap": ("change", lambda c: _g("M21 16.4V21.6", c, 1.8) + f'<circle cx="17.4" cy="19" r="1.7" fill="{c}"/>'),
    "align": ("change", lambda c: _g("M16.8 16.4V21.6", c, 1.6) + _g("M18.5 17.6h3.1M18.5 20.4h1.8", c, 1.8)),
    "extend": ("change", lambda c: _g("M16.3 19h3", c, 1.8) + _g("M21.6 16.5V21.5", c, 1.6) + '<polygon points="19,17 20.9,19 19,21" fill="' + c + '"/>'),
    "flip": ("change", lambda c: _g("M17.6 21.4V17.6M20.4 16.6V20.4", c, 1.6) + '<polygon points="16.2,18 17.6,16.2 19,18" fill="' + c + '"/><polygon points="19,20 20.4,21.8 21.8,20" fill="' + c + '"/>'),
    "triangulate": ("change", lambda c: _g("M19 16.3L21.6 21.2H16.4ZM19 16.3V21.2", c, 1.4)),
    "sort": ("change", lambda c: _g("M16.6 17.1h4.8M16.6 19h3.2M16.6 20.9h1.6", c, 1.6)),
    "remove": ("remove", lambda c: _g("M16.6 19h4.8", c, 1.8)),
    "error": ("remove", lambda c: _g("M19 16.7v2.7", c, 1.6) + f'<circle cx="19" cy="21" r="0.9" fill="{c}"/>'),
    "calculate": ("evaluate", lambda c: _g("M16.8 17.9h4.4M16.8 20.1h4.4", c, 1.4)),
    "sum": ("evaluate", lambda c: _g("M19 16.8v4.4M16.8 19h4.4", c, 1.4)),
    "multiply": ("evaluate", lambda c: _g("M17.3 17.3l3.4 3.4M20.7 17.3l-3.4 3.4", c, 1.4)),
    "divide": ("evaluate", lambda c: _g("M16.8 19h4.4", c, 1.3) + f'<circle cx="19" cy="17.2" r="0.8" fill="{c}"/><circle cx="19" cy="20.8" r="0.8" fill="{c}"/>'),
    "analyse": ("evaluate", lambda c: _g("M17.2 21.2v-1.8M19 21.2v-4.2M20.8 21.2v-2.8", c, 1.3)),
    "validate": ("evaluate", lambda c: _g("M16.9 19.1L18.4 20.6L21.2 17.4", c, 1.5)),
    "convert": ("transfer", lambda c: _g("M16.7 17.6H19.8M21.3 20.4H18.2", c, 1.5) + '<polygon points="19.6,16.1 21.7,17.6 19.6,19.1" fill="' + c + '"/><polygon points="18.4,18.9 16.3,20.4 18.4,21.9" fill="' + c + '"/>'),
    "import": ("transfer", lambda c: _g("M19 16.1V19.4", c, 2.0) + '<polygon points="16.5,18.8 21.5,18.8 19,21.9" fill="' + c + '"/>'),
    "export": ("transfer", lambda c: _g("M19 21.9V18.6", c, 2.0) + '<polygon points="16.5,19.2 21.5,19.2 19,16.1" fill="' + c + '"/>'),
    "value": ("value", lambda c: _g("M16.9 17.3h4.2M16.9 19h4.2M16.9 20.7h4.2", c, 1.3)),
}


def badge(op):
    fam, g = BADGES[op]
    disc, gc, ring = FAMILIES[fam]
    r = f' stroke="{ring}" stroke-width="0.9"' if ring else ""
    return (f'<circle cx="19" cy="19" r="5" fill="{WHITE}"/>'
            f'<circle cx="19" cy="19" r="{"3.9" if ring else "4.2"}" fill="{disc}"{r}/>' + g(gc))


# ---------------------------------------------------------------- icon
def icon_svg(obj, op=None, plural=False, container=None, subject=True, comment=""):
    fn = G[obj]
    if container == "library":
        base = lambda s: compose_library(fn, s, add=op == "add")  # noqa: E731
    elif op == "add":
        base = lambda s: compose_add(fn, s, plural)  # noqa: E731
    elif plural:
        base = lambda s: compose_plural(fn, s)  # noqa: E731
    else:
        base = fn
    body = EFFECTS[op](base, subject) if op in EFFECTS else base(subject)
    b = badge(op) if op else ""
    note = f"<!-- {comment} -->" if comment else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">{note}'
            f'<g vector-effect="non-scaling-stroke">{body}</g>{b}</svg>\n')
