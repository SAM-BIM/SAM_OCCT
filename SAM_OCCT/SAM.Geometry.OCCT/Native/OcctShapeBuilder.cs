// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Native
{
    /// <summary>
    /// Try-pattern helpers for the persistent shape-handle entry points
    /// (issue #14), mirroring the diagnostics conventions of
    /// <see cref="OcctCellComplexBuilder"/>.
    /// </summary>
    internal static class OcctShapeBuilder
    {
        public static bool TryCreateTopology(IEnumerable<Shell> shells, OcctBuildOptions options, OcctCellComplexResult result, out OcctTopology topology)
        {
            topology = null;

            if (!OcctNativeInputBuilder.TryBuild(shells, options, result, "SAM_OCCT_TOPOLOGY_INPUT_EMPTY", "No serializable shell geometry was found.", out OcctNativeInput input))
            {
                return false;
            }

            try
            {
                int status = OcctNativeMethods.sam_occt_shape_create_shells(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    input.ShellFaceCounts,
                    input.ShellCount,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    out OcctTopology topology_Temp);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                return HandleCreateStatus(status, topology_Temp, result, out topology);
            }
            catch (DllNotFoundException exception)
            {
                return HandleNativeMissing(exception, result);
            }
            catch (EntryPointNotFoundException exception)
            {
                return HandleEntryPointMissing(exception, result);
            }
        }

        public static bool TryCreateTopology(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result, out OcctTopology topology)
        {
            topology = null;

            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            try
            {
                int status = OcctNativeMethods.sam_occt_shape_create_cell_complex(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    options.AvoidInternalShapes ? 1 : 0,
                    out OcctTopology topology_Temp);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                return HandleCreateStatus(status, topology_Temp, result, out topology);
            }
            catch (DllNotFoundException exception)
            {
                return HandleNativeMissing(exception, result);
            }
            catch (EntryPointNotFoundException exception)
            {
                return HandleEntryPointMissing(exception, result);
            }
        }

        public static bool TryDecode(OcctTopology topology, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (topology == null || topology.IsInvalid || topology.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = OcctNativeMethods.sam_occt_shape_decode(topology, options.Tolerance, out resultHandle);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DECODE_FAILED", string.Format("Native OCCT topology decode returned status {0}.", status));
                    return false;
                }

                return OcctCellComplexBuilder.DecodeResult(resultHandle, result);
            }
            catch (DllNotFoundException exception)
            {
                return HandleNativeMissing(exception, result);
            }
            catch (EntryPointNotFoundException exception)
            {
                return HandleEntryPointMissing(exception, result);
            }
            catch (ObjectDisposedException)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The OCCT topology handle was disposed while in use.");
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

        private static bool HandleCreateStatus(int status, OcctTopology topology_Temp, OcctCellComplexResult result, out OcctTopology topology)
        {
            topology = null;

            if (status != 0)
            {
                topology_Temp?.Dispose();
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_NATIVE_FAILED", string.Format("Native OCCT topology creation returned status {0} ({1}).", status, OcctOpenShellAnalysis.DescribeBuildStatus(status)));
                return false;
            }

            if (topology_Temp == null || topology_Temp.IsInvalid)
            {
                topology_Temp?.Dispose();
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_NATIVE_FAILED", "Native OCCT topology creation did not return a handle.");
                return false;
            }

            topology = topology_Temp;
            return true;
        }

        private static bool HandleNativeMissing(DllNotFoundException exception, OcctCellComplexResult result)
        {
            result.NativeAvailable = false;
            result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_MISSING", string.Format("Native OCCT library '{0}' was not found. {1}", global::SAM.Core.OCCT.Query.NativeLibraryName(), exception.Message));
            return false;
        }

        private static bool HandleEntryPointMissing(EntryPointNotFoundException exception, OcctCellComplexResult result)
        {
            // Also the graceful path when a new managed build runs against a
            // stale native library that predates the shape-handle ABI.
            result.NativeAvailable = false;
            result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_ENTRYPOINT_MISSING", exception.Message);
            return false;
        }
    }
}
