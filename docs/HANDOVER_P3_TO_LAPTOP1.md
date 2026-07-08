# P3 Handover → Laptop 1

Date: 2026-07-03 · From: laptop 2 (Windows VM) · Phase: **P3 — ABI v4 native history export**

Read this top to bottom before touching anything. It is written so another Claude session
can continue without guessing. Authoritative design record:
`docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md` (where it and the plan differ, the review wins).

---

## 1. Branch & latest commit

- **Branch:** `fix/solver-raw-first`
- **HEAD:** `099301c` — `feat(P3): ABI v4 native history/wires/tolerance export (checkpoint)`
- **Pushed?** As of writing, `origin/fix/solver-raw-first` = `2ad5bbf`. Two commits are being
  pushed with this handover: `6000878` (spike results) and `099301c` (native ABI v4). After the
  push that accompanies this file, `git pull --ff-only` on laptop 1 gets everything.
- Recent history (newest first): `099301c` native ABI v4 · `<this doc commit>` · `6000878` spike
  results · `2ad5bbf` design review · `c4a2eac` P2B closure fix · `ea522ca` P2B tests.

## 2. P3 status (per the plan's 6 steps)

| Step | What | Status |
|---|---|---|
| 0 | Persist design review + commit | ✅ DONE (`2ad5bbf`) |
| 1 | Native spike S1–S7 | ✅ DONE — **all 7 GO**, no fallback needed. Results in plan §E-P3 (`6000878`) |
| 2 | ABI v4 native | 🟡 **CODE COMPLETE & COMMITTED (`099301c`), compiles, exports verified — but NOT validated end-to-end** |
| 3 | Managed `OcctHistory` + probe | ⛔ NOT STARTED |
| 4 | ResolveStage composition + demote `NearestSourceIndex` | ⛔ NOT STARTED |
| 5 | Tests + fixtures | ⛔ NOT STARTED |
| 6 | Docs (TESTING.md, PR body, scope-cut note) | ⛔ NOT STARTED (only the plan spike note is done) |

**The single most important status fact:** the native code compiles and exports all 8 new
symbols, but **no test has ever run against the new DLL**. History-capture correctness and
observationality (golden masters unchanged) are **unverified**. That verification is the next step.

## 3. Files changed this session

**Committed to git** (source only — the built DLL is gitignored):
- `docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md` (new, `2ad5bbf`)
- `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` (spike-results note in §E-P3, `6000878`)
- `docs/HANDOVER_P3_TO_LAPTOP1.md` (this file)
- Native (`099301c`):
  - `native/SAM.Occt.Native/src/History.h` (new, 62 lines)
  - `native/SAM.Occt.Native/src/History.cpp` (new, 217 lines)
  - `native/SAM.Occt.Native/src/OcctNativeCore.h` (+45: `HistoryRecord`, `Result` history/tolerance fields, `FreeWire`, `Validation.wires`, `append_solid_to_result` optional out-param)
  - `native/SAM.Occt.Native/src/CellComplexBuilder.cpp` (+187: capture in `build_cell_complex` & `merge_coplanar`, 5 result accessors)
  - `native/SAM.Occt.Native/src/ShapeValidate.cpp` (+211: `build_free_wire`, wire grouping in `collect_free_bounds`, 4 wire accessors)
  - `native/SAM.Occt.Native/src/ShapeHandle.cpp` (+49: `abi_version` → 4, `sam_occt_shape_max_tolerance`)
  - `native/SAM.Occt.Native/include/sam_occt.h` (+97: ABI v4 declarations block)
  - `native/SAM.Occt.Native/CMakeLists.txt` (+1: add `src/History.cpp`)

**NOT in git (do not expect these on laptop 1):**
- The throwaway OCCT spike (`spike.cpp` / `build_run.bat`) lives in laptop 2's scratchpad temp
  dir, not the repo. Its logic and verdicts are fully recorded in plan §E-P3 — **do not re-run it.**
- Rebuilt `build/SAM.Occt.Native.dll` (ABI v4, gitignored) — laptop 1 must rebuild its own.

## 4. Tests run & exact results

- **Native OCCT spike S1–S7** (throwaway `cl.exe` exe, OCCT 8.0.0): **all 7 GO.**
  S1 MakerVolume split+delete, S2 Sewing ModifiedSubShape, S3 ReShape History, **S4
  UnifySameDomain History (the key unknown — GO)**, S5 History::Merge, S6 FreeBounds wires+owners,
  S7 ShapeTolerance. Exact numbers in plan §E-P3.
- **Unit tests** (`Testing/SAM.OCCT.UnitTests`): last green run **256/256** at `c4a2eac` (P2B).
  **NOT re-run after ABI v4** (managed unit tests don't touch native; expected unaffected).
- **Integration tests** (`Testing/SAM.OCCT.IntegrationTests`): last green run **101 passed, 1
  skipped, 0 failed** at `c4a2eac`. **NOT re-run against the ABI v4 DLL.**
- **Against the new native DLL: zero tests run.** This is the validation gap to close first.

## 5. Failing tests / warnings / unresolved

- **No failing tests** (none were run against the new code).
- **Native build warning (pre-existing, harmless):** `C4005 'NOMINMAX' macro redefinition` in
  `DataExchangeRuntime.cpp`. Unrelated to P3.
- **Git cosmetic:** LF→CRLF warnings on `History.cpp`/`.h` (line endings only).
- **Unresolved / unverified (IMPORTANT):**
  - History-capture correctness is unverified against real fixtures. I was about to run an ABI
    probe + split/merge fixtures when the session handed over.
  - The ShapeFix-history composition (`shape_fix.Context()->History()` merged into MakerVolume
    history) is plausible but unproven on real cell complexes — if a face is silently unmapped,
    the managed layer (Step 3) must emit a `HistoryGap` diagnostic, never crash.
  - `build-native.ps1` hardcodes **VS 2022** paths; laptop 2 has **VS 18 (2026)** and built via
    direct cmake. Laptop 1's toolchain determines which build path works (see §9).

## 6. Important design decisions

1. **Output-face identity = FLAT DECODE ORDINAL** (cell-major, face-minor — the order the managed
   `SelectMany` walk produces). `append_solid_to_result` collects the per-ordinal `TopoDS_Face`
   **only for accepted cells**, so ordinals line up exactly with the managed decode. Translation
   uses an `IsSame` (orientation-independent) ordinal map; a face shared by two cells gets **both**
   ordinals.
2. **Unchanged faces map to self.** `BRepTools_History` records only *changes*, so an input face
   with an empty `Modified()` that isn't `IsRemoved` is mapped to its own ordinal(s).
3. **ShapeFix history is composed** onto MakerVolume history via `BRepTools_History::Merge`
   (belt-and-suspenders; empty merge is a no-op for clean complexes).
4. **`OcctHistory` will be a pure managed snapshot** (Step 3) — the `Result` struct was extended
   additively; there is **no new native handle type**, nothing new to dispose/leak.
5. **Wires live on the validation handle** (reuse the existing FreeBounds pass). `wire_info`
   returns **both** `point_count` and `edge_count`. `history_entries` takes **explicit capacities**
   (status 11 on undersize).
6. **No fallback path built** — S4 proved USD history works, so the review's managed same-plane
   fallback is not needed.

## 7. Deviations from the design review

1. **Sew hop history scope cut (review-sanctioned, §F.4).** History is captured in
   `build_cell_complex` (MakerVolume) and `merge_coplanar` (its internal sew→USD, composed). The
   standalone `sam_occt_sew_faces`→`sam_occt_shape_decode` path (used by ResolveStage's *adaptive
   residual sew* hop) does **NOT** capture history. Consequence: when that rarely-hit sew hop is
   adopted, its faces get no history → Step 4 composition must **skip that hop** and fall back to
   `NearestSourceIndex` for those faces (exactly today's behavior). **This must be documented in
   Step 6 as an explicit scope cut.** Pre-merge and post-merge coplanar hops ARE covered.
2. **Wire owner-face index simplification.** The review described resolving the owner "through the
   internal sew's `ModifiedSubShape` map." Implemented instead as the owner face's position in the
   validated shape's `TopExp` face-enumeration order (best-effort, `-1` when unknown). Relies on
   the validated shape's face order matching the caller's face-list order (true for the
   `Query.Validate(List<Face3D>)` path). Review explicitly allows `-1`-tolerant best-effort, and
   AutoTune3D falls back to midpoint-on-face matching, so this is within bounds — but re-confirm
   the ordering assumption when Step 4/Phase 5 consume it.
3. Everything else follows the review (flat ordinals, validation-handle wires, managed snapshot,
   per-adopted-hop composition, capacities, two-part tolerance test intent).

## 8. Next safest step for laptop 1

**Validate the native ABI v4 end-to-end BEFORE writing any managed code.** Concretely, in order:
1. Rebuild the native DLL (§9) and confirm `abi_version == 4` and the new exports are present.
2. **Run the existing integration + unit suites against the rebuilt DLL.** They MUST stay green
   with **unchanged golden-master signatures** — that proves history capture is observational. A
   changed signature = a capture side-effect bug → STOP and diagnose, do not re-baseline.
3. Write ONE throwaway probe or the first integration test: build the split fixture (unit box's 6
   outer faces + a mid-plane face at z=0.5 = **7 input faces**, `avoid_internal_shapes=0`), assert
   `history_available==1`, `input_count==7`, a side wall face `modified_count==2` (split), and the
   mid face maps to **2** ordinals (shared internal face). This is the go/no-go on the capture.
4. Only then start **Step 3** (managed `OcctHistory` snapshot + `ToSourceMap` adapter + probe),
   then Step 4, 5, 6.

## 9. Commands laptop 1 runs first

```bash
cd <repo>/SAM_OCCT
git fetch origin && git checkout fix/solver-raw-first && git pull --ff-only
git log --oneline -3   # expect 099301c at or near HEAD

# --- Rebuild the ABI v4 native DLL ---
# Preferred (only if VS 2022 Community is at the path build-native.ps1 hardcodes):
powershell -ExecutionPolicy Bypass -File build-native.ps1

# If that fails on toolchain paths (laptop 2 had VS 18 / 2026, not 2022), drive cmake directly
# with your machine's cmake.exe; OCCT 8.0.0 SDK is expected at C:\OCCT\occt-8.0.0:
#   cmake -S native/SAM.Occt.Native -B native/build/x64-windows
#   cmake --build native/build/x64-windows --config Release
# (This outputs build/SAM.Occt.Native.dll; the OCCT runtime DLLs are already deployed in build/.)

# --- Confirm the ABI ---
dumpbin /exports build/SAM.Occt.Native.dll | findstr /i "history wire max_tolerance abi_version"
# expect: abi_version + result_history_available/entries/face/input_count + result_max_tolerance
#         + validation_wire_count/info/point/edge_owner + shape_max_tolerance

# --- Prove observationality (MUST be green, signatures unchanged) ---
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj
```

Laptop 2 toolchain, for reference if laptop 1 matches it: cmake at
`C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe`,
MSVC 14.51.36231, OCCT `C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64` (manual include/lib dirs;
no vcpkg needed for OCCT — the prior build tree at `native/build/x64-windows/CMakeCache.txt`
records the exact settings).

## 10. Must NOT redo / must avoid

- **Do NOT re-run the OCCT spike S1–S7** — done, all GO, recorded in plan §E-P3. In particular,
  **do not re-investigate UnifySameDomain history** or build the managed same-plane fallback: S4
  proved USD history works.
- **Do NOT re-implement the native ABI** — it is committed at `099301c`. Extend/validate it; don't
  rewrite. If a bug surfaces, fix in place.
- **Do NOT commit `build/*.dll`** — gitignored by design; every machine rebuilds.
- **Do NOT change solver geometry or re-baseline golden masters.** P3 is strictly observational. A
  golden-master signature change means a capture side-effect bug — STOP and report, don't accept it.
- **Do NOT run `build-native.ps1` unmodified if your VS install isn't at its hardcoded 2022 path** —
  it throws on the toolchain check. Use the direct cmake commands instead.
- **Do NOT rebase/squash the already-pushed commits** (`ea522ca`, `c4a2eac`, `2ad5bbf`, and — once
  pushed with this handover — `6000878`, `099301c`).
- **Do NOT look for the spike source in the repo** — it's throwaway in laptop 2's temp scratchpad,
  unreachable from laptop 1 and unnecessary (verdicts are in the plan).
- The **adaptive residual sew hop has no native history by design** (§7.1) — do not treat its
  missing history as a bug; wire the Step 4 composition to skip it gracefully.
