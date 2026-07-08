// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SAM.Geometry.OCCT.Native
{
    internal static class OcctCellComplexBuilder
    {
        public static bool TryIntersection(IEnumerable<Shell> shells, IEnumerable<Shell> toolShells, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(shells, options, result, "SAM_OCCT_INTERSECTION_TARGET_EMPTY", "No serializable target shell geometry was found.", out OcctNativeInput targetInput))
            {
                return false;
            }

            if (!OcctNativeInputBuilder.TryBuild(toolShells, options, result, "SAM_OCCT_INTERSECTION_TOOL_EMPTY", "No serializable tool shell geometry was found.", out OcctNativeInput toolInput))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_shells_intersection(
                    targetInput.Coordinates,
                    targetInput.Coordinates.Length / 3,
                    targetInput.LoopPointCounts,
                    targetInput.LoopPointCounts.Length,
                    targetInput.FaceLoopCounts,
                    targetInput.FaceCount,
                    targetInput.ShellFaceCounts,
                    targetInput.ShellCount,
                    toolInput.Coordinates,
                    toolInput.Coordinates.Length / 3,
                    toolInput.LoopPointCounts,
                    toolInput.LoopPointCounts.Length,
                    toolInput.FaceLoopCounts,
                    toolInput.FaceCount,
                    toolInput.ShellFaceCounts,
                    toolInput.ShellCount,
                    options.Tolerance,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INTERSECTION_NATIVE_FAILED", string.Format("Native OCCT intersection returned status {0}.", status));
                    return false;
                }

                return DecodeResult(resultHandle, result);
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryUnion(IEnumerable<Shell> shells, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(shells, options, result, "SAM_OCCT_UNION_INPUT_EMPTY", "No serializable shell geometry was found.", out OcctNativeInput input))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_shells_union(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    input.ShellFaceCounts,
                    input.ShellCount,
                    options.Tolerance,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_UNION_NATIVE_FAILED", string.Format("Native OCCT union returned status {0}.", status));
                    return false;
                }

                return DecodeResult(resultHandle, result);
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryRepair(IEnumerable<Shell> shells, OcctBuildOptions options, double minArea, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(shells, options, result, "SAM_OCCT_REPAIR_INPUT_EMPTY", "No serializable shell geometry was found.", out OcctNativeInput input))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_shells_repair(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    input.ShellFaceCounts,
                    input.ShellCount,
                    options.Tolerance,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    minArea,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_REPAIR_NATIVE_FAILED", string.Format("Native OCCT repair returned status {0}.", status));
                    return false;
                }

                return DecodeResult(resultHandle, result);
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryDifference(IEnumerable<Shell> shells, IEnumerable<Shell> cutterShells, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(shells, options, result, "SAM_OCCT_DIFFERENCE_TARGET_EMPTY", "No serializable target shell geometry was found.", out OcctNativeInput targetInput))
            {
                return false;
            }

            if (!OcctNativeInputBuilder.TryBuild(cutterShells, options, result, "SAM_OCCT_DIFFERENCE_CUTTER_EMPTY", "No serializable cutter shell geometry was found.", out OcctNativeInput cutterInput))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_shells_difference(
                    targetInput.Coordinates,
                    targetInput.Coordinates.Length / 3,
                    targetInput.LoopPointCounts,
                    targetInput.LoopPointCounts.Length,
                    targetInput.FaceLoopCounts,
                    targetInput.FaceCount,
                    targetInput.ShellFaceCounts,
                    targetInput.ShellCount,
                    cutterInput.Coordinates,
                    cutterInput.Coordinates.Length / 3,
                    cutterInput.LoopPointCounts,
                    cutterInput.LoopPointCounts.Length,
                    cutterInput.FaceLoopCounts,
                    cutterInput.FaceCount,
                    cutterInput.ShellFaceCounts,
                    cutterInput.ShellCount,
                    options.Tolerance,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DIFFERENCE_NATIVE_FAILED", string.Format("Native OCCT difference returned status {0}.", status));
                    return false;
                }

                return DecodeResult(resultHandle, result);
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryBuild(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            // Materialise once so the watertightness fallback can re-read the
            // same faces after a native failure without re-enumerating a lazy source.
            List<Face3D> face3DList = face3Ds?.ToList();

            if (!OcctNativeInputBuilder.TryBuild(face3DList, options, result, out OcctNativeInput input))
            {
                return false;
            }

            // issue #37 follow-on: BOP glue path. Glue is a throughput win on cell
            // complexes with many coincident shared walls but corrupts merely-near-
            // coincident faces, so it runs only when a watertightness pre-check
            // reports no gaps (naked edges); otherwise it degrades to the glue-off
            // path below (and likewise when the native build predates the glue ABI).
            if (options.GlueMode != OcctGlueMode.Off && TryGluedBuild(face3DList, options, result))
            {
                return true;
            }

            // issue #37: optionally heal the face soup into a watertight shell
            // BEFORE MakerVolume, so triangulated / near-touching faces close
            // first instead of relying on MakerVolume's fuzzy-tolerance guesswork.
            if (options.SewBeforeBuild && TrySewThenMakeVolume(face3DList, options, result))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", "Built the cell complex via native sew-and-heal before MakerVolume (SewBeforeBuild).");
                return true;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_build_cell_complex(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    options.Tolerance,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    options.AvoidInternalShapes ? 1 : 0,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    // issue #37: on a hard close failure (MakerVolume reported
                    // errors or produced no closed solid) attempt one sew-then-
                    // rebuild before giving up - this recovers the triangulated /
                    // near-touching face soups that defeat a direct MakerVolume.
                    // Skipped when SewBeforeBuild already tried (and failed) above.
                    if ((status == 30 || status == 40) && !options.SewBeforeBuild && TrySewThenMakeVolume(face3DList, options, result))
                    {
                        result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", string.Format("MakerVolume returned status {0} ({1}); recovered via native sew-and-heal retry.", status, OcctOpenShellAnalysis.DescribeBuildStatus(status)));
                        return true;
                    }

                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_FAILED", string.Format("Native OCCT builder returned status {0} ({1}).", status, OcctOpenShellAnalysis.DescribeBuildStatus(status)));

                    // The faces did not bound a volume; report where the shell is
                    // open so the failure is actionable instead of an opaque code.
                    ReportInputDiagnostics(face3DList, options, result);
                    return false;
                }

                bool decoded = DecodeResult(resultHandle, result);

                // ABI v4 (observational): snapshot the MakerVolume BRepTools_History
                // while the handle is alive, so the solver can compose an exact
                // source->output map instead of the geometric NearestSourceIndex
                // heuristic. Null on a pre-v4 native or when history is unavailable.
                if (decoded)
                {
                    result.History = OcctHistory.Capture(resultHandle);
                }

                return decoded;
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// The native sew-then-MakerVolume recovery (issue #37): heal the face
        /// soup into a (possibly still open) shell with BRepBuilderAPI_Sewing +
        /// ShapeFix, then close it with MakerVolume. Runs in an isolated result so
        /// a failed attempt leaves no Error diagnostics on the caller's result
        /// (which would otherwise make a later success read as a failure); only a
        /// fully successful recovery is merged back into <paramref name="result"/>.
        /// </summary>
        private static bool TrySewThenMakeVolume(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            OcctCellComplexResult sewResult = new OcctCellComplexResult();

            OcctTopology sewn = null;
            OcctTopology volume = null;
            try
            {
                if (!OcctShapeBuilder.TrySewFacesToTopology(face3Ds, false, options, sewResult, out sewn)
                    || !OcctShapeBuilder.TryMakeVolume(sewn, options, sewResult, out volume)
                    || !OcctShapeBuilder.TryDecode(volume, options, sewResult))
                {
                    // Propagate only the native availability so the caller's
                    // graceful-degradation reporting stays accurate.
                    result.NativeAvailable = sewResult.NativeAvailable;
                    result.NativeVersion = sewResult.NativeVersion;
                    return false;
                }

                result.NativeAvailable = true;
                result.NativeVersion = sewResult.NativeVersion;
                foreach (OcctCell cell in sewResult.Cells)
                {
                    result.AddCell(cell);
                }

                if (options.RetainTopology)
                {
                    result.Topology = volume;
                    volume = null; // ownership transferred to the caller's result
                }

                result.BuildFaceAdjacencies();
                return true;
            }
            finally
            {
                sewn?.Dispose();
                volume?.Dispose();
                sewResult.Dispose();
            }
        }

        /// <summary>
        /// The glue gate (issue #37 follow-on): glue is safe only on gap-free
        /// input (near-coincident faces beyond tolerance show up as gaps). Prefers
        /// the native validation report - the safety net this feature is built on -
        /// and falls back to the pure-managed naked-edge check when the native
        /// validator is unavailable. Non-manifold (shared-wall) edges, which are
        /// exactly what glue accelerates, never block it. Adds a Warning (not an
        /// Error, so the glue-off success still reads as success) when it skips.
        /// </summary>
        private static bool IsInputCleanForGlue(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            // Native gate: sew the soup and run the kernel watertightness check.
            OcctCellComplexResult validateResult = new OcctCellComplexResult();
            try
            {
                if (OcctShapeBuilder.TryValidateFaces(face3Ds, true, options, validateResult, out OcctValidationReport report) && report != null)
                {
                    if (report.IsWatertight)
                    {
                        return true;
                    }

                    result.AddDiagnostic(
                        OcctDiagnosticSeverity.Warning,
                        "SAM_OCCT_GLUE_SKIPPED",
                        string.Format("BOP glue ({0}) was requested but native validation found {1} naked (free) edge(s); building without glue to avoid corrupting near-coincident faces.", options.GlueMode, report.CountOf(OcctValidationIssueCategory.NakedEdge)));
                    return false;
                }
            }
            finally
            {
                validateResult.Dispose();
            }

            // Managed fallback (no native validator): the welded-edge naked-edge check.
            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(face3Ds, options);
            if (summary.IsCleanForGlue)
            {
                return true;
            }

            result.AddDiagnostic(
                OcctDiagnosticSeverity.Warning,
                "SAM_OCCT_GLUE_SKIPPED",
                string.Format("BOP glue ({0}) was requested but the input has {1} naked (free) edge(s); building without glue to avoid corrupting near-coincident faces.", options.GlueMode, summary.NakedEdgeCount));
            return false;
        }

        /// <summary>
        /// The BOP-glue cell-complex build (issue #37 follow-on). Gated on a clean
        /// watertightness report (glue corrupts merely-near-coincident
        /// faces), then run natively via the _ex entry point in an isolated result
        /// so a degrade (dirty input, glued build failure, or a native build
        /// predating the glue ABI) leaves no Error diagnostics on the caller's
        /// result; only a fully successful glued build is merged back and only
        /// then is <c>true</c> returned, so the caller falls through to the
        /// glue-off path otherwise.
        /// </summary>
        private static bool TryGluedBuild(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            // Gate: glue corrupts merely-near-coincident faces, so it must run only
            // on input with no gaps (naked edges). Non-manifold edges - the shared
            // walls glue exists to accelerate - are fine and do NOT block it.
            if (!IsInputCleanForGlue(face3Ds, options, result))
            {
                return false;
            }

            OcctCellComplexResult glueResult = new OcctCellComplexResult();
            OcctTopology topology = null;
            try
            {
                if (!OcctShapeBuilder.TryCreateCellComplexGlued(face3Ds, options.GlueMode, options, glueResult, out topology)
                    || !OcctShapeBuilder.TryDecode(topology, options, glueResult))
                {
                    // Propagate native availability so graceful-degradation
                    // reporting stays accurate, then degrade with a Warning (not
                    // an Error, which would make the later glue-off success read
                    // as a failure).
                    result.NativeAvailable = glueResult.NativeAvailable;
                    result.NativeVersion = glueResult.NativeVersion;
                    result.AddDiagnostic(
                        OcctDiagnosticSeverity.Warning,
                        "SAM_OCCT_GLUE_DEGRADED",
                        string.Format("BOP glue ({0}) did not complete (the native build may predate the glue ABI v3); building without glue.", options.GlueMode));
                    return false;
                }

                result.NativeAvailable = true;
                result.NativeVersion = glueResult.NativeVersion;
                foreach (OcctCell cell in glueResult.Cells)
                {
                    result.AddCell(cell);
                }

                if (options.RetainTopology)
                {
                    result.Topology = topology;
                    topology = null; // ownership transferred to the caller's result
                }

                result.BuildFaceAdjacencies();
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Info,
                    "SAM_OCCT_GLUE_SUCCESS",
                    string.Format("Built the cell complex with BOP glue ({0}) on validated-clean input.", options.GlueMode));
                return true;
            }
            finally
            {
                topology?.Dispose();
                glueResult.Dispose();
            }
        }

        /// <summary>
        /// Reports why a cell-complex build failed to close (issue #37 follow-on).
        /// When <c>ValidateInput</c> is set and the native library is available,
        /// the input face soup is sewn and validated so the diagnostics carry
        /// kernel-grade located issues (naked edges with XYZ, self-intersections) -
        /// the teeth behind <c>ValidateInput</c>. Falls back to the pure-managed
        /// naked-edge analysis when native validation is unavailable or disabled.
        /// </summary>
        private static void ReportInputDiagnostics(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (options.ValidateInput)
            {
                OcctCellComplexResult validateResult = new OcctCellComplexResult();
                try
                {
                    if (OcctShapeBuilder.TryValidateFaces(face3Ds, true, options, validateResult, out OcctValidationReport report) && report != null)
                    {
                        EmitValidationDiagnostics(report, result);
                        return;
                    }
                }
                finally
                {
                    validateResult.Dispose();
                }
            }

            // Managed fallback: the welded-edge naked-edge analysis, which needs
            // no native library.
            OcctOpenShellAnalysis.Report(face3Ds, options, result);
        }

        private static void EmitValidationDiagnostics(OcctValidationReport report, OcctCellComplexResult result)
        {
            if (report.IsWatertight && report.IsValid)
            {
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Info,
                    "SAM_OCCT_VALIDATE_SUCCESS",
                    "Native validation found the sewn input valid and watertight, so OCCT failed to close it for another reason (e.g. a sliver below tolerance).");
                return;
            }

            int nakedCount = report.CountOf(OcctValidationIssueCategory.NakedEdge);
            OcctValidationIssue sample = report.IssuesOf(OcctValidationIssueCategory.NakedEdge).FirstOrDefault();
            Point3D location = sample?.Location;
            int selfIntersections = report.CountOf(OcctValidationIssueCategory.SelfIntersection);

            result.AddDiagnostic(
                OcctDiagnosticSeverity.Warning,
                "SAM_OCCT_VALIDATE_INPUT",
                string.Format(
                    "Native validation of the input: {0}, {1}; {2} naked edge(s){3}, {4} self-intersection(s). This is why OCCT could not build a closed volume.",
                    report.IsValid ? "valid" : "INVALID",
                    report.IsWatertight ? "watertight" : "NOT watertight",
                    nakedCount,
                    location == null ? string.Empty : string.Format(" (e.g. near ({0:0.###}, {1:0.###}, {2:0.###}))", location.X, location.Y, location.Z),
                    selfIntersections));
        }

        public static bool TryTriangulate(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, double linearDeflection, double angularDeflection, bool relativeDeflection, OcctCellComplexResult result, out List<Triangle3D> triangles)
        {
            triangles = new List<Triangle3D>();

            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            return TryTriangulateCore(input, options, linearDeflection, angularDeflection, relativeDeflection, result, out triangles);
        }

        public static bool TryTriangulate(IEnumerable<IReadOnlyList<Point3D>> boundaryLoops, OcctBuildOptions options, double linearDeflection, double angularDeflection, bool relativeDeflection, OcctCellComplexResult result, out List<Triangle3D> triangles)
        {
            triangles = new List<Triangle3D>();

            if (!OcctNativeInputBuilder.TryBuild(boundaryLoops, options, result, out OcctNativeInput input))
            {
                return false;
            }

            return TryTriangulateCore(input, options, linearDeflection, angularDeflection, relativeDeflection, result, out triangles);
        }

        private static bool TryTriangulateCore(OcctNativeInput input, OcctBuildOptions options, double linearDeflection, double angularDeflection, bool relativeDeflection, OcctCellComplexResult result, out List<Triangle3D> triangles)
        {
            triangles = new List<Triangle3D>();

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_triangulate(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    linearDeflection,
                    angularDeflection,
                    relativeDeflection ? 1 : 0,
                    options.Tolerance,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TRIANGULATE_NATIVE_FAILED", string.Format("Native OCCT triangulation returned status {0}.", status));
                    return false;
                }

                int cellCount = OcctNativeMethods.sam_occt_result_cell_count(resultHandle);
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    triangles.AddRange(DecodeTriangle3Ds(resultHandle, cellIndex));
                }

                if (triangles.Count == 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TRIANGULATE_NO_FACES", "Native OCCT triangulation did not return any planar triangles.");
                    return false;
                }

                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TRIANGULATE_SUCCESS", string.Format("Decoded {0} planar triangle(s).", triangles.Count));
                return true;
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryMergeCoplanar(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, double angularTolerance, OcctCellComplexResult result, out List<Face3D> merged)
        {
            merged = new List<Face3D>();

            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_merge_coplanar(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    options.Tolerance,
                    angularTolerance,
                    out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_MERGE_COPLANAR_NATIVE_FAILED", string.Format("Native OCCT coplanar merge returned status {0}.", status));
                    return false;
                }

                int cellCount = OcctNativeMethods.sam_occt_result_cell_count(resultHandle);
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    List<OcctCellFace> faces = DecodeFaces(resultHandle, cellIndex, result);
                    if (faces == null)
                    {
                        continue;
                    }

                    foreach (OcctCellFace face in faces)
                    {
                        if (face?.Face3D != null)
                        {
                            merged.Add(face.Face3D);
                        }
                    }
                }

                if (merged.Count == 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_MERGE_COPLANAR_NO_FACES", "Native OCCT coplanar merge did not return any faces.");
                    return false;
                }

                // ABI v4 (observational): snapshot the merge history (hand-built sew map
                // merged with UnifySameDomain::History) while the handle is alive. The
                // merged faces were decoded above in the same cell-major/face-minor order
                // the history's output ordinals index.
                result.History = OcctHistory.Capture(resultHandle);

                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_MERGE_COPLANAR_SUCCESS", string.Format("Merged into {0} face(s).", merged.Count));
                return true;
            }
            catch (DllNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                result.NativeAvailable = false;
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
                return false;
            }
            finally
            {
                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        OcctNativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal static bool DecodeResult(IntPtr resultHandle, OcctCellComplexResult result)
        {
            int cellCount = OcctNativeMethods.sam_occt_result_cell_count(resultHandle);
            if (cellCount <= 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NO_CELLS", "Native OCCT result did not contain any cells.");
                return false;
            }

            for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                List<OcctCellFace> faces = DecodeFaces(resultHandle, cellIndex, result);
                if (faces == null || faces.Count == 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_CELL_NO_FACES", "Native OCCT cell did not contain any decodable faces.", cellIndex);
                    continue;
                }

                List<Face3D> face3Ds = faces.ConvertAll(x => x.Face3D);
                Shell shell = new Shell(face3Ds);
                double volume = OcctNativeMethods.sam_occt_result_cell_volume(resultHandle, cellIndex);
                Point3D center = DecodeCellCenter(resultHandle, cellIndex);
                result.AddCell(new OcctCell(shell, volume, null, faces, center));
            }

            if (result.Cells.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NO_SHELLS", "Native OCCT cells could not be converted to SAM shells.");
                return false;
            }

            result.BuildFaceAdjacencies();
            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SUCCESS", string.Format("Decoded {0} OCCT cell(s).", result.Cells.Count));
            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY", string.Format("Decoded {0} shared OCCT face adjacency relation(s).", result.FaceAdjacencies.Count));
            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_CELL_CENTERS", string.Format("Decoded {0} OCCT cell center candidate(s).", result.Cells.Count(x => x.Center != null)));
            return true;
        }

        private static Point3D DecodeCellCenter(IntPtr resultHandle, int cellIndex)
        {
            int success = OcctNativeMethods.sam_occt_result_cell_center(resultHandle, cellIndex, out double x, out double y, out double z);
            if (success == 0 || double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            {
                return null;
            }

            return new Point3D(x, y, z);
        }

        private static List<Triangle3D> DecodeTriangle3Ds(IntPtr resultHandle, int cellIndex)
        {
            int faceCount = OcctNativeMethods.sam_occt_result_cell_face_count(resultHandle, cellIndex);
            List<Triangle3D> triangle3Ds = new List<Triangle3D>();

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                // Each triangulated face is a single loop of three points.
                int pointCount = OcctNativeMethods.sam_occt_result_loop_point_count(resultHandle, cellIndex, faceIndex, 0);
                if (pointCount < 3)
                {
                    continue;
                }

                List<Point3D> points = new List<Point3D>();
                for (int pointIndex = 0; pointIndex < 3; pointIndex++)
                {
                    if (OcctNativeMethods.sam_occt_result_point(resultHandle, cellIndex, faceIndex, 0, pointIndex, out double x, out double y, out double z) != 0)
                    {
                        points.Add(new Point3D(x, y, z));
                    }
                }

                if (points.Count != 3)
                {
                    continue;
                }

                try
                {
                    triangle3Ds.Add(new Triangle3D(points[0], points[1], points[2]));
                }
                catch
                {
                    // Skip degenerate (collinear/coincident) triangles.
                }
            }

            return triangle3Ds;
        }

        private static List<OcctCellFace> DecodeFaces(IntPtr resultHandle, int cellIndex, OcctCellComplexResult result)
        {
            int faceCount = OcctNativeMethods.sam_occt_result_cell_face_count(resultHandle, cellIndex);
            List<OcctCellFace> faces = new List<OcctCellFace>();

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                List<IClosedPlanar3D> loops = DecodeLoops(resultHandle, cellIndex, faceIndex);
                if (loops == null || loops.Count == 0)
                {
                    continue;
                }

                Face3D face3D = Face3D.Create(loops);
                if (face3D == null)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_FACE_DECODE_FAILED", "OCCT face loops could not be converted to a SAM Face3D.", cellIndex);
                    continue;
                }

                int topologyKey = OcctNativeMethods.sam_occt_result_face_key(resultHandle, cellIndex, faceIndex);
                faces.Add(new OcctCellFace(face3D, topologyKey));
            }

            return faces;
        }

        private static List<IClosedPlanar3D> DecodeLoops(IntPtr resultHandle, int cellIndex, int faceIndex)
        {
            int loopCount = OcctNativeMethods.sam_occt_result_face_loop_count(resultHandle, cellIndex, faceIndex);
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D>();

            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                int pointCount = OcctNativeMethods.sam_occt_result_loop_point_count(resultHandle, cellIndex, faceIndex, loopIndex);
                if (pointCount < 3)
                {
                    continue;
                }

                List<Point3D> points = new List<Point3D>();
                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    int success = OcctNativeMethods.sam_occt_result_point(resultHandle, cellIndex, faceIndex, loopIndex, pointIndex, out double x, out double y, out double z);
                    if (success == 0)
                    {
                        continue;
                    }

                    points.Add(new Point3D(x, y, z));
                }

                if (points.Count >= 3)
                {
                    loops.Add(new Polygon3D(points));
                }
            }

            return loops.Where(x => x != null).ToList();
        }
    }
}
