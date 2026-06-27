# Third-Party Notices

## Open CASCADE Technology

SAM_OCCT is designed to dynamically link Open CASCADE Technology (OCCT) through
the `SAM.Occt.Native` bridge.

OCCT is distributed under the GNU Lesser General Public License version 2.1 with
an additional exception. Before shipping OCCT DLLs with any SAM installer,
include the OCCT license text, copyright notices, source-location information,
and instructions for replacing the OCCT binaries.

### Data Exchange toolkits (STEP / IGES — issue #20)

The STEP and IGES import/export bridge links the OCCT Data Exchange toolkits
`TKDESTEP`, `TKDEIGES` and `TKXSBase` (`XSControl_Reader`, the readers' base
class, lives in `TKXSBase`). These ship under the same OCCT
LGPL-2.1-with-exception licence as the rest of the OCCT stack. STEP (ISO 10303)
and IGES are open, published interchange formats.

**Runtime DLLs required.** `TKDESTEP.dll`/`TKDEIGES.dll` are *delay-loaded*, so
`SAM.Occt.Native` keeps loading (and every other operation keeps working) even
when they are absent — STEP/IGES then reports native status 64. To actually run
import/export, the **full transitive closure** must be deployed beside the
existing OCCT DLLs, which is broader than the core modeling set:

- the Data Exchange toolkits `TKDESTEP.dll`, `TKDEIGES.dll`, `TKXSBase.dll`;
- the XDE/CAF stack they pull in: `TKXCAF`, `TKLCAF`, `TKCAF`, `TKVCAF`,
  `TKCDF`, `TKDE`, `TKBinXCAF`, `TKXmlXCAF`;
- the OCCT visualization toolkits those reference: `TKV3d`, `TKService`,
  `TKMesh`, `TKHLR`;
- and the **third-party runtime** `TKService`/`TKV3d` need — `freetype.dll` and
  `FreeImage.dll` — which the modeling-only runtime never required.

`build-native.ps1` deploys this whole set (it copies the full OCCT `bin` and
every `*\bin\*.dll` under the OCCT `3rdparty` root into `build/` and
`%APPDATA%\SAM`). When packaging a SAM installer, verify the closure with
`dumpbin /dependents` on the freshly built `SAM.Occt.Native.dll` and on
`TKDESTEP.dll`.

Project site:

https://dev.opencascade.org/

Licensing:

https://dev.opencascade.org/resources/licensing

## Distribution default

The intended default is dynamic linking only. Static linking requires separate
license/legal review.
