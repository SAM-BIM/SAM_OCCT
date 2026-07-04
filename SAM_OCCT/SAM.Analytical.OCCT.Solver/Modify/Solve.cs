// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    public static partial class Modify
    {
        /// <summary>
        /// Analytical entry point for the true 3D panel solver. Wraps
        /// <see cref="Panel3DSnapSolver"/>: skips air panels, derives the capture width from each
        /// panel's real construction thickness, snaps lower-weight panels onto higher-weight
        /// backers in full 3D, and resolves junctions through the native OCCT kernel.
        /// </summary>
        /// <param name="panels">Panels to solve. Not modified; new panels are returned.</param>
        /// <param name="nakedPoint3Ds">Locations of naked (free) boundary edges reported by the validator.</param>
        /// <param name="diagnostics">Coded diagnostics describing the solve.</param>
        /// <param name="weights">Optional per-panel backer weights (aligned with the non-air panel order); null uses the default.</param>
        /// <param name="maxExtends">Optional per-panel lateral extend reach (the Solver's <c>SolverParameter.MaxExtend</c>),
        /// how far a wall may grow sideways to meet the next wall and close the plan loop; null reads
        /// <c>SolverParameter.MaxExtend</c> off each panel, falling back to 0.4 m.</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <param name="options">OCCT build options (distance/fuzzy/glue tolerances).</param>
        /// <param name="forceManagedPipeline">Diagnostic-only: skip the raw-first attempt and always run the
        /// managed clean/extend/resolve pipeline, even on input the raw solve would otherwise adopt watertight.
        /// Lets golden-master tests capture the managed-pipeline signature on well-modelled fixtures (see
        /// <see cref="Geometry.OCCT.Solver.Panel3DSnapSolver.ForceManagedPipeline"/>). Default false preserves
        /// the existing raw-first-then-managed-fallback behaviour.</param>
        /// <returns>The resolved panels, or null when no usable panels were supplied.</returns>
        public static List<Panel> Solve3D(
            this IEnumerable<Panel> panels,
            out List<Point3D> nakedPoint3Ds,
            out List<string> diagnostics,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3,
            OcctBuildOptions options = null,
            bool forceManagedPipeline = false)
        {
            return Solve3D(panels, out nakedPoint3Ds, out diagnostics, out _, weights, maxExtends, minBucketSize, thicknessFactor, alignColinearOffset, normalizeCapOffset, options, forceManagedPipeline);
        }

        /// <summary>
        /// Phase 4 overload: as <see cref="Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool)"/>,
        /// additionally reporting apertures that could not be re-hosted on any resolved panel (the
        /// orphan policy: docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 4). Solved panels
        /// preserve the source's Guid/parameters/construction on a clean 1:1 mapping, get a fresh Guid
        /// stamped with the original source's Guid on a split, and keep the dominant (largest-area)
        /// source's Guid (with the others stamped) on a merge - see <see cref="PanelReconstruction"/>.
        /// </summary>
        /// <param name="orphanedApertures">Apertures whose source panel contributed to the solve but no
        /// resolved output face came within <paramref name="maxApertureDistance"/> of them - original
        /// world-space geometry and source Guid, for manual re-hosting. Never silently dropped.</param>
        /// <param name="minApertureArea">Minimum aperture area to re-host onto a resolved panel (the Panel ctor's own gate).</param>
        /// <param name="maxApertureDistance">Max distance between a resolved panel and an aperture for it to be re-hosted there.</param>
        public static List<Panel> Solve3D(
            this IEnumerable<Panel> panels,
            out List<Point3D> nakedPoint3Ds,
            out List<string> diagnostics,
            out List<OrphanedAperture> orphanedApertures,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3,
            OcctBuildOptions options = null,
            bool forceManagedPipeline = false,
            double minApertureArea = Tolerance.MacroDistance,
            double maxApertureDistance = Tolerance.MacroDistance)
        {
            return Solve3D(panels, out nakedPoint3Ds, out diagnostics, out orphanedApertures, out _, weights, maxExtends, minBucketSize, thicknessFactor, alignColinearOffset, normalizeCapOffset, options, forceManagedPipeline, minApertureArea, maxApertureDistance);
        }

        /// <summary>
        /// Phase 8 overload: as <see cref="Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, out List{OrphanedAperture}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool, double, double)"/>,
        /// additionally returning a <see cref="Solve3DReport"/> bundling every staged/diagnostic field the
        /// solver already produces (closure signatures, diagnostics, source map, cells, naked wires, Stage A
        /// clean faces, level frames) for Grasshopper inspection
        /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 8). Solves geometry identically to the
        /// other overloads; this only adds reporting.
        /// </summary>
        /// <param name="report">The staged solve snapshot; never null, even when the solve produced no result.</param>
        /// <param name="classifyCells">When true, runs the additive Phase 7b cell classification
        /// (<see cref="CellClassifier.ClassifyCells"/>) - one extra native envelope decode - and populates
        /// <see cref="Solve3DReport.CellRoles"/>. Default false (no extra native cost unless requested).</param>
        /// <param name="minCellVolume">Minimum cell volume (m3) below which a cell classifies <see cref="CellRole.Sliver"/>; only used when <paramref name="classifyCells"/> is true.</param>
        public static List<Panel> Solve3D(
            this IEnumerable<Panel> panels,
            out List<Point3D> nakedPoint3Ds,
            out List<string> diagnostics,
            out List<OrphanedAperture> orphanedApertures,
            out Solve3DReport report,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3,
            OcctBuildOptions options = null,
            bool forceManagedPipeline = false,
            double minApertureArea = Tolerance.MacroDistance,
            double maxApertureDistance = Tolerance.MacroDistance,
            bool classifyCells = false,
            double minCellVolume = 0.05)
        {
            nakedPoint3Ds = new List<Point3D>();
            diagnostics = new List<string>();
            orphanedApertures = new List<OrphanedAperture>();
            report = null;

            if (!PrepareInput(panels, minBucketSize, thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources))
            {
                diagnostics.Add("SAM_OCCT_SOLVE3D_INPUT_EMPTY: No valid non-air panel geometry was supplied.");
                report = new Solve3DReport(false, null, null, null, null, sources, null, null, null, null, null, false, 0);
                return null;
            }

            List<double> effectiveWeights = ResolveWeights(weights, sources);
            List<double> effectiveMaxExtends = ResolveMaxExtends(maxExtends, sources);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds, bucketSizes, effectiveWeights, effectiveMaxExtends)
            {
                Up = ResolveUp(sources),
                AlignColinearOffset = alignColinearOffset,
                NormalizeCapOffset = normalizeCapOffset,
                ForceManagedPipeline = forceManagedPipeline
            };
            solver.Execute(options);

            nakedPoint3Ds = solver.NakedEdgePoint3Ds ?? new List<Point3D>();

            List<Face3D> resolved = solver.ResolvedFace3Ds;
            if (resolved == null || resolved.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_SOLVE3D_NO_RESULT: The solver produced no resolved faces.");
                report = BuildReport(solver, sources, options, classifyCells, minCellVolume);
                return new List<Panel>();
            }

            double tolerance = options?.Tolerance ?? Tolerance.Distance;

            // Phase 4: rebuild via the source-set-aware policy (1:1 keeps the source's Guid, a split gets
            // fresh Guids stamped back to the source, a merge keeps the dominant source's Guid) instead of
            // the single-winner BuildPanels/NearestSourceIndex path. solver.SourceMap is always populated
            // (exact via composed native history when Phase 3 provided it, geometric fallback otherwise),
            // so every resolved face still gets a Panel even where history is unavailable.
            List<Panel> result = PanelReconstruction.Build(resolved, sources, solver.SourceMap, bucketSizes, effectiveWeights, effectiveMaxExtends, tolerance, out orphanedApertures, minApertureArea, maxApertureDistance);

            foreach (OrphanedAperture orphan in orphanedApertures)
            {
                diagnostics.Add(string.Format(
                    "SAM_OCCT_SOLVE3D_APERTURE_ORPHANED: Aperture {0} from source panel {1} did not land within {2} m of any resolved panel; returned for manual re-hosting.",
                    orphan.Aperture?.Guid,
                    orphan.SourceGuid,
                    maxApertureDistance));
            }

            // Step-2 gap-fill faces (residual naked-boundary loops) become air panels: each is emitted as a
            // PanelType.Air panel (null construction), a virtual boundary rather than solid wall. Sliver
            // loops (near-zero area) are micro-gaps the native resolve leaves around aligned/merged edges -
            // skip them so they do not surface as degenerate air panels.
            const double minAirArea = 1e-4; // 1 cm^2: below any real opening/gap, above float-noise slivers
            int airCount = 0;
            foreach (Face3D holeFace3D in solver.HoleFillFace3Ds ?? new List<Face3D>())
            {
                if (holeFace3D == null || !holeFace3D.IsValid() || holeFace3D.GetArea() <= minAirArea)
                {
                    continue;
                }

                Panel airPanel = global::SAM.Analytical.Create.Panel(null, PanelType.Air, holeFace3D);
                if (airPanel != null)
                {
                    // Provenance-stamped so a gap-fill air panel is distinguishable from a "real" opening
                    // an analytical model might otherwise carry (docs plan Phase 4).
                    airPanel.SetValue(PanelProvenanceParameter.Provenance, "GapFill");
                    result.Add(airPanel);
                    airCount++;
                }
            }

            diagnostics.Add(string.Format(
                "SAM_OCCT_SOLVE3D_AIR: Created {0} air panel(s) from closed gaps.", airCount));

            diagnostics.Add(string.Format(
                "SAM_OCCT_SOLVE3D_RESULT: Solved {0} panel(s) into {1} resolved panel(s); nativeResolved={2}; {3} cell(s); {4} naked edge(s).",
                face3Ds.Count,
                result.Count,
                solver.NativeResolved,
                solver.ResolvedCellCount,
                nakedPoint3Ds.Count));

            // Surface the structured solver diagnostics (gate rejections, adopted level, ...) alongside the
            // existing SAM_OCCT_* summary lines, so every rejection's reason is visible without changing this
            // method's List<string> diagnostics contract.
            diagnostics.AddRange(SolverReportFormat.FormatDiagnostics(solver.Diagnostics, "SAM_OCCT_SOLVE3D"));

            report = BuildReport(solver, sources, options, classifyCells, minCellVolume);

            return result;
        }

        /// <summary>Assembles a <see cref="Solve3DReport"/> from a solver that has already run <c>Execute</c>,
        /// optionally running the additive Phase 7b cell classification.</summary>
        private static Solve3DReport BuildReport(Panel3DSnapSolver solver, List<Panel> sources, OcctBuildOptions options, bool classifyCells, double minCellVolume)
        {
            IReadOnlyList<CellRole> cellRoles = null;
            if (classifyCells && solver.Cells != null && solver.Cells.Count != 0)
            {
                cellRoles = CellClassifier.ClassifyCells(solver.Cells, solver.ResolvedFace3Ds, minCellVolume, options, solver.Diagnostics);
            }

            return new Solve3DReport(
                solver.RawAdopted,
                solver.Signature,
                solver.RawAttemptSignature,
                solver.Diagnostics,
                solver.SourceMap,
                sources,
                solver.Cells,
                cellRoles,
                solver.NakedWires,
                solver.CleanFace3Ds,
                solver.LevelFrames,
                solver.NativeResolved,
                solver.ResolvedCellCount);
        }

        /// <summary>
        /// Step 1 only - the clean bucket. Skips air panels, derives the capture width from construction
        /// thickness, then returns clean single panels: external shape only (openings stripped), within-bucket
        /// near-parallel panels snapped onto one backer, and contained/overlapping coplanar panels merged. No
        /// fill/extend/native-resolve is run, so bucket values can be tuned and reviewed in isolation.
        /// </summary>
        /// <param name="panels">Panels to clean. Not modified; new panels are returned.</param>
        /// <param name="diagnostics">Coded diagnostics describing the clean pass.</param>
        /// <param name="weights">Optional per-panel backer weights (aligned with the non-air panel order); null uses the default.</param>
        /// <param name="maxExtends">Optional per-panel lateral extend reach (the Solver's <c>SolverParameter.MaxExtend</c>);
        /// null reads it off each panel, falling back to 0.4 m. Carried through and stamped for <c>SAMAnalytical.Visualize</c>.</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <returns>The clean panels, or null when no usable panels were supplied.</returns>
        public static List<Panel> Clean3D(
            this IEnumerable<Panel> panels,
            out List<string> diagnostics,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3)
        {
            return Clean3D(panels, out diagnostics, out _, weights, maxExtends, minBucketSize, thicknessFactor, alignColinearOffset, normalizeCapOffset);
        }

        /// <summary>
        /// Phase 8 overload: as <see cref="Clean3D(IEnumerable{Panel}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double)"/>,
        /// additionally returning a <see cref="Solve3DReport"/> for Grasshopper inspection of Stage A
        /// (diagnostics, source map, level frames). No native resolve happens on this path, so
        /// <see cref="Solve3DReport.Signature"/>/<see cref="Solve3DReport.Cells"/>/<see cref="Solve3DReport.NakedWires"/>
        /// stay null/empty - honestly reported, not fabricated.
        /// </summary>
        /// <param name="report">The staged Stage A snapshot; never null, even when the clean pass produced no result.</param>
        public static List<Panel> Clean3D(
            this IEnumerable<Panel> panels,
            out List<string> diagnostics,
            out Solve3DReport report,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3)
        {
            diagnostics = new List<string>();
            report = null;

            if (!PrepareInput(panels, minBucketSize, thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources))
            {
                diagnostics.Add("SAM_OCCT_CLEAN3D_INPUT_EMPTY: No valid non-air panel geometry was supplied.");
                report = new Solve3DReport(false, null, null, null, null, sources, null, null, null, null, null, false, 0);
                return null;
            }

            List<double> effectiveWeights = ResolveWeights(weights, sources);
            List<double> effectiveMaxExtends = ResolveMaxExtends(maxExtends, sources);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds, bucketSizes, effectiveWeights, effectiveMaxExtends) { StopAfterClean = true, AlignColinearOffset = alignColinearOffset, NormalizeCapOffset = normalizeCapOffset };
            solver.Execute(null);

            List<Face3D> clean = solver.CleanFace3Ds;
            if (clean == null || clean.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_CLEAN3D_NO_RESULT: The clean bucket produced no panels.");
                report = BuildStageReport(solver, sources);
                return new List<Panel>();
            }

            List<Panel> result = BuildPanels(clean, sources, bucketSizes, effectiveWeights, effectiveMaxExtends, Tolerance.Distance);

            diagnostics.Add(string.Format(
                "SAM_OCCT_CLEAN3D_RESULT: Cleaned {0} panel(s) into {1} clean panel(s).",
                face3Ds.Count,
                result.Count));

            diagnostics.AddRange(SolverReportFormat.FormatDiagnostics(solver.Diagnostics, "SAM_OCCT_CLEAN3D"));

            report = BuildStageReport(solver, sources);

            return result;
        }

        /// <summary>
        /// Step 1 + Step 2 managed fill/extend, WITHOUT the native resolve (the split). Cleans the panels, grows
        /// floors/roofs out to the surrounding walls, and extends walls up to the cap above / down to the floor
        /// below (overshooting their caps). Returns those filled/extended panels so the pre-resolve geometry can
        /// be reviewed before <see cref="Modify.Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool)"/> runs the native MakerVolume split - no cells are formed and
        /// no walls are trimmed here. Output panels carry the bucket/weight/max-extend stamps for
        /// <c>SAMAnalytical.Visualize</c>, so the extend assumptions can be seen (and the per-panel
        /// <c>SolverParameter.MaxExtend</c> adjusted) before solving.
        /// </summary>
        /// <param name="panels">Panels to fill/extend. Not modified; new panels are returned.</param>
        /// <param name="diagnostics">Coded diagnostics describing the pass.</param>
        /// <param name="weights">Optional per-panel backer weights (aligned with the non-air panel order); null uses the default.</param>
        /// <param name="maxExtends">Optional per-panel lateral extend reach (the Solver's <c>SolverParameter.MaxExtend</c>);
        /// null reads <c>SolverParameter.MaxExtend</c> off each panel, falling back to 0.4 m.</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <param name="fillMargin">How far floors/roofs are grown outward past the walls (and walls past their caps), in metres.</param>
        /// <returns>The filled/extended panels, or null when no usable panels were supplied.</returns>
        public static List<Panel> Extend3D(
            this IEnumerable<Panel> panels,
            out List<string> diagnostics,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double fillMargin = 0.5,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3)
        {
            return Extend3D(panels, out diagnostics, out _, weights, maxExtends, minBucketSize, thicknessFactor, fillMargin, alignColinearOffset, normalizeCapOffset);
        }

        /// <summary>
        /// Phase 8 overload: as <see cref="Extend3D(IEnumerable{Panel}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, double)"/>,
        /// additionally returning a <see cref="Solve3DReport"/> for Grasshopper inspection of the pre-resolve
        /// conditioned state (diagnostics, source map, level frames). No native resolve happens on this path,
        /// so <see cref="Solve3DReport.Signature"/>/<see cref="Solve3DReport.Cells"/>/<see cref="Solve3DReport.NakedWires"/>
        /// stay null/empty - honestly reported, not fabricated.
        /// </summary>
        /// <param name="report">The staged pre-resolve snapshot; never null, even when the pass produced no result.</param>
        public static List<Panel> Extend3D(
            this IEnumerable<Panel> panels,
            out List<string> diagnostics,
            out Solve3DReport report,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double fillMargin = 0.5,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3)
        {
            diagnostics = new List<string>();
            report = null;

            if (!PrepareInput(panels, minBucketSize, thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources))
            {
                diagnostics.Add("SAM_OCCT_EXTEND3D_INPUT_EMPTY: No valid non-air panel geometry was supplied.");
                report = new Solve3DReport(false, null, null, null, null, sources, null, null, null, null, null, false, 0);
                return null;
            }

            List<double> effectiveWeights = ResolveWeights(weights, sources);
            List<double> effectiveMaxExtends = ResolveMaxExtends(maxExtends, sources);

            // StopAfterExtend keeps the pass managed (native-free, like Clean3D): clean bucket -> extend walls
            // to the next wall (close the plan loop) -> extend walls to caps -> fill caps to walls, then stop
            // before the native MakerVolume split (Solve3D's job).
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds, bucketSizes, effectiveWeights, effectiveMaxExtends)
            {
                StopAfterExtend = true,
                FillMargin = fillMargin,
                Up = ResolveUp(sources),
                AlignColinearOffset = alignColinearOffset,
                NormalizeCapOffset = normalizeCapOffset
            };
            solver.Execute(null);

            List<Face3D> extended = solver.ResolvedFace3Ds;
            if (extended == null || extended.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_EXTEND3D_NO_RESULT: The fill/extend pass produced no panels.");
                report = BuildStageReport(solver, sources);
                return new List<Panel>();
            }

            List<Panel> result = BuildPanels(extended, sources, bucketSizes, effectiveWeights, effectiveMaxExtends, Tolerance.Distance);

            diagnostics.Add(string.Format(
                "SAM_OCCT_EXTEND3D_RESULT: Filled/extended {0} panel(s) into {1} panel(s) (pre-resolve; the split runs in Solve3D).",
                face3Ds.Count,
                result.Count));

            // Plan-closure check: how many wall ends are still open after the managed extend. Non-zero means
            // the wall loops do not close there, so floors/roofs cannot fill a closed polysurface - raise
            // MaxExtend or bucket size on the OpenPanels3D panels at those corners.
            int openWallEndCount = solver.OpenWallEndPoint3Ds?.Count ?? 0;
            diagnostics.Add(string.Format(
                "SAM_OCCT_EXTEND3D_OPEN_ENDS: {0} wall end(s) still open in plan after extend ({1} wall panel(s) to upgrade).",
                openWallEndCount,
                solver.OpenWallFace3Ds?.Count ?? 0));

            diagnostics.AddRange(SolverReportFormat.FormatDiagnostics(solver.Diagnostics, "SAM_OCCT_EXTEND3D"));

            report = BuildStageReport(solver, sources);

            return result;
        }

        /// <summary>Assembles a <see cref="Solve3DReport"/> from a solver run through a pre-resolve stage
        /// (<c>StopAfterClean</c>/<c>StopAfterExtend</c>) - no native resolve happens, so
        /// <see cref="Panel3DSnapSolver.Signature"/>/<see cref="Panel3DSnapSolver.Cells"/>/<see cref="Panel3DSnapSolver.NakedWires"/>
        /// stay null/empty on the solver itself; this reports that state honestly rather than fabricating it.</summary>
        private static Solve3DReport BuildStageReport(Panel3DSnapSolver solver, List<Panel> sources)
        {
            return new Solve3DReport(
                solver.RawAdopted,
                solver.Signature,
                solver.RawAttemptSignature,
                solver.Diagnostics,
                solver.SourceMap,
                sources,
                solver.Cells,
                null,
                solver.NakedWires,
                solver.CleanFace3Ds,
                solver.LevelFrames,
                solver.NativeResolved,
                solver.ResolvedCellCount);
        }

        /// <summary>
        /// Plan-closure diagnostic. Runs the managed clean + extend (no native resolve), then reports which
        /// walls still do NOT close into a loop in plan: their feet leave an end that no other wall meets.
        /// Until those ends close, the floors/roofs cannot fill a closed polysurface and the native
        /// <c>Create.Shells</c> (MakerVolume) will not form cells. The returned panels are the walls to
        /// upgrade - raise their <c>SolverParameter.MaxExtend</c> or bucket size and re-run - and
        /// <paramref name="openEndPoint3Ds"/> marks the exact open corners (drop them in Rhino to see the gaps).
        /// </summary>
        /// <param name="panels">Panels to diagnose. Not modified; new panels are returned.</param>
        /// <param name="openEndPoint3Ds">Locations of wall-foot ends that no other wall meets in plan.</param>
        /// <param name="diagnostics">Coded diagnostics describing the diagnosis.</param>
        /// <param name="weights">Optional per-panel backer weights; null reads SolverParameter.Weight (default).</param>
        /// <param name="maxExtends">Optional per-panel lateral extend reach; null reads SolverParameter.MaxExtend (default 0.4 m).</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <param name="connectionTolerance">How close another wall must come (in plan) for an end to count as met.</param>
        /// <returns>The open wall panels (carrying their BucketSize/Weight/MaxExtend stamps), or null when no usable panels were supplied.</returns>
        public static List<Panel> OpenPanels3D(
            this IEnumerable<Panel> panels,
            out List<Point3D> openEndPoint3Ds,
            out List<string> diagnostics,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtends = null,
            double minBucketSize = 0.4,
            double thicknessFactor = 0.6,
            double connectionTolerance = 0.1,
            double alignColinearOffset = 0.3,
            double normalizeCapOffset = 0.3)
        {
            openEndPoint3Ds = new List<Point3D>();
            diagnostics = new List<string>();

            if (!PrepareInput(panels, minBucketSize, thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources))
            {
                diagnostics.Add("SAM_OCCT_OPENPANELS3D_INPUT_EMPTY: No valid non-air panel geometry was supplied.");
                return null;
            }

            List<double> effectiveWeights = ResolveWeights(weights, sources);
            List<double> effectiveMaxExtends = ResolveMaxExtends(maxExtends, sources);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds, bucketSizes, effectiveWeights, effectiveMaxExtends)
            {
                StopAfterExtend = true,
                ConnectionTolerance = connectionTolerance,
                Up = ResolveUp(sources),
                AlignColinearOffset = alignColinearOffset,
                NormalizeCapOffset = normalizeCapOffset
            };
            solver.Execute(null);

            openEndPoint3Ds = solver.OpenWallEndPoint3Ds ?? new List<Point3D>();

            List<Face3D> openWallFace3Ds = solver.OpenWallFace3Ds ?? new List<Face3D>();
            List<Panel> result = BuildPanels(openWallFace3Ds, sources, bucketSizes, effectiveWeights, effectiveMaxExtends, Tolerance.Distance);

            diagnostics.Add(string.Format(
                "SAM_OCCT_OPENPANELS3D_RESULT: {0} wall(s) still open in plan ({1} open end(s)); raise MaxExtend or bucket size on these and re-run.",
                result.Count,
                openEndPoint3Ds.Count));

            return result;
        }

        /// <summary>
        /// The level "up" axis for the Step-2 extend, derived from the cap panels' plane normals: floors
        /// define the level/slab plane, so their shared normal is the level normal. For an ordinary upright
        /// model this is world Z and no rotation happens; for a whole-level-tilted model it is the tilt
        /// direction, so the extend runs in the level's own frame. Floors are voted first (and roofs only
        /// when there are no floors) so an ordinary building with a pitched roof over flat floors keeps
        /// <c>up = Z</c> - only a genuinely tilted slab moves it. Falls back to world Z when there are no caps.
        /// </summary>
        private static Vector3D ResolveUp(List<Panel> sources)
        {
            Vector3D up = AverageNormal(sources, global::SAM.Analytical.PanelGroup.Floor);
            if (up == null)
            {
                up = AverageNormal(sources, global::SAM.Analytical.PanelGroup.Roof);
            }

            return up ?? new Vector3D(0, 0, 1);
        }

        /// <summary>
        /// Mean unit plane-normal of the panels in <paramref name="panelGroup"/>, collapsed to one
        /// hemisphere so opposing caps (a floor below, the roof above) reinforce rather than cancel.
        /// Null when the group is empty or its normals cancel out.
        /// </summary>
        private static Vector3D AverageNormal(List<Panel> sources, global::SAM.Analytical.PanelGroup panelGroup)
        {
            double x = 0, y = 0, z = 0;
            int count = 0;
            foreach (Panel panel in sources ?? new List<Panel>())
            {
                if (panel == null || global::SAM.Analytical.Query.PanelGroup(panel.PanelType) != panelGroup)
                {
                    continue;
                }

                Vector3D normal = panel.GetFace3D()?.GetPlane()?.Normal?.Unit;
                if (normal == null)
                {
                    continue;
                }

                if (normal.Z < 0)
                {
                    normal = normal.GetNegated();
                }

                x += normal.X;
                y += normal.Y;
                z += normal.Z;
                count++;
            }

            Vector3D result = new Vector3D(x, y, z);
            return count != 0 && result.Length > Tolerance.Distance ? result.Unit : null;
        }

        /// <summary>Drops air panels and collects valid Face3Ds + thickness-derived bucket sizes + source panels.</summary>
        private static bool PrepareInput(IEnumerable<Panel> panels, double minBucketSize, double thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources)
        {
            face3Ds = new List<Face3D>();
            bucketSizes = new List<double>();
            sources = new List<Panel>();

            // Air panels carry no real surface to snap to; drop them up front (Query.Air analogue).
            List<Panel> panels_Temp = panels?.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            if (panels_Temp == null || panels_Temp.Count == 0)
            {
                return false;
            }

            foreach (Panel panel in panels_Temp)
            {
                Face3D face3D = panel.GetFace3D();
                if (face3D == null || !face3D.IsValid())
                {
                    continue;
                }

                face3Ds.Add(face3D);
                bucketSizes.Add(BucketSize(panel, minBucketSize, thicknessFactor));
                sources.Add(panel);
            }

            return face3Ds.Count != 0;
        }

        /// <summary>
        /// Rebuilds Panels from solved/clean faces, carrying construction/type from the nearest source and
        /// stamping the backer <c>Weight</c>, capture-slab <c>BucketSize</c> and lateral <c>MaxExtend</c> that
        /// source used, so the output feeds <c>SAMAnalytical.Visualize</c> (which reads those
        /// <see cref="SolverParameter"/>s) - letting the extend assumptions be seen and adjusted.
        /// </summary>
        private static List<Panel> BuildPanels(List<Face3D> face3Ds, List<Panel> sources, List<double> bucketSizes, List<double> weights, List<double> maxExtends, double tolerance, SAM.Geometry.OCCT.Solver.SourceMap sourceMap = null)
        {
            List<Panel> result = new List<Panel>();
            for (int faceIndex = 0; faceIndex < face3Ds.Count; faceIndex++)
            {
                Face3D face3D = face3Ds[faceIndex];

                // Phase 3: the exact native-history source for this output face, when available;
                // otherwise the geometric NearestSourceIndex heuristic (the demoted fallback).
                int index = DominantSourceIndex(sourceMap, faceIndex, sources);
                if (index < 0)
                {
                    index = NearestSourceIndex(face3D, sources, tolerance);
                }

                if (index < 0)
                {
                    continue;
                }

                Panel source = sources[index];
                Panel panel = global::SAM.Analytical.Create.Panel(source.Construction, source.PanelType, face3D);
                if (panel == null)
                {
                    continue;
                }

                // Stamp the bucket size + backer weight + max-extend used for this panel so the capture slab
                // and extend reach can be drawn directly from the Clean3D/Extend3D/Solve3D output (the bands
                // SAMAnalytical.Visualize renders in the middle of the panel).
                if (bucketSizes != null && index < bucketSizes.Count)
                {
                    panel.SetValue(SolverParameter.BucketSize, bucketSizes[index]);
                }

                if (weights != null && index < weights.Count)
                {
                    panel.SetValue(SolverParameter.Weight, weights[index]);
                }

                if (maxExtends != null && index < maxExtends.Count)
                {
                    panel.SetValue(SolverParameter.MaxExtend, maxExtends[index]);
                }

                result.Add(panel);
            }

            return result;
        }

        /// <summary>
        /// Resolves the per-panel backer weights: the caller's list when supplied, otherwise the canonical
        /// length-based weights (longer/larger panels dominate as backers) derived via
        /// <see cref="SAM.Analytical.Solver.Modify.SetWeights{T}(List{T}, bool, double)"/>. The metric runs
        /// on throwaway clones so the caller's panels are not mutated.
        /// </summary>
        private static List<double> ResolveWeights(IEnumerable<double> weights, List<Panel> sources)
        {
            List<double> supplied = weights?.ToList();
            if (supplied != null && supplied.Count != 0)
            {
                return supplied;
            }

            // Keep clones index-aligned 1:1 with sources so the weights line up with bucketSizes/sources.
            List<Panel> clones = sources.Select(x => global::SAM.Analytical.Create.Panel(x)).ToList();
            clones.SetWeights();

            List<double> result = new List<double>(clones.Count);
            foreach (Panel clone in clones)
            {
                result.Add(clone != null && clone.TryGetValue(SolverParameter.Weight, out double weight) && !double.IsNaN(weight)
                    ? weight
                    : Panel3DSnapSolver.DEFAULT_Weight);
            }

            return result;
        }

        /// <summary>
        /// Resolves the per-panel lateral extend reach (<c>SolverParameter.MaxExtend</c>): the caller's list
        /// when supplied, otherwise the value already stamped on each panel by the Solver's
        /// <c>SetMaxExtends</c> workflow, falling back to <see cref="Panel3DSnapSolver.DEFAULT_MaxExtension"/>
        /// (0.4 m) when a panel carries none. Reads the panels read-only; nothing is mutated. Using the same
        /// <see cref="SolverParameter.MaxExtend"/> the 2D Solver uses lets the reach be tuned (and seen via
        /// <c>SAMAnalytical.Visualize</c>) exactly as in the Solver.
        /// </summary>
        private static List<double> ResolveMaxExtends(IEnumerable<double> maxExtends, List<Panel> sources)
        {
            List<double> supplied = maxExtends?.ToList();
            if (supplied != null && supplied.Count != 0)
            {
                return supplied;
            }

            List<double> result = new List<double>(sources.Count);
            foreach (Panel source in sources)
            {
                result.Add(source != null && source.TryGetValue(SolverParameter.MaxExtend, out double maxExtend) && !double.IsNaN(maxExtend) && maxExtend > 0
                    ? maxExtend
                    : Panel3DSnapSolver.DEFAULT_MaxExtension);
            }

            return result;
        }

        /// <summary>
        /// Capture half-width for a panel. Prefers the <c>SolverParameter.BucketSize</c> already stamped on
        /// the panel (the same one <c>SAMAnalytical.Visualize</c> shows and the user tunes, mirroring weight
        /// and max-extend), so a hand-set bucket is honoured. Otherwise derives it from construction
        /// thickness: thickness * factor, floored at a minimum.
        /// </summary>
        private static double BucketSize(Panel panel, double minBucketSize, double thicknessFactor)
        {
            if (panel != null && panel.TryGetValue(SolverParameter.BucketSize, out double bucketSize) && !double.IsNaN(bucketSize) && bucketSize > 0)
            {
                return bucketSize;
            }

            double thickness = panel?.Construction?.GetThickness() ?? double.NaN;
            if (double.IsNaN(thickness) || thickness <= 0)
            {
                return minBucketSize;
            }

            return System.Math.Max(minBucketSize, thickness * thicknessFactor);
        }

        /// <summary>
        /// The dominant source panel for the resolved output face at <paramref name="faceIndex"/> per the
        /// exact native-history <paramref name="sourceMap"/> (Phase 3): the recorded source whose panel has
        /// the largest face area (the backer that most explains a merged/split output). Returns -1 when the
        /// map is null, has no source for this face, or the sources are fabricated only - the caller then
        /// falls back to the geometric <see cref="NearestSourceIndex"/> heuristic.
        /// </summary>
        private static int DominantSourceIndex(SAM.Geometry.OCCT.Solver.SourceMap sourceMap, int faceIndex, List<Panel> sources)
        {
            if (sourceMap == null || sources == null || sources.Count == 0)
            {
                return -1;
            }

            int best = -1;
            double bestArea = double.NegativeInfinity;
            foreach (int source in sourceMap.SourcesOf(new SAM.Geometry.OCCT.Solver.FaceKey(faceIndex)))
            {
                if (source < 0 || source >= sources.Count)
                {
                    continue; // fabricated (-1) or out-of-range source: no panel to carry forward.
                }

                double area = sources[source]?.GetFace3D()?.GetArea() ?? 0;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = source;
                }
            }

            return best;
        }

        /// <summary>
        /// Index of the source panel that best explains a resolved face: parallel supporting planes,
        /// then the source whose centroid sits inside the resolved face's bounding box. Used to carry
        /// construction/type (and the bucket/weight stamps) forward, since native boolean resolution
        /// loses the 1:1 source mapping. Returns -1 only when there are no sources.
        /// </summary>
        /// <summary>Visible to <see cref="PanelReconstruction"/> as the last-resort fallback when a
        /// face has no <see cref="SAM.Geometry.OCCT.Solver.SourceMap"/> attribution at all.</summary>
        internal static int NearestSourceIndex(Face3D face3D, List<Panel> sources, double tolerance)
        {
            if (sources == null || sources.Count == 0)
            {
                return -1;
            }

            Plane plane = face3D?.GetPlane();
            if (plane == null)
            {
                return 0;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < sources.Count; i++)
            {
                Face3D sourceFace3D = sources[i]?.GetFace3D();
                Plane sourcePlane = sourceFace3D?.GetPlane();
                if (sourcePlane == null)
                {
                    continue;
                }

                if (System.Math.Abs(plane.Normal.Unit.DotProduct(sourcePlane.Normal.Unit)) < 0.99)
                {
                    continue;
                }

                Point3D centroid = sourceFace3D.GetBoundingBox()?.GetCentroid();
                if (centroid == null)
                {
                    continue;
                }

                double distance = plane.Distance(centroid);
                if (boundingBox3D != null && Within(boundingBox3D, centroid, tolerance))
                {
                    distance -= 1.0; // prefer a source whose centroid actually lands on the resolved face
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 ? best : 0;
        }

        private static bool Within(BoundingBox3D boundingBox3D, Point3D point3D, double tolerance)
        {
            Point3D min = boundingBox3D.Min;
            Point3D max = boundingBox3D.Max;
            return point3D.X >= min.X - tolerance && point3D.X <= max.X + tolerance
                && point3D.Y >= min.Y - tolerance && point3D.Y <= max.Y + tolerance
                && point3D.Z >= min.Z - tolerance && point3D.Z <= max.Z + tolerance;
        }
    }
}
