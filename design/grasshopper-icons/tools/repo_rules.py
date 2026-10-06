"""Repository-specific classification decisions for SAM_OCCT (the only non-shared tool file).

OVERRIDES     : component display name -> (object glyph, op, extra)   extra: None | "plural" | "library" | note
PARAM_OBJECTS : param type key (Goo<X>Param class or typeof(X) name) -> glyph | (glyph, container, plural)
OBJECTS/VERBS : extra noun/verb rules tried before the shared ones (same shapes as SAM's OBJECTS/VERBS)
"""
# Shell booleans follow SAM's own geometry overrides (SAMGeometry.ShellsUnion/ShellIntersection/...); STEP/IGES = shell import/export.
OVERRIDES = {
    "SAMOCCT.ShellsUnion": ("shell", "union", "plural"), "SAMOCCT.ShellsIntersection": ("shell", "intersect", None),
    "SAMOCCT.ShellsDifference": ("shell", "difference", None), "SAMOCCT.ShellsSplit": ("shell", "split", None),
    "SAMOCCT.ShellsSectionByPlane": ("sectionBox", "section", None), "SAMOCCT.ShellsOffset": ("shell", "offset", None),
    "SAMOCCT.ShellsThicken": ("shell", "modify", "hollow into a wall of given thickness"),
    "SAMOCCT.ShellsRepair": ("shell", "update", "rebuild/repair"), "SAMOCCT.ShellsImprint": ("face", "split", "imprint shared boundaries"),
    "SAMOCCT.ShellsDistance": ("shell", "calculate", None),
    "SAMOCCT.ImportIGES": ("shell", "import", "plural"), "SAMOCCT.ImportSTEP": ("shell", "import", "plural"),
    "SAMOCCT.ExportIGES": ("shell", "export", "plural"), "SAMOCCT.ExportSTEP": ("shell", "export", "plural"),
    "SAMOCCT.ExtrudeFootprints": ("shell", "create", "plural"), "SAMOCCT.Sew": ("shell", "merge", None),
    "SAMOCCT.TriangulateSurface": ("face", "triangulate", None), "SAMOCCT.Validate": ("face", "validate", None),
    "SAMOCCT.Solve3D": ("cluster", "run", "full 3D panel solver"),
    "SAMOCCT.AutoTune3D": ("algorithm", "run", "diagnosis-driven auto-tune"),
    "SAMOCCT.AutoTune3D (Discover)": ("algorithm", "get", "discovers optimal solver parameters"),
    "SAMOCCT.Clean3D": ("panel", "modify", "plural"), "SAMOCCT.Extend3D": ("panel", "extend", "plural"),
    "SAMOCCT.CreateTower": ("cluster", "create", None), "SAMOCCT.PanelsFromShells": ("panel", "create", "plural"),
}
PARAM_OBJECTS = {}
OBJECTS = []
VERBS = []
