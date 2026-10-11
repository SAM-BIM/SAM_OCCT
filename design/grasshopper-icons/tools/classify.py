"""Classify every inventoried GH object into (object glyph, operation badge, modifiers).

Input : ../manifest_raw.json (from inventory.py)
Output: ../manifest.json + ../manifest.csv  (the source of truth for this repository's icons)

Rule order (first hit wins, all deterministic):
  1. repo_rules.OVERRIDES, then SAM's OVERRIDES (sam_classify.py, vendored verbatim from SAM#166)
  2. verbs  : SAM VERBS, then EXT_VERBS below
  3. nouns  : repo_rules.OBJECTS, then EXT_OBJECTS below (new SAM-BIM glyphs), then SAM OBJECTS
  4. params : repo_rules.PARAM_OBJECTS, EXT_PARAM_OBJECTS, then SAM PARAM_OBJECTS
Qualifiers (`...By<X>`) are not drawn, exactly as in SAM.
"""
import csv
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import icons  # noqa: E402
import icons_ext  # noqa: E402
import repo_rules as R  # noqa: E402
import sam_classify as C  # noqa: E402

# ------------------------------------------------------------------ extra verbs (after SAM's VERBS)
EXT_VERBS = [
    (r"^(Run|Simulate|Execute|Solve|Workflow)", "run"), (r"^Open", "import"), (r"^Print", "export"),
    (r"^(Show|Visuali[sz]e)", "display"), (r"^(Delete|Clear)", "remove"), (r"^Query", "get"), (r"^Assign", "set"),
    (r"^(Renumber|Rationalise|Clean|Repair)", "modify"), (r"^(Verify|Validate)", "validate"), (r"^Compare", "analyse"),
    (r"^(Assemble|Sew)", "merge"), (r"^Tag", "display"), (r"^(Duplicated|Connected|Ordered|Related)", "get"),
]

# ------------------------------------------------------------------ extra nouns -> SAM-BIM extension glyphs (before SAM's OBJECTS)
EXT_OBJECTS = [
    ("AdiabaticHumidification", "processAdiabatic", None), ("IsothermalHumidification", "processHumidification", None),
    ("SteamHumidification", "processHumidification", None), ("HeatingProcess", "processHeating", None),
    ("CoolingProcess", "processCooling", None), ("MixingProcess", "processMixing", None),
    ("HeatRecoveryProcess", "processHeatRecovery", None), ("FanProcess", "processFan", None), ("RoomProcess", "processRoom", None),
    ("UndefinedProcess", "mollierProcess", None), ("MollierProcess", "mollierProcess", None), ("MollierPoint", "mollierPoint", None),
    ("MollierGroup", "group", None), ("MollierChart", "mollierChart", None), ("Diagram", "mollierChart", None),
    ("Psychrometric", "mollierChart", None), ("Mollier", "mollierChart", None),
    ("CellComplex", "cellComplex", None), ("Topology", "cellComplex", None),
    ("Algorithm", "algorithm", None), ("GenOpt", "algorithm", None), ("Solver", "algorithm", None), ("Objective", "objective", None),
    ("Script", "script", None), ("Python", "script", None), ("Macro", "script", None), ("Multitasker", "tasks", None),
    ("Absorption", "sound", None), ("BandValues", "sound", None), ("Acoustic", "sound", None),
    ("Sheet", "view", None), ("View", "view", None), ("ElementId", "element", None), ("UniqueId", "element", None),
    ("Element", "element", None),
    ("EnergyCentre", "energyCentre", None), ("PlantRoom", "plantRoom", None), ("Connector", "connector", None),
    ("SunDirection", "sunPath", None), ("SunPath", "sunPath", None), ("Irradiance", "solar", None), ("SunExposure", "solar", None),
    ("SunAnalysis", "solar", None), ("SolarSimulation", "solar", None),
]
EXT_PARAM_OBJECTS = {
    "MollierPoint": "mollierPoint", "MollierProcess": "mollierProcess", "MollierGroup": "group",
    "MollierObject": "mollierChart", "MollierChartObject": "mollierChart", "CellComplex": "cellComplex", "ResolvedCellComplex": "cellComplex",
    "Algorithm": "algorithm", "Objective": "objective", "Script": "script", "BandValues": "sound",
    "SystemEnergyCentre": "energyCentre", "SystemPlantRoom": "plantRoom",
}
NO_PLURAL = {g for g in icons_ext.EXT_GLYPHS if g.startswith(("mollier", "process"))}  # charts do not stack


def _param_type(x):
    """Type key of a param: from `{typeof(X).Name}` / a literal name, and from the Goo<X>Param class name."""
    name = x["name"] or ""
    m = re.fullmatch(r"\{typeof\((?:[\w:.]+\.)?(\w+)\)\.Name\}", name)
    by_name = m.group(1) if m else name.split(".")[-1]
    by_class = re.sub(r"^Goo|Param$", "", x["class"])
    return by_class, by_name


def _lookup_param(x):
    for key in _param_type(x):
        for table in (R.PARAM_OBJECTS, EXT_PARAM_OBJECTS, C.PARAM_OBJECTS):
            if key in table:
                v = table[key]
                obj, container, plural = (v, None, False) if isinstance(v, str) else (v + (False,))[:3]
                if table is C.PARAM_OBJECTS and key in C.PARAM_LIBRARY:
                    container = "library"
                return obj, container, bool(plural), key
    raise KeyError(f"no param glyph for {x['class']} / {x['name']} (add it to repo_rules.PARAM_OBJECTS)")


def _short_candidates(name):
    s = C.short(name)
    out = [s]
    t = re.sub(r"^\w+\.", "", s)  # any plugin prefix: SAMMollier., Tas., SAMOCCT., Revit., ...
    if t != s:
        out.append(t)
    return out


def classify(x):
    name = x["name"] or ""
    if x["category"] == "Params" or x["base_type"].startswith(("GH_PersistentParam", "GH_Param")):
        obj, container, plural, key = _lookup_param(x)
        return dict(kind="param", object=obj, op=None, plural=plural, container=container, rule="param:" + key)
    for table, tag in ((R.OVERRIDES, "override"), (C.OVERRIDES, "sam-override")):
        if name in table:
            obj, op, extra = table[name]
            return dict(kind="component", object=obj, op=op, plural=extra == "plural",
                        container="library" if extra == "library" else None, rule=tag,
                        note=extra if extra not in (None, "plural", "library") else None)
    verbs = list(R.VERBS) + C.VERBS + EXT_VERBS
    nouns = list(R.OBJECTS) + EXT_OBJECTS + C.OBJECTS
    cands = _short_candidates(name)
    s, op = cands[0], None
    for cand in cands:
        op = next((o for rx, o in verbs if re.match(rx, cand)), None)
        if op:
            s = cand
            break
    else:
        s = cands[-1]
    body = re.split(r"By[A-Z]", s, 1)[0]
    obj, container = None, None
    for text in (body, s):
        for key, o, c in nouns:
            if key in text:
                obj, container = o, c
                break
        if obj:
            break
    if x["base_type"].startswith("GH_SAMEnumComponent") and op is None:
        op = "value"
    plural = bool(re.search(C.PLURAL_RE + r"(By|$|[A-Z])", body)) and container is None
    return dict(kind="component", object=obj, op=op, plural=plural, container=container, rule="rule")


def normalise(c):
    c = C.normalise(c)  # ADD: plurality not drawn
    if c["object"] in NO_PLURAL:
        c["plural"] = False
    return c


def origin(c):
    g = "SAM-BIM ext v" + icons_ext.EXT_VERSION if c["object"] in icons_ext.EXT_GLYPHS else "SAM"
    b = "SAM-BIM ext v" + icons_ext.EXT_VERSION if c["op"] in icons_ext.EXT_BADGES else "SAM"
    return g, b


def main():
    raw = json.load(open(os.path.join(DESIGN, "manifest_raw.json"), encoding="utf-8"))
    out, problems = [], []
    for x in raw:
        c = normalise(classify(x))
        if not c["object"] or c["object"] not in icons.G or (c["kind"] == "component" and not c["op"]) or (c["op"] and c["op"] not in icons.BADGES):
            problems.append((x["class"], x["name"], c))
            continue
        iid = C.icon_id(c)
        g_src, b_src = origin(c)
        out.append({
            "class": x["class"], "name": x["name"], "nickname": x["nickname"], "category": x["category"],
            "subcategory": x["subcategory"], "project": x["project"], "project_dir": x["project_dir"], "source": x["source"],
            "guid": x["guid"], "kind": c["kind"], "exposure": x["exposure"], "obsolete": x["obsolete"],
            "current_icon": x["icon_expr"], "icon_holder": x["icon_holder"],
            "object": c["object"], "op": c["op"], "plural": c["plural"], "container": c["container"],
            "glyph_origin": g_src, "badge_origin": b_src if c["op"] else None,
            "rule": c["rule"], "note": c.get("note"), "icon_id": iid, "resource": C.resource_name(iid),
            "icon_file": f"svg/{iid}.svg", "status": "classified",
        })
    for cls, n, c in problems:
        print("UNCLASSIFIED", cls, n, c)
    out.sort(key=lambda r: (r["project"], r["object"], r["op"] or "", r["name"] or "", r["guid"]))
    json.dump(out, open(os.path.join(DESIGN, "manifest.json"), "w", encoding="utf-8"), indent=1)
    with open(os.path.join(DESIGN, "manifest.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(out[0].keys()) if out else ["class"])
        w.writeheader()
        w.writerows(out)
    print(len(out), "classified,", len(problems), "unclassified,", len({r['icon_id'] for r in out}), "distinct icons")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
