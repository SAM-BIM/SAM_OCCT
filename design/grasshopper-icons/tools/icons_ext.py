"""SAM-BIM extension glyphs for the Grasshopper icon grammar (shared by every SAM-BIM plugin repo).

`icons.py` is the frozen SAM design system (SAM-BIM/SAM#166, vendored verbatim). This module only
*adds* object glyphs for domains that SAM itself has no noun for (psychrometrics, topology,
optimisation, scripting, acoustics, Revit documents, HVAC plant, solar). It adds one verb, `run`
(Evaluate family), and does not change any colour, family, badge, modifier or existing glyph.
The file is byte-identical in every SAM-BIM repository that uses it; see ICON_DESIGN_SYSTEM_EXT.md.
"""
import math

import icons
from icons import (BLUE, DEEP, EMBLEM, G_LEFT, G_RIGHT, G_TOP, INK, MUTED, N_FLAT, N_LEFT, N_RIGHT, N_TOP, WHITE,  # noqa: F401
                   YELLOW, _g, _sun, box, circle, f, glyph, iso, path, poly, rect, tones)

EXT_VERSION = "1"


# ---------------------------------------------------------------- psychrometrics (Mollier)
def _chart(s, strong=True):
    """Psychrometric chart frame: axes + saturation curve. Green area when the chart is the subject."""
    out = ""
    if strong:
        out += path("M3.8 15.2C8.6 14.6 12.4 10.8 15.2 3.2V15.2Z", fill=G_TOP if s else N_FLAT, stroke="none")
    out += path("M3 2.5V16H17", sw=1.3)
    out += path("M3.8 15.2C8.6 14.6 12.4 10.8 15.2 3.2", stroke=DEEP if (s and strong) else (MUTED if strong else N_RIGHT), sw=1.6 if strong else 1.1)
    return out


def _arrow(x0, y0, x1, y1, c=DEEP, sw=2.2, head=2.8):
    """Bold line with a filled arrowhead at (x1, y1)."""
    a = math.atan2(y1 - y0, x1 - x0)
    bx, by = x1 - head * math.cos(a), y1 - head * math.sin(a)
    l = (bx + head * 0.62 * math.sin(a), by - head * 0.62 * math.cos(a))
    r = (bx - head * 0.62 * math.sin(a), by + head * 0.62 * math.cos(a))
    return (path(f"M{f(x0)} {f(y0)}L{f(bx)} {f(by)}", stroke=c, sw=sw)
            + f'<polygon points="{f(x1)},{f(y1)} {f(l[0])},{f(l[1])} {f(r[0])},{f(r[1])}" fill="{c}" stroke="{c}" stroke-width="0.6" stroke-linejoin="round"/>')


@glyph("mollierChart")
def _mollier_chart(s=True):
    return _chart(s)


@glyph("mollierPoint")
def _mollier_point(s=True):
    return _chart(False, strong=False) + circle(9, 9.5, 2.6, G_LEFT if s else N_TOP, sw=1.2)


def _process(s, body):
    # process arrows use the mid subject green (#659335): it reads on the light, orange and dark GH bodies
    return _chart(False, strong=False) + body(G_RIGHT if s else INK)


@glyph("mollierProcess")
def _mollier_process(s=True):
    # generic / undefined process: two states joined (no direction)
    c = G_LEFT if s else N_TOP
    return _chart(False, strong=False) + path("M5.5 12.5L13 6", stroke=G_RIGHT if s else INK, sw=2.2) + circle(5.5, 12.5, 2.1, c) + circle(13, 6, 2.1, c)


@glyph("processHeating")
def _p_heating(s=True):
    return _process(s, lambda c: _arrow(4.5, 10, 14.5, 10, c))


@glyph("processCooling")
def _p_cooling(s=True):
    return _process(s, lambda c: _arrow(14.5, 10, 4.5, 10, c))


@glyph("processHumidification")
def _p_humid(s=True):
    return _process(s, lambda c: _arrow(9, 14.5, 9, 4, c))


@glyph("processAdiabatic")
def _p_adiabatic(s=True):
    return _process(s, lambda c: _arrow(13.5, 13.5, 5.5, 5, c))


@glyph("processRoom")
def _p_room(s=True):
    return _process(s, lambda c: _arrow(5, 13.5, 13.5, 5, c))


@glyph("processMixing")
def _p_mixing(s=True):
    return _process(s, lambda c: _arrow(4, 13.5, 8.2, 10.2, c, sw=1.8, head=2.3) + _arrow(14.5, 5, 10.2, 8.6, c, sw=1.8, head=2.3)
                    + circle(9.2, 9.4, 1.3, G_LEFT if s else N_TOP, sw=0.9))


@glyph("processHeatRecovery")
def _p_heat_recovery(s=True):
    return _process(s, lambda c: _arrow(4.5, 7.5, 14, 7.5, c, sw=1.8, head=2.3) + _arrow(14, 12, 4.5, 12, c, sw=1.8, head=2.3))


@glyph("processFan")
def _p_fan(s=True):
    def body(c):
        out = _arrow(8.5, 10, 15, 10, c)
        out += circle(5.8, 10, 2.9, WHITE, sw=1)
        for a in (30, 150, 270):
            r = math.radians(a)
            out += poly([(5.8, 10), (5.8 + 2.4 * math.cos(r), 10 + 2.4 * math.sin(r)),
                         (5.8 + 1.6 * math.cos(r + 0.9), 10 + 1.6 * math.sin(r + 0.9))], c, sw=0.5)
        return out
    return _process(s, body)


# ---------------------------------------------------------------- topology
@glyph("cellComplex")
def _cell_complex(s=True):
    # one solid subdivided into cells: 2 x 2 x 2 cube with the cell seams drawn
    P = iso(10, 10)
    t = tones(s)
    out = box(P, 0, 0, 0, 5, 5, 7, t)
    seams = [(P(2.5, 0, 7), P(2.5, 5, 7)), (P(0, 2.5, 7), P(5, 2.5, 7)),          # top
             (P(0, 5, 3.5), P(5, 5, 3.5)), (P(2.5, 5, 0), P(2.5, 5, 7)),          # left face
             (P(5, 0, 3.5), P(5, 5, 3.5)), (P(5, 2.5, 0), P(5, 2.5, 7))]          # right face
    return out + path("".join(f"M{f(a[0])} {f(a[1])}L{f(b[0])} {f(b[1])}" for a, b in seams), sw=0.9)


# ---------------------------------------------------------------- optimisation
@glyph("algorithm")
def _algorithm(s=True):
    # objective curve converging on its optimum (green)
    return (path("M3 2.5V16H17", sw=1.3) + path("M4.8 3.8C6.6 12.6 8.4 13.6 10.2 13.6S14 10.6 16.4 5.4", stroke=MUTED, sw=1.3)
            + circle(10.2, 13.2, 2.3, G_LEFT if s else N_TOP, sw=1.1))


@glyph("objective")
def _objective(s=True):
    return circle(10, 10, 7.5, WHITE) + circle(10, 10, 5, G_LEFT if s else N_TOP) + circle(10, 10, 2.4, WHITE) + circle(10, 10, 0.9, INK, stroke=None)


# ---------------------------------------------------------------- scripting / automation
@glyph("script")
def _script(s=True):
    c = G_TOP if s else WHITE
    return (rect(1.5, 3, 17, 14, INK if s else MUTED, rx=1.5) + path("M4.5 7L8 10L4.5 13", stroke=c, sw=1.8)
            + path("M9.5 13.2H14", stroke=c, sw=1.8))


@glyph("tasks")
def _tasks(s=True):
    # parallel tasks (multitasking): three lanes running side by side
    out = ""
    for y in (4, 9.5, 15):
        out += rect(2, y - 2, 11, 4, G_LEFT if s else N_TOP, rx=1)
        out += path(f"M13.5 {f(y)}H16.5", sw=1.4) + poly([(16.2, y - 1.6), (18.4, y), (16.2, y + 1.6)], INK, sw=0.6)
    return out


# ---------------------------------------------------------------- acoustics
@glyph("sound")
def _sound(s=True):
    return (poly([(2, 7.5), (5.5, 7.5), (10, 3.5), (10, 16.5), (5.5, 12.5), (2, 12.5)], G_LEFT if s else N_TOP)
            + path("M12.6 7.2a4 4 0 0 1 0 5.6M14.9 4.9a7.2 7.2 0 0 1 0 10.2", sw=1.5))


# ---------------------------------------------------------------- BIM documents (Revit)
@glyph("view")
def _view(s=True):
    # drawing sheet: plan drawing (green) + title block
    return (rect(1.5, 2.5, 17, 14, WHITE, rx=1) + path("M4 5H11V8.5H13.5V13.5H4Z", fill=G_TOP if s else N_FLAT, stroke=DEEP if s else INK, sw=1.2)
            + path("M15.5 2.5V16.5", sw=1))


@glyph("element")
def _element(s=True):
    # a single BIM element instance: cube on a neutral base plate
    P = iso(10, 11.5)
    base = poly([P(-1, -1, 0), P(5.5, -1, 0), P(5.5, 5.5, 0), P(-1, 5.5, 0)], N_FLAT, sw=0.9)
    return base + box(P, 0.5, 0.5, 0, 4, 4, 4.5, tones(s))


# ---------------------------------------------------------------- HVAC plant (SAM Systems)
@glyph("energyCentre")
def _energy_centre(s=True):
    # central plant building with a stack
    P = iso(9, 12)
    n = (N_TOP, N_LEFT, N_RIGHT)
    return box(P, 3.4, 0.2, 0, 1.2, 1.2, 10, n) + box(P, 0, 1.6, 0, 5, 3, 4.5, tones(s))


@glyph("plantRoom")
def _plant_room(s=True):
    # cutaway room corner (the SAM `floor` walls, neutral floor) with a plant unit standing in it (green)
    P = iso(9, 11.5)
    walls = poly([(9, 8), (9, 2), (1, 6), (1, 12)], "#EFEFEF") + poly([(9, 8), (17, 12), (17, 6), (9, 2)], N_RIGHT)
    floor = poly([(9, 8), (17, 12), (9, 16), (1, 12)], N_TOP)
    return walls + floor + box(P, -0.4, 0, 0, 3.4, 2.4, 4.6, tones(s))


@glyph("connector")
def _connector(s=True):
    fill = G_LEFT if s else N_TOP
    return (path("M6.5 10H13.5", stroke=DEEP if s else INK, sw=2.4) + rect(1.5, 6.5, 5, 7, fill, rx=1)
            + rect(13.5, 6.5, 5, 7, fill, rx=1) + circle(10, 10, 1.5, WHITE, sw=1))


# ---------------------------------------------------------------- solar
@glyph("sunPath")
def _sun_path(s=True):
    # sun position -> direction vector (green)
    return (path("M2 17H18", sw=1.3) + _sun(13.8, 5.2, 2.4, rays=True)
            + _arrow(11.6, 7.4, 4.2, 14.6, DEEP if s else INK, sw=2.0))


@glyph("solar")
def _solar(s=True):
    # solar irradiance: parallel rays onto a surface (green)
    return (poly([(10, 5), (17, 8.5), (17, 16), (10, 12.5)], G_LEFT if s else N_LEFT) + _sun(4, 4, 2.4, rays=False)
            + _arrow(5.2, 7.6, 9.6, 10, INK, sw=1.3, head=1.9) + _arrow(7.8, 4.6, 12.4, 7.2, INK, sw=1.3, head=1.9))


# ---------------------------------------------------------------- verb: run / simulate (Evaluate family)
icons.BADGES["run"] = ("evaluate", lambda c: f'<polygon points="17.6,16.6 21.6,19 17.6,21.4" fill="{c}" stroke="{c}" stroke-width="0.8" stroke-linejoin="round"/>')

EXT_GLYPHS = ["mollierChart", "mollierPoint", "mollierProcess", "processHeating", "processCooling", "processHumidification",
              "processAdiabatic", "processRoom", "processMixing", "processHeatRecovery", "processFan", "cellComplex", "algorithm",
              "objective", "script", "tasks", "sound", "view", "element", "energyCentre", "plantRoom", "connector", "sunPath", "solar"]
EXT_BADGES = ["run"]
