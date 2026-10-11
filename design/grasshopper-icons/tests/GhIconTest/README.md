# GhIconTest: real Rhino 8 / Grasshopper icon load test

Hosts the installed Rhino 8 + Grasshopper in-process (McNeel `Rhino.Testing`, the same setup as SAM's
`SAM.Core.Grasshopper.Tests`). Grasshopper loads the installed SAM plugins (`%APPDATA%\SAM` via `SAM.ghlink`).
For every manifest row the test:

* emits the object by its ComponentGuid through `Grasshopper.Instances.ComponentServer` (it must load),
* checks name and category against the source,
* checks `Icon_24x24` is 24×24 and matches the manifest PNG (tolerance: 2 levels per channel, premultiplied-alpha rounding).

It is not part of any solution. Build the repository first (post-build installs to `%APPDATA%\SAM`), then:

```
set SAM_ICON_MANIFESTS=<repo>\design\grasshopper-icons\manifest.json
dotnet test design\grasshopper-icons\tests\GhIconTest
```

**Offline mode.** Some plugins can't load in plain Rhino; for example the SAM_Revit plugins need Rhino.Inside.Revit.
For those, set `SAM_ICON_ASSEMBLY_DIR=<repo>\build`. Any object that Grasshopper can't emit then has its resource read
directly from the built assembly (`ResourceManager`, which resolves no plugin types) and compared with the manifest PNG.
The report counts loaded and offline objects separately. The link from GUID to resource is proven by `tools/integrate.py`
(source re-parse) and `tools/check_source.py`.
