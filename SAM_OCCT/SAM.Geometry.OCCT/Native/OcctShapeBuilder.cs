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

        internal enum ShapeOperation
        {
            Union,
            Difference,
            Intersection,
            Repair
        }

        public static bool TryOperate(ShapeOperation operation, OcctTopology topology, OcctTopology secondTopology, OcctBuildOptions options, double minArea, OcctCellComplexResult result, out OcctTopology output)
        {
            output = null;

            if (topology == null || topology.IsInvalid || topology.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            bool requiresSecond = operation == ShapeOperation.Difference || operation == ShapeOperation.Intersection;
            if (requiresSecond && (secondTopology == null || secondTopology.IsInvalid || secondTopology.IsClosed))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The second OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            try
            {
                int status;
                OcctTopology output_Temp;
                int runParallel = options.RunParallel ? 1 : 0;
                switch (operation)
                {
                    case ShapeOperation.Union:
                        status = OcctNativeMethods.sam_occt_shape_union(topology, options.FuzzyTolerance, runParallel, out output_Temp);
                        break;

                    case ShapeOperation.Difference:
                        status = OcctNativeMethods.sam_occt_shape_difference(topology, secondTopology, options.FuzzyTolerance, runParallel, out output_Temp);
                        break;

                    case ShapeOperation.Intersection:
                        status = OcctNativeMethods.sam_occt_shape_intersection(topology, secondTopology, options.FuzzyTolerance, runParallel, out output_Temp);
                        break;

                    case ShapeOperation.Repair:
                        status = OcctNativeMethods.sam_occt_shape_repair(topology, options.FuzzyTolerance, runParallel, minArea, out output_Temp);
                        break;

                    default:
                        return false;
                }

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                return HandleCreateStatus(status, output_Temp, result, out output);
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
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "An OCCT topology handle was disposed while in use.");
                return false;
            }
        }

        /// <summary>
        /// Shape-handle equivalent of the legacy shell operations, used when
        /// OcctBuildOptions.RetainTopology is set: create the input topology
        /// (per-shell solids), run the operation natively, decode into the
        /// result and attach the retained output handle (owned by the result).
        /// </summary>
        public static bool TryOperateRetained(ShapeOperation operation, IEnumerable<Shell> shells, IEnumerable<Shell> secondShells, OcctBuildOptions options, double minArea, OcctCellComplexResult result)
        {
            if (!TryCreateTopology(shells, options, result, out OcctTopology input))
            {
                return false;
            }

            OcctTopology secondInput = null;
            OcctTopology output = null;
            try
            {
                bool requiresSecond = operation == ShapeOperation.Difference || operation == ShapeOperation.Intersection;
                if (requiresSecond && !TryCreateTopology(secondShells, options, result, out secondInput))
                {
                    return false;
                }

                if (!TryOperate(operation, input, secondInput, options, minArea, result, out output))
                {
                    return false;
                }

                if (!TryDecode(output, options, result))
                {
                    return false;
                }

                result.Topology = output;
                output = null;
                return true;
            }
            finally
            {
                input.Dispose();
                secondInput?.Dispose();
                output?.Dispose();
            }
        }

        /// <summary>
        /// Exports the live topology to a STEP or IGES file (issue #20). The
        /// handle stays valid and caller-owned. Guard paths (null/disposed
        /// handle, null/empty path) never touch the native library.
        /// </summary>
        public static bool TryExport(OcctTopology topology, string path, OcctExchangeFormat format, OcctCellComplexResult result)
        {
            if (topology == null || topology.IsInvalid || topology.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_TOPOLOGY_DISPOSED", "The OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_INPUT", "No export file path was supplied.");
                return false;
            }

            try
            {
                int status = format == OcctExchangeFormat.Iges
                    ? OcctNativeMethods.sam_occt_shape_export_iges(topology, path)
                    : OcctNativeMethods.sam_occt_shape_export_step(topology, path);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_NATIVE_FAILED", string.Format("Native OCCT {0} export returned status {1} ({2}).", format, status, DescribeExchangeStatus(status)));
                    return false;
                }

                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_EXPORT_SUCCESS", string.Format("Exported OCCT topology to {0} file '{1}'.", format, path));
                return true;
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
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_TOPOLOGY_DISPOSED", "The OCCT topology handle was disposed while in use.");
                return false;
            }
        }

        /// <summary>
        /// Imports a STEP or IGES file into a new caller-owned topology handle
        /// (issue #20). The managed File.Exists pre-check keeps the missing-file
        /// path off the native library; everything else maps native status.
        /// </summary>
        public static bool TryImport(string path, OcctExchangeFormat format, OcctCellComplexResult result, out OcctTopology topology)
        {
            topology = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_IMPORT_INPUT", "No import file path was supplied.");
                return false;
            }

            if (!System.IO.File.Exists(path))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_IMPORT_FILE_MISSING", string.Format("Import file '{0}' was not found.", path));
                return false;
            }

            try
            {
                int status;
                OcctTopology topology_Temp;
                if (format == OcctExchangeFormat.Iges)
                {
                    status = OcctNativeMethods.sam_occt_shape_import_iges(path, out topology_Temp);
                }
                else
                {
                    status = OcctNativeMethods.sam_occt_shape_import_step(path, out topology_Temp);
                }

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (status != 0)
                {
                    topology_Temp?.Dispose();
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_IMPORT_NATIVE_FAILED", string.Format("Native OCCT {0} import returned status {1} ({2}).", format, status, DescribeExchangeStatus(status)));
                    return false;
                }

                if (topology_Temp == null || topology_Temp.IsInvalid)
                {
                    topology_Temp?.Dispose();
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_IMPORT_NATIVE_FAILED", "Native OCCT import did not return a handle.");
                    return false;
                }

                topology = topology_Temp;
                result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_IMPORT_SUCCESS", string.Format("Imported OCCT topology with {0} solid(s) from {1} file.", topology.SolidCount, format));
                return true;
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

        private static string DescribeExchangeStatus(int status)
        {
            switch (status)
            {
                case 50: return "the OCCT shape handle was invalid";
                case 60: return "a null or empty file path was supplied";
                case 61: return "the file could not be opened or read";
                case 62: return "the OCCT writer/transfer did not complete";
                case 63: return "the file contained no transferable shape";
                case 64: return "the OCCT Data Exchange runtime (TKDESTEP/TKDEIGES) was not found - deploy the Data Exchange DLLs (e.g. run build-native.ps1)";
                case 99: return "an unexpected native exception was thrown";
                default: return "unrecognised native status";
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
