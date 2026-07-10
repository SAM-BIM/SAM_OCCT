// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    public static partial class Modify
    {
        /// <summary>
        /// Analytical entry point for diagnosis-driven closure (Phase 5e,
        /// docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E). Wraps <see cref="AutoTune3DSolver"/> the
        /// same way <see cref="Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool)"/>
        /// wraps <c>Panel3DSnapSolver</c>: skips air panels, derives the capture width from construction
        /// thickness, runs a baseline solve and - only when naked edges remain - a bounded escalation of the
        /// implicated walls' <c>MaxExtend</c> reach, then reconstructs panels through
        /// <see cref="PanelReconstruction"/> (Guid/parameters/construction/apertures preserved). When the
        /// baseline is already watertight this returns exactly what <see cref="Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool)"/>
        /// would - AutoTune only engages on a gappy solve.
        /// </summary>
        /// <param name="panels">Panels to solve. Not modified; new panels are returned. Air panels pass through unchanged.</param>
        /// <param name="nakedPoint3Ds">Residual naked (free) boundary edge locations after tuning.</param>
        /// <param name="diagnostics">Coded diagnostics describing the solve (escalation log, rejections, residual loops).</param>
        /// <param name="weights">Optional per-panel backer weights (aligned with the non-air panel order); null uses the default.</param>
        /// <param name="maxExtends">Optional per-panel baseline lateral extend reach; null reads <c>SolverParameter.MaxExtend</c> off each panel (fallback 0.4 m). AutoTune raises these along its ladder only on the implicated panels.</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <param name="options">OCCT build options (distance/fuzzy/glue tolerances).</param>
        /// <param name="tune">AutoTune knobs (ladder, max rounds, bucket escalation); null uses the defaults.</param>
        /// <returns>The resolved panels, or null when no usable panels were supplied.</returns>
        public static List<Panel> AutoTune3D(
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
            AutoTune3DOptions tune = null,
            double bucketBetweenLevels = 0.0,
            double fillMargin = 0.5,
            bool directionalCapGrow = true)
        {
            return AutoTune3D(panels, out nakedPoint3Ds, out diagnostics, out _, weights, maxExtends, minBucketSize, thicknessFactor, alignColinearOffset, normalizeCapOffset, options, tune, bucketBetweenLevels: bucketBetweenLevels, fillMargin: fillMargin, directionalCapGrow: directionalCapGrow);
        }

        /// <summary>
        /// As <see cref="AutoTune3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, AutoTune3DOptions)"/>,
        /// additionally reporting apertures that could not be re-hosted on any resolved panel (the Phase 4
        /// orphan policy). The split/merge Guid policy and provenance stamping are the same as
        /// <see cref="Solve3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, out List{OrphanedAperture}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, bool, double, double)"/>.
        /// </summary>
        /// <param name="orphanedApertures">Apertures whose source contributed to the solve but landed on no resolved panel; never silently dropped.</param>
        /// <param name="minApertureArea">Minimum aperture area to re-host onto a resolved panel (the Panel ctor's own gate).</param>
        /// <param name="maxApertureDistance">Max distance between a resolved panel and an aperture for it to be re-hosted there.</param>
        public static List<Panel> AutoTune3D(
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
            AutoTune3DOptions tune = null,
            double minApertureArea = Tolerance.MacroDistance,
            double maxApertureDistance = Tolerance.MacroDistance,
            double bucketBetweenLevels = 0.0,
            double fillMargin = 0.5,
            bool directionalCapGrow = true)
        {
            return AutoTune3D(panels, out nakedPoint3Ds, out diagnostics, out orphanedApertures, out _, weights, maxExtends, minBucketSize, thicknessFactor, alignColinearOffset, normalizeCapOffset, options, tune, minApertureArea, maxApertureDistance, bucketBetweenLevels, fillMargin, directionalCapGrow);
        }

        /// <summary>
        /// Phase 8 overload: as <see cref="AutoTune3D(IEnumerable{Panel}, out List{Point3D}, out List{string}, out List{OrphanedAperture}, IEnumerable{double}, IEnumerable{double}, double, double, double, double, OcctBuildOptions, AutoTune3DOptions, double, double)"/>,
        /// additionally returning a <see cref="Solve3DReport"/> for Grasshopper inspection (closure signature,
        /// diagnostics, source map, naked wires, escalation round counts). <see cref="AutoTune3DSolver"/>'s
        /// public surface does not track per-cell metadata, level frames, or whether its internal baseline
        /// attempt was raw-adopted (Phase 5e scope), so <see cref="Solve3DReport.Cells"/>/<see cref="Solve3DReport.LevelFrames"/>/
        /// <see cref="Solve3DReport.CleanFace3Ds"/> stay empty and <see cref="Solve3DReport.RawAdopted"/> stays
        /// false here - honestly reported, not fabricated, and not a reason to extend AutoTune3DSolver's own
        /// core surface for this UI-only phase.
        /// </summary>
        /// <param name="report">The staged solve snapshot; never null, even when the solve produced no result.</param>
        public static List<Panel> AutoTune3D(
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
            AutoTune3DOptions tune = null,
            double minApertureArea = Tolerance.MacroDistance,
            double maxApertureDistance = Tolerance.MacroDistance,
            double bucketBetweenLevels = 0.0,
            double fillMargin = 0.5,
            bool directionalCapGrow = true)
        {
            nakedPoint3Ds = new List<Point3D>();
            diagnostics = new List<string>();
            orphanedApertures = new List<OrphanedAperture>();
            report = null;

            // Air panels are excluded from solving and passed through unchanged - the SAME PrepareInput
            // Solve3D uses, so AutoTune3D handles air panels identically.
            if (!PrepareInput(panels, minBucketSize, thicknessFactor, out List<Face3D> face3Ds, out List<double> bucketSizes, out List<Panel> sources))
            {
                diagnostics.Add("SAM_OCCT_AUTOTUNE3D_INPUT_EMPTY: No valid non-air panel geometry was supplied.");
                report = new Solve3DReport(false, null, null, null, null, sources, null, null, null, null, null, false, 0);
                return null;
            }

            List<double> effectiveWeights = ResolveWeights(weights, sources);
            List<double> effectiveMaxExtends = ResolveMaxExtends(maxExtends, sources);

            AutoTune3DSolver solver = new AutoTune3DSolver(face3Ds, bucketSizes, effectiveWeights, effectiveMaxExtends)
            {
                Up = ResolveUp(sources),
                AlignColinearOffset = alignColinearOffset,
                NormalizeCapOffset = normalizeCapOffset,
                BucketBetweenLevels = bucketBetweenLevels,
                FillMargin = fillMargin,
                DirectionalCapGrow = directionalCapGrow
            };
            solver.Execute(options, tune);

            nakedPoint3Ds = solver.NakedEdgePoint3Ds ?? new List<Point3D>();

            List<Face3D> resolved = solver.ResolvedFace3Ds;
            if (resolved == null || resolved.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_AUTOTUNE3D_NO_RESULT: The solver produced no resolved faces.");
                report = BuildReport(solver, sources);
                return new List<Panel>();
            }

            double tolerance = options?.Tolerance ?? Tolerance.Distance;

            // Reconstruct with the FINAL (possibly escalated) per-source reach/bucket so the stamped
            // SolverParameter values reflect the reach that actually produced the adopted geometry. Both lists
            // stay index-aligned to `sources` (AutoTune only raises entries in place, never reorders). Weights
            // are not escalated, so effectiveWeights is used directly.
            List<double> stampBucketSizes = solver.BucketSizes == null ? bucketSizes : new List<double>(solver.BucketSizes);
            List<double> stampMaxExtends = solver.MaxExtensions == null ? effectiveMaxExtends : new List<double>(solver.MaxExtensions);

            // Phase 4 reconstruction: 1:1 keeps the source Guid, split gets fresh Guids stamped back to the
            // source, merge keeps the dominant source's Guid. solver.SourceMap is always populated.
            List<Panel> result = PanelReconstruction.Build(resolved, sources, solver.SourceMap, stampBucketSizes, effectiveWeights, stampMaxExtends, tolerance, out orphanedApertures, minApertureArea, maxApertureDistance);

            foreach (OrphanedAperture orphan in orphanedApertures)
            {
                diagnostics.Add(string.Format(
                    "SAM_OCCT_AUTOTUNE3D_APERTURE_ORPHANED: Aperture {0} from source panel {1} did not land within {2} m of any resolved panel; returned for manual re-hosting.",
                    orphan.Aperture?.Guid,
                    orphan.SourceGuid,
                    maxApertureDistance));
            }

            // Gap-fill faces (residual naked-boundary loops closed by patches) become air panels, exactly as in
            // Solve3D - a virtual boundary, provenance-stamped so it is distinguishable from a real opening.
            const double minAirArea = 1e-4;
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
                    airPanel.SetValue(PanelProvenanceParameter.Provenance, "GapFill");
                    result.Add(airPanel);
                    airCount++;
                }
            }

            diagnostics.Add(string.Format(
                "SAM_OCCT_AUTOTUNE3D_AIR: Created {0} air panel(s) from closed gaps.", airCount));

            diagnostics.Add(string.Format(
                "SAM_OCCT_AUTOTUNE3D_RESULT: Solved {0} panel(s) into {1} resolved panel(s); nativeResolved={2}; {3} cell(s); {4} naked edge(s); {5} round(s) attempted, {6} accepted.",
                face3Ds.Count,
                result.Count,
                solver.NativeResolved,
                solver.ResolvedCellCount,
                nakedPoint3Ds.Count,
                solver.Rounds,
                solver.RoundsAccepted));

            // Surface the structured solver diagnostics (escalation log, rejections, residual naked loops, and
            // the adopted solve's own events) alongside the SAM_OCCT_* summary lines.
            diagnostics.AddRange(SolverReportFormat.FormatDiagnostics(solver.Diagnostics, "SAM_OCCT_AUTOTUNE3D"));

            report = BuildReport(solver, sources);

            return result;
        }

        /// <summary>Assembles a <see cref="Solve3DReport"/> from an <see cref="AutoTune3DSolver"/> that has
        /// already run <c>Execute</c>. <see cref="AutoTune3DSolver"/> does not track per-cell metadata, level
        /// frames, Stage A clean faces, or its internal baseline's raw-adopted state (Phase 5e scope), so
        /// those fields stay empty/false rather than being fabricated.</summary>
        private static Solve3DReport BuildReport(AutoTune3DSolver solver, List<Panel> sources)
        {
            return new Solve3DReport(
                false,
                solver.Signature,
                null,
                solver.Diagnostics,
                solver.SourceMap,
                sources,
                null,
                null,
                solver.NakedWires,
                null,
                null,
                solver.NativeResolved,
                solver.ResolvedCellCount,
                solver.Rounds,
                solver.RoundsAccepted);
        }
    }
}
