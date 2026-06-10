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
`TKDESTEP`, `TKDEIGES`, `TKDE` and `TKXSBase` (plus their transitive
dependencies). These ship under the same OCCT LGPL-2.1-with-exception licence as
the rest of the OCCT stack. STEP (ISO 10303) and IGES are open, published
interchange formats. When shipping the native bundle, include
`TKDESTEP.dll`, `TKDEIGES.dll`, `TKDE.dll` and `TKXSBase.dll` (and any
transitive Data Exchange DLLs the loader pulls — verify with `dumpbin
/dependents` after the native build) alongside the existing OCCT DLLs.

Project site:

https://dev.opencascade.org/

Licensing:

https://dev.opencascade.org/resources/licensing

## Distribution default

The intended default is dynamic linking only. Static linking requires separate
license/legal review.
