// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Validates a set of shells against the OCCT kernel (issue #37 follow-on):
        /// runs BRepCheck_Analyzer + ShapeAnalysis_FreeBounds (+ an optional
        /// self-intersection test) and returns whether the geometry is valid and
        /// watertight, with the located, categorised issues. This is the native
        /// safety net behind <see cref="OcctBuildOptions.ValidateInput"/> and the
        /// gate the BOP-glue build path relies on.
        /// </summary>
        /// <param name="shells">The shells to validate.</param>
        /// <param name="report">The validation report, or null on failure.</param>
        /// <param name="result">OCCT diagnostics for the validation.</param>
        /// <param name="options">OCCT build options; <see cref="OcctBuildOptions.Tolerance"/> sets the fuzzy value for the analysers.</param>
        /// <param name="checkSelfIntersections">When true (default) the costly BOPAlgo_ArgumentAnalyzer self-intersection test runs as well.</param>
        /// <returns>True when a report was produced (inspect it for validity); false when the native call failed.</returns>
        public static bool Validate(IEnumerable<Shell> shells, out OcctValidationReport report, out OcctCellComplexResult result, OcctBuildOptions options = null, bool checkSelfIntersections = true)
        {
            report = null;
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_VALIDATE_INPUT_EMPTY", "No shells were supplied.");
                return false;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryValidateShells(shells_Temp, checkSelfIntersections, options, result, out report))
            {
                return false;
            }

            ReportValidation(report, result);
            return true;
        }

        /// <summary>
        /// Face-soup overload of <see cref="Validate(IEnumerable{Shell}, out OcctValidationReport, out OcctCellComplexResult, OcctBuildOptions, bool)"/>.
        /// The faces need not bound a volume - they are sewn into a shell first so
        /// the analysers can locate the naked edges / self-intersections that keep
        /// them from closing.
        /// </summary>
        public static bool Validate(IEnumerable<Face3D> face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, OcctBuildOptions options = null, bool checkSelfIntersections = true)
        {
            report = null;
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_VALIDATE_INPUT_EMPTY", "No Face3D geometry was supplied.");
                return false;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryValidateFaces(face3Ds_Temp, checkSelfIntersections, options, result, out report))
            {
                return false;
            }

            ReportValidation(report, result);
            return true;
        }

        /// <summary>
        /// Topology-handle overload of <see cref="Validate(IEnumerable{Shell}, out OcctValidationReport, out OcctCellComplexResult, OcctBuildOptions, bool)"/>:
        /// validates a live OCCT topology (e.g. an imported STEP shape) without
        /// rebuilding it. The handle stays valid and caller-owned.
        /// </summary>
        public static bool Validate(OcctTopology topology, out OcctValidationReport report, out OcctCellComplexResult result, OcctBuildOptions options = null, bool checkSelfIntersections = true)
        {
            report = null;
            result = new OcctCellComplexResult();

            if (topology == null || topology.IsInvalid || topology.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_VALIDATE_INPUT_EMPTY", "No OCCT topology was supplied.");
                return false;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryValidate(topology, checkSelfIntersections, options, result, out report))
            {
                return false;
            }

            ReportValidation(report, result);
            return true;
        }

        /// <summary>
        /// Adds a Success diagnostic summarising the report and, when the shape is
        /// invalid / not watertight, a Warning that names the located issues so an
        /// open or self-intersecting shape self-reports the problem. Marks the
        /// (cell-less) operation successful.
        /// </summary>
        private static void ReportValidation(OcctValidationReport report, OcctCellComplexResult result)
        {
            if (report == null)
            {
                return;
            }

            result.OperationSucceeded = true;
            result.AddDiagnostic(
                OcctDiagnosticSeverity.Info,
                "SAM_OCCT_VALIDATE_SUCCESS",
                string.Format("Validation complete: {0}, {1}, {2} issue(s).", report.IsValid ? "valid" : "INVALID", report.IsWatertight ? "watertight" : "NOT watertight", report.Issues.Count));

            if (!report.IsWatertight)
            {
                int nakedCount = report.CountOf(OcctValidationIssueCategory.NakedEdge);
                OcctValidationIssue sample = report.IssuesOf(OcctValidationIssueCategory.NakedEdge).FirstOrDefault();
                Point3D location = sample?.Location;
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Warning,
                    "SAM_OCCT_VALIDATE_NOT_WATERTIGHT",
                    string.Format(
                        "The shape is NOT watertight: {0} naked (free) edge(s) found{1}. This is why OCCT cannot build a closed volume.",
                        nakedCount,
                        location == null ? string.Empty : string.Format(", e.g. near ({0:0.###}, {1:0.###}, {2:0.###})", location.X, location.Y, location.Z)));
            }

            if (!report.IsValid)
            {
                int selfIntersections = report.CountOf(OcctValidationIssueCategory.SelfIntersection);
                int invalidFaces = report.CountOf(OcctValidationIssueCategory.InvalidFace);
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Warning,
                    "SAM_OCCT_VALIDATE_INVALID",
                    string.Format("The shape is topologically invalid: {0} self-intersection(s), {1} invalid face(s).", selfIntersections, invalidFaces));
            }
        }
    }
}
