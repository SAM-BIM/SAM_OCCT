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
            if (options.SewBeforeBuild && CanAttemptSew(face3DList, options, result))
            {
                // Build the sew candidate in isolation so its coverage can be checked before it is adopted:
                // manifold sewing can drop T-junction partitions without any error (an under-split).
                OcctCellComplexResult sewCandidate = new OcctCellComplexResult();
                if (TrySewThenMakeVolume(face3DList, options, sewCandidate))
                {
                    // False only when the guard proved the sew build invalid and found no acceptable replacement:
                    // fail closed instead of falling through to another build.
                    return AdoptSewCandidate(face3DList, options, sewCandidate, (OcctCellComplexResult candidate, out bool decoded) => TryDirectBuild(input, options, candidate, out decoded), result);
                }

                result.NativeAvailable = sewCandidate.NativeAvailable;
                result.NativeVersion = sewCandidate.NativeVersion;
                sewCandidate.Dispose();
            }

            try
            {
                int status = TryDirectBuild(input, options, result, out bool decoded);
                if (status != 0)
                {
                    // issue #37: on a hard close failure (MakerVolume reported
                    // errors or produced no closed solid) attempt one sew-then-
                    // rebuild before giving up - this recovers the triangulated /
                    // near-touching face soups that defeat a direct MakerVolume.
                    // Skipped when SewBeforeBuild already tried (and failed) above.
                    if ((status == 30 || status == 40)
                        && !options.SewBeforeBuild
                        && CanAttemptSew(face3DList, options, result)
                        && TrySewThenMakeVolume(face3DList, options, result))
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
        }

        /// <summary>
        /// The plain native MakerVolume build: returns the native status and, when it is 0, decodes the cells
        /// (and the ABI v4 history) into <paramref name="result"/>. Native-missing exceptions propagate.
        /// </summary>
        private static int TryDirectBuild(OcctNativeInput input, OcctBuildOptions options, OcctCellComplexResult result, out bool decoded)
        {
            decoded = false;
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
                    return status;
                }

                decoded = DecodeResult(resultHandle, result);

                // ABI v4 (observational): snapshot the MakerVolume BRepTools_History
                // while the handle is alive, so the solver can compose an exact
                // source->output map instead of the geometric NearestSourceIndex
                // heuristic. Null on a pre-v4 native or when history is unavailable.
                if (decoded)
                {
                    result.History = OcctHistory.Capture(resultHandle);
                }

                return status;
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

        /// <summary>The isolated direct MakerVolume build used as the sew guard's alternative (see <see cref="TryDirectBuild"/>).</summary>
        internal delegate int DirectBuild(OcctCellComplexResult candidate, out bool decoded);

        /// <summary>
        /// Adopts a successful sew-then-MakerVolume candidate. With <see cref="OcctBuildOptions.ValidateSewCoverage"/>
        /// it is first checked against a direct MakerVolume build made in isolation:
        /// <list type="bullet">
        /// <item>A sew build with a non-finite or non-positive signed cell volume, or with overlapping cells, is
        /// invalid. The direct build replaces it only if it passes its own cell and coverage checks
        /// (<see cref="OcctUnderSplitGuard.EvaluateReplacement"/>); otherwise the build fails closed with an Error and
        /// no cells.</item>
        /// <item>A valid sew build whose coverage shows faces cutting through a cell or unused, or whose total volume
        /// disagrees with the direct build, is suspect. The direct build replaces it only when
        /// <see cref="OcctUnderSplitGuard.Evaluate"/> proves it a valid, strictly better refinement. When the sew
        /// build is both suspect and in volume conflict (not a pure under-split), a direct build that passes
        /// <see cref="OcctUnderSplitGuard.EvaluateReplacement"/> on its own may also replace it. Otherwise the valid sew
        /// build is kept with a Warning.</item>
        /// </list>
        /// The direct build is made on every validated call, so a volume disagreement is found even when the sew
        /// coverage looks clean (an inverted or duplicated envelope cell covers every face).
        /// Returns false only when the build failed closed.
        /// </summary>
        internal static bool AdoptSewCandidate(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult sewCandidate, DirectBuild directBuild, OcctCellComplexResult result)
        {
            const string sewSuccess = "Built the cell complex via native sew-and-heal before MakerVolume (SewBeforeBuild).";
            double minArea = global::SAM.Core.Tolerance.MacroDistance;

            if (!options.ValidateSewCoverage)
            {
                result.AdoptFrom(sewCandidate);
                sewCandidate.Dispose();
                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", sewSuccess);
                return true;
            }

            OcctCellComplexResult directCandidate = new OcctCellComplexResult();
            try
            {
                FaceCoverageReport sewCoverage = null;
                try
                {
                    sewCoverage = Query.FaceCoverage(face3Ds, sewCandidate.Cells.Select(x => x?.Shell), minArea, options.Tolerance, options.FuzzyTolerance);
                }
                catch (Exception exception)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_COVERAGE_UNAVAILABLE", string.Format("Input-face coverage of the sew build could not be measured ({0}); only its cell volumes and overlaps were checked.", exception.Message));
                }

                // Intrinsic invalidity first: it needs no alternative and is never hidden by clean coverage.
                List<string> invalidity = OcctUnderSplitGuard.CheckSignedVolumes(sewCandidate.Cells, "sew cell");

                // The direct build is the volume reference and supplies probe points for the overlap check. It is not
                // trusted because it is different: it must pass its own checks before it can be adopted.
                List<string> directFailures = new List<string>();
                try
                {
                    int status = directBuild(directCandidate, out bool decoded);
                    if (status != 0 || !decoded)
                    {
                        directFailures.Add(string.Format("direct build returned status {0} ({1})", status, OcctOpenShellAnalysis.DescribeBuildStatus(status)));
                    }
                    else if (directCandidate.Cells.Count == 0)
                    {
                        directFailures.Add("direct build produced no cells");
                    }
                }
                catch (DllNotFoundException exception)
                {
                    directFailures.Add(string.Format("direct build unavailable ({0})", exception.Message));
                }
                catch (EntryPointNotFoundException exception)
                {
                    directFailures.Add(string.Format("direct build unavailable ({0})", exception.Message));
                }

                IReadOnlyList<OcctCell> directCells = directFailures.Count == 0 ? directCandidate.Cells : null;
                if (directCells != null)
                {
                    directFailures.AddRange(OcctUnderSplitGuard.CheckSignedVolumes(directCells, "direct cell"));
                }

                string volumeConflict = null;
                if (invalidity.Count == 0 && directFailures.Count == 0)
                {
                    double inputArea = face3Ds.Sum(x => { double area = x?.GetArea() ?? 0; return double.IsNaN(area) ? 0 : area; });
                    volumeConflict = OcctUnderSplitGuard.CheckVolumeAgreement(sewCandidate.Cells, directCells, inputArea, options);
                }

                bool suspect = OcctUnderSplitGuard.IsSuspect(sewCoverage);

                // Overlap test (pairwise point-in-cell): run when something is already in doubt, or when no direct
                // volume is available to compare. A clean, positive sew build that agrees with the direct volume
                // cannot hide an overlap: overlapping cells add volume that only missing (uncovered) space could offset.
                if (invalidity.Count == 0 && (suspect || volumeConflict != null || directFailures.Count != 0 || sewCoverage == null))
                {
                    IEnumerable<Point3D> probes = directCells == null ? null : OcctUnderSplitGuard.InternalPoints(directCells, options);
                    invalidity.AddRange(OcctUnderSplitGuard.CheckOverlap(sewCandidate.Cells, probes, options, "sew cells"));
                }

                if (invalidity.Count != 0)
                {
                    return ReplaceInvalidSew(face3Ds, options, sewCandidate, directCandidate, directCells, invalidity, directFailures, minArea, result);
                }

                if (!suspect && volumeConflict == null)
                {
                    result.AdoptFrom(sewCandidate);
                    result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", sewSuccess);
                    return true;
                }

                bool interior = sewCoverage != null && sewCoverage.InteriorArea > 0;
                if (interior)
                {
                    List<FaceCoverageEntry> suspects = sewCoverage.Entries.Where(x => x.InteriorArea > 0).OrderByDescending(x => x.InteriorArea).ToList();
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED", string.Format(
                        "The sew build left {0} input face(s) ({1:0.###} area) cutting through a cell instead of bounding it - a likely under-split (merged spaces). Faces: {2}{3}. Coverage: {4}.",
                        suspects.Count, sewCoverage.InteriorArea, string.Join("; ", suspects.Take(10).Select(x => x.ToString())), suspects.Count > 10 ? "; ..." : string.Empty, sewCoverage.Summary()));
                }

                // RetainTopology needs the sew path's live handle (the direct build frees its handle), so the
                // alternative cannot be adopted there: keep the valid sew build, flagged.
                if (options.RetainTopology)
                {
                    result.AdoptFrom(sewCandidate);
                    result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", sewSuccess);
                    if (interior)
                    {
                        result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_UNRELIABLE", "The sew build is suspected of under-splitting, but RetainTopology requires the sew path, so no direct alternative was tried. Cell count may be too low.");
                    }

                    AddVolumeConflict(volumeConflict, "RetainTopology requires the sew path", result);
                    return true;
                }

                OcctUnderSplitGuard.Evaluation evaluation;
                if (directFailures.Count != 0)
                {
                    evaluation = new OcctUnderSplitGuard.Evaluation(false, directFailures, sewCoverage, null);
                }
                else
                {
                    try
                    {
                        evaluation = OcctUnderSplitGuard.Evaluate(face3Ds, sewCandidate.Cells, directCells, options, minArea, sewCoverage);
                    }
                    catch (Exception exception)
                    {
                        evaluation = new OcctUnderSplitGuard.Evaluation(false, new List<string> { string.Format("evaluation failed ({0})", exception.Message) }, sewCoverage, null);
                    }
                }

                if (evaluation.Adopt)
                {
                    result.AdoptFrom(directCandidate);
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_FALLBACK_DIRECT", string.Format(
                        "Adopted the direct MakerVolume build instead of the sew build: {0} cell(s) instead of {1}, and it passed the closure, overlap, refinement and coverage checks. Sew coverage: {2}. Direct coverage: {3}.",
                        directCandidate.Cells.Count, sewCandidate.Cells.Count, evaluation.SewCoverage?.Summary(), evaluation.DirectCoverage?.Summary()));
                    return true;
                }

                // Incomplete coverage AND a total-volume conflict: the sew build lost or gained volume, so it is not a
                // pure under-split (merging rooms keeps the volume) and refining it proves nothing. The direct build may
                // still replace it, but only on its own merits, exactly as for an invalid sew build.
                if (suspect && volumeConflict != null && directFailures.Count == 0)
                {
                    OcctUnderSplitGuard.Evaluation replacement;
                    try
                    {
                        replacement = OcctUnderSplitGuard.EvaluateReplacement(face3Ds, directCells, options, minArea);
                    }
                    catch (Exception exception)
                    {
                        replacement = new OcctUnderSplitGuard.Evaluation(false, new List<string> { string.Format("evaluation failed ({0})", exception.Message) }, null, null);
                    }

                    if (replacement.Adopt)
                    {
                        result.AdoptFrom(directCandidate);
                        result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_FALLBACK_DIRECT", string.Format(
                            "Adopted the direct MakerVolume build instead of the sew build: the sew build's coverage is incomplete and its volume disagrees ({0}), so it is not an under-split to refine. The direct build ({1} cell(s)) passed the closure, positive-volume, sliver, overlap and complete-coverage checks on its own. Sew coverage: {2}. Direct coverage: {3}.",
                            volumeConflict, directCandidate.Cells.Count, sewCoverage?.Summary(), replacement.DirectCoverage?.Summary()));
                        return true;
                    }

                    evaluation.Failures.AddRange(replacement.Failures.Select(x => "as a replacement: " + x));
                }

                result.AdoptFrom(sewCandidate);
                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", sewSuccess);
                if (interior)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_UNRELIABLE", string.Format(
                        "The sew build is suspected of under-splitting and the direct alternative was rejected ({0}). The sew build was kept; check the space count against the expected rooms.",
                        string.Join("; ", evaluation.Failures.Take(8))));
                }
                else if (suspect)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_UNUSED_FACES", string.Format(
                        "{0:0.###} area of input faces bounds no cell and lies outside every cell; a direct build did not use them either ({1}). Coverage: {2}.",
                        sewCoverage.UnusedArea, string.Join("; ", evaluation.Failures.Take(4)), sewCoverage.Summary()));
                }

                AddVolumeConflict(volumeConflict, string.Format("the direct alternative was rejected: {0}", string.Join("; ", evaluation.Failures.Take(4))), result);
                return true;
            }
            finally
            {
                sewCandidate.Dispose();
                directCandidate.Dispose();
            }
        }

        /// <summary>
        /// The sew build is demonstrably invalid. Adopts the direct build when it passes its own cell and coverage
        /// checks; otherwise fails closed: an Error, no cells and false, so no downstream cluster is built from it.
        /// The caller disposes both candidates, including the sew build's retained topology.
        /// </summary>
        private static bool ReplaceInvalidSew(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult sewCandidate, OcctCellComplexResult directCandidate, IReadOnlyList<OcctCell> directCells, List<string> invalidity, List<string> directFailures, double minArea, OcctCellComplexResult result)
        {
            result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_SEW_BUILD_INVALID", string.Format(
                "The sew build is invalid and cannot be used: {0}{1}. Sew: {2} cell(s), total volume {3:0.###} m3.",
                string.Join("; ", invalidity.Take(8)), invalidity.Count > 8 ? "; ..." : string.Empty, sewCandidate.Cells.Count, OcctUnderSplitGuard.TotalVolume(sewCandidate.Cells)));

            List<string> failures = new List<string>(directFailures);
            OcctUnderSplitGuard.Evaluation evaluation = null;
            if (options.RetainTopology)
            {
                // The direct build frees its native handle, so it cannot honour the RetainTopology contract.
                failures.Insert(0, "RetainTopology requires the sew build's topology, so no direct alternative can be adopted");
            }
            else if (failures.Count == 0)
            {
                try
                {
                    evaluation = OcctUnderSplitGuard.EvaluateReplacement(face3Ds, directCells, options, minArea);
                    failures.AddRange(evaluation.Failures);
                }
                catch (Exception exception)
                {
                    failures.Add(string.Format("evaluation failed ({0})", exception.Message));
                }
            }

            if (failures.Count == 0 && evaluation != null && evaluation.Adopt)
            {
                result.AdoptFrom(directCandidate);
                result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_FALLBACK_DIRECT", string.Format(
                    "Adopted the direct MakerVolume build instead of the invalid sew build: {0} cell(s), total volume {1:0.###} m3. It passed the closure, positive-volume, sliver, overlap and complete-coverage checks; refinement of the sew build and coverage gain were not required because the sew build is not a valid reference. Direct coverage: {2}.",
                    directCandidate.Cells.Count, OcctUnderSplitGuard.TotalVolume(directCandidate.Cells), evaluation.DirectCoverage?.Summary()));
                return true;
            }

            result.NativeAvailable = sewCandidate.NativeAvailable;
            result.NativeVersion = sewCandidate.NativeVersion;
            result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_BUILD_REJECTED", string.Format(
                "The sew build is invalid and no acceptable replacement exists ({0}{1}). No cells were returned.",
                string.Join("; ", failures.Take(8)), failures.Count > 8 ? "; ..." : string.Empty));
            return false;
        }

        private static void AddVolumeConflict(string volumeConflict, string reason, OcctCellComplexResult result)
        {
            if (volumeConflict == null)
            {
                return;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_SEW_VOLUME_CONFLICT", string.Format(
                "The sew build was kept although its volume disagrees with a direct build ({0}); {1}. Its cells are valid, so the disagreement does not show which build is right: check the spaces.",
                volumeConflict, reason));
        }

        internal static bool CanAttemptSew(List<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result = null)
        {
            int maxSewFaceCount = options?.MaxSewFaceCount ?? new OcctBuildOptions().MaxSewFaceCount;
            int faceCount = face3Ds?.Count ?? 0;

            if (maxSewFaceCount <= 0 || faceCount <= maxSewFaceCount)
            {
                return true;
            }

            result?.AddDiagnostic(
                OcctDiagnosticSeverity.Warning,
                "SAM_OCCT_SEW_SKIPPED_LARGE_INPUT",
                string.Format("Skipped native sew-and-heal for {0} face(s) because MaxSewFaceCount is {1}. Large analytical panel soups can exhaust the host stack in OCCT sewing; building directly instead.", faceCount, maxSewFaceCount));
            return false;
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
