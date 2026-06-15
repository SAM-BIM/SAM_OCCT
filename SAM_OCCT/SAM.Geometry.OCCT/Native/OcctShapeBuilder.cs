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

        /// <summary>
        /// Minimum distance between two sets of shells (issue #28): build each
        /// set into its own topology, then call the native
        /// BRepExtrema_DistShapeShape primitive. Returns the gap distance and
        /// the closest point on each side. Both topologies are always disposed;
        /// neither is retained. Distance of zero means the shapes touch or
        /// overlap.
        /// </summary>
        public static bool TryDistance(IEnumerable<Shell> shells, IEnumerable<Shell> otherShells, OcctBuildOptions options, OcctCellComplexResult result, out double distance, out Point3D pointA, out Point3D pointB)
        {
            distance = double.NaN;
            pointA = null;
            pointB = null;

            if (!TryCreateTopology(shells, options, result, out OcctTopology topologyA))
            {
                return false;
            }

            OcctTopology topologyB = null;
            try
            {
                if (!TryCreateTopology(otherShells, options, result, out topologyB))
                {
                    return false;
                }

                try
                {
                    int status = OcctNativeMethods.sam_occt_shape_distance(
                        topologyA,
                        topologyB,
                        out double distance_Temp,
                        out double ax,
                        out double ay,
                        out double az,
                        out double bx,
                        out double by,
                        out double bz);

                    result.NativeAvailable = true;
                    result.NativeVersion = OcctNativeMethods.AbiVersionString;

                    if (status != 0)
                    {
                        result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DISTANCE_NATIVE_FAILED", string.Format("Native OCCT distance returned status {0} ({1}).", status, DescribeDistanceStatus(status)));
                        return false;
                    }

                    distance = distance_Temp;
                    pointA = new Point3D(ax, ay, az);
                    pointB = new Point3D(bx, by, bz);
                    // Distance is a cell-less query; mark success explicitly so
                    // result.Success is true without a decoded cell complex.
                    result.OperationSucceeded = true;
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
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "An OCCT topology handle was disposed while in use.");
                    return false;
                }
            }
            finally
            {
                topologyA.Dispose();
                topologyB?.Dispose();
            }
        }

        private static string DescribeDistanceStatus(int status)
        {
            switch (status)
            {
                case 10: return "a null distance output pointer was supplied";
                case 30: return "the OCCT distance computation did not complete";
                case 40: return "the shapes produced no distance solution";
                case 50: return "an OCCT shape handle was invalid";
                case 99: return "an unexpected native exception was thrown";
                default: return "unrecognised native status";
            }
        }

        /// <summary>
        /// Extrudes planar footprint Face3Ds along a direction vector (issue
        /// #30): serialize the footprints, call the native BRepPrimAPI_MakePrism
        /// entry point to get one closed solid per footprint, then decode into
        /// the result. The output handle is retained on the result only when
        /// OcctBuildOptions.RetainTopology is set, otherwise disposed.
        /// </summary>
        public static bool TryExtrude(IEnumerable<Face3D> face3Ds, Vector3D direction, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            OcctTopology topology = null;
            try
            {
                int status = OcctNativeMethods.sam_occt_extrude(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    direction.X,
                    direction.Y,
                    direction.Z,
                    out OcctTopology topology_Temp);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                if (!HandleCreateStatus(status, topology_Temp, result, out topology))
                {
                    return false;
                }

                if (!TryDecode(topology, options, result))
                {
                    return false;
                }

                if (options.RetainTopology)
                {
                    result.Topology = topology;
                    topology = null;
                }

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
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "An OCCT topology handle was disposed while in use.");
                return false;
            }
            finally
            {
                topology?.Dispose();
            }
        }

        /// <summary>
        /// Offsets the skin of each shell's solid by a signed distance (issue
        /// #29) or, when <paramref name="thicken"/> is true, hollows each into a
        /// wall of that thickness. Builds the input topology, calls the native
        /// BRepOffsetAPI entry point, then decodes. The output handle is retained
        /// on the result only when OcctBuildOptions.RetainTopology is set.
        /// </summary>
        public static bool TryOffsetShells(IEnumerable<Shell> shells, double distance, bool thicken, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!TryCreateTopology(shells, options, result, out OcctTopology input))
            {
                return false;
            }

            OcctTopology output = null;
            try
            {
                int status;
                OcctTopology output_Temp;
                if (thicken)
                {
                    status = OcctNativeMethods.sam_occt_shape_thick_solid(input, distance, options.Tolerance, out output_Temp);
                }
                else
                {
                    status = OcctNativeMethods.sam_occt_shape_offset(input, distance, options.Tolerance, out output_Temp);
                }

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                // Map the native status with an offset/thicken-specific message
                // rather than the generic MakerVolume wording of HandleCreateStatus.
                if (status != 0 || output_Temp == null || output_Temp.IsInvalid)
                {
                    output_Temp?.Dispose();
                    result.AddDiagnostic(
                        OcctDiagnosticSeverity.Error,
                        thicken ? "SAM_OCCT_THICKEN_NATIVE_FAILED" : "SAM_OCCT_OFFSET_NATIVE_FAILED",
                        string.Format("Native OCCT {0} returned status {1} ({2}).", thicken ? "thick-solid" : "offset", status, DescribeOffsetStatus(status)));
                    return false;
                }

                output = output_Temp;

                if (!TryDecode(output, options, result))
                {
                    return false;
                }

                if (options.RetainTopology)
                {
                    result.Topology = output;
                    output = null;
                }

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
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "An OCCT topology handle was disposed while in use.");
                return false;
            }
            finally
            {
                input.Dispose();
                output?.Dispose();
            }
        }

        private static string DescribeOffsetStatus(int status)
        {
            switch (status)
            {
                case 10: return "a null output handle was supplied";
                case 12: return "the offset / thickness distance was zero";
                case 30: return "the OCCT offset / thick-solid operation did not complete for any solid - the geometry may be too complex, or the distance too large or the wrong sign (try a smaller magnitude, or the opposite sign)";
                case 40: return "the operation produced no solids";
                case 50: return "the OCCT shape handle was invalid";
                case 99: return "an unexpected native exception was thrown";
                default: return "unrecognised native status";
            }
        }

        internal enum ShapeOperation
        {
            Union,
            Difference,
            Intersection,
            Repair,
            Imprint
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

                    case ShapeOperation.Imprint:
                        status = OcctNativeMethods.sam_occt_shape_imprint(topology, options.FuzzyTolerance, runParallel, out output_Temp);
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
        /// Imprints a set of shells against one another (issue #27): build the
        /// per-shell solids into one topology, run the native General Fuse so
        /// coincident boundary regions are split into matching sub-faces, then
        /// decode the result. The shared sub-faces key-match during decode, so
        /// touching spaces gain second-level FaceAdjacencies. The input topology
        /// is always disposed; the imprinted output is retained on the result
        /// only when OcctBuildOptions.RetainTopology is set, otherwise disposed.
        /// </summary>
        public static bool TryImprintShells(IEnumerable<Shell> shells, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!TryCreateTopology(shells, options, result, out OcctTopology input))
            {
                return false;
            }

            OcctTopology output = null;
            try
            {
                if (!TryOperate(ShapeOperation.Imprint, input, null, options, 0, result, out output))
                {
                    return false;
                }

                if (!TryDecode(output, options, result))
                {
                    return false;
                }

                if (options.RetainTopology)
                {
                    result.Topology = output;
                    output = null;
                }

                return true;
            }
            finally
            {
                input.Dispose();
                output?.Dispose();
            }
        }

        /// <summary>
        /// Sews a face soup into the tightest closed shell/solid (issue #37):
        /// serialize the faces, call the native BRepBuilderAPI_Sewing + ShapeFix
        /// entry point, then decode. When <paramref name="makeSolid"/> is true the
        /// healed closed shells become solids that decode into cells; when false
        /// the relaxed (possibly open) shell is returned with no solids to decode,
        /// so cell-producing callers must pass true. The output handle is retained
        /// on the result only when OcctBuildOptions.RetainTopology is set.
        /// </summary>
        public static bool TrySew(IEnumerable<Face3D> face3Ds, bool makeSolid, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!TrySewFacesToTopology(face3Ds, makeSolid, options, result, out OcctTopology output))
            {
                return false;
            }

            return DecodeAndRetainSewn(output, options, result);
        }

        /// <summary>
        /// Shell overload of <see cref="TrySew(IEnumerable{Face3D}, bool, OcctBuildOptions, OcctCellComplexResult)"/>:
        /// build the per-shell solids into one topology, sew+heal their faces
        /// natively (closing micro-gaps left by an earlier op), then decode.
        /// </summary>
        public static bool TrySew(IEnumerable<Shell> shells, bool makeSolid, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!TryCreateTopology(shells, options, result, out OcctTopology input))
            {
                return false;
            }

            OcctTopology output = null;
            try
            {
                if (!TrySewShapeToTopology(input, makeSolid, options, result, out output))
                {
                    return false;
                }

                if (!TryDecode(output, options, result))
                {
                    return false;
                }

                if (options.RetainTopology)
                {
                    result.Topology = output;
                    output = null;
                }

                return true;
            }
            finally
            {
                input.Dispose();
                output?.Dispose();
            }
        }

        /// <summary>
        /// Sews a face soup into a NEW healed topology handle without decoding it
        /// (issue #37). Used both by <see cref="TrySew(IEnumerable{Face3D}, bool, OcctBuildOptions, OcctCellComplexResult)"/>
        /// and by the OcctCellComplexBuilder sew-then-MakerVolume pipeline, which
        /// re-feeds the healed shell (makeSolid=false) to MakerVolume.
        /// </summary>
        public static bool TrySewFacesToTopology(IEnumerable<Face3D> face3Ds, bool makeSolid, OcctBuildOptions options, OcctCellComplexResult result, out OcctTopology output)
        {
            output = null;

            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            try
            {
                int status = OcctNativeMethods.sam_occt_sew_faces(
                    input.Coordinates,
                    input.Coordinates.Length / 3,
                    input.LoopPointCounts,
                    input.LoopPointCounts.Length,
                    input.FaceLoopCounts,
                    input.FaceCount,
                    options.EffectiveSewingTolerance,
                    options.RunParallel ? 1 : 0,
                    makeSolid ? 1 : 0,
                    out OcctTopology output_Temp);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                return HandleSewStatus(status, output_Temp, result, out output);
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

        /// <summary>
        /// Sews+heals the faces already inside a handle into a NEW topology handle
        /// without decoding it (issue #37). The input handle stays valid.
        /// </summary>
        public static bool TrySewShapeToTopology(OcctTopology input, bool makeSolid, OcctBuildOptions options, OcctCellComplexResult result, out OcctTopology output)
        {
            output = null;

            if (input == null || input.IsInvalid || input.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            try
            {
                int status = OcctNativeMethods.sam_occt_shape_sew(input, options.EffectiveSewingTolerance, options.RunParallel ? 1 : 0, makeSolid ? 1 : 0, out OcctTopology output_Temp);

                result.NativeAvailable = true;
                result.NativeVersion = OcctNativeMethods.AbiVersionString;

                return HandleSewStatus(status, output_Temp, result, out output);
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
        /// BOPAlgo_MakerVolume over the faces of an existing handle into a NEW
        /// handle (no extra faces). The shape-handle core used by the sew-then-
        /// rebuild pipeline (issue #37) after the sewn shell is healed.
        /// </summary>
        public static bool TryMakeVolume(OcctTopology input, OcctBuildOptions options, OcctCellComplexResult result, out OcctTopology output)
        {
            output = null;

            if (input == null || input.IsInvalid || input.IsClosed)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_DISPOSED", "The OCCT topology handle is null, invalid or disposed.");
                return false;
            }

            try
            {
                int status = OcctNativeMethods.sam_occt_shape_make_volume(
                    input,
                    null,
                    0,
                    null,
                    0,
                    null,
                    0,
                    options.FuzzyTolerance,
                    options.RunParallel ? 1 : 0,
                    options.AvoidInternalShapes ? 1 : 0,
                    out OcctTopology output_Temp);

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

        private static bool DecodeAndRetainSewn(OcctTopology output, OcctBuildOptions options, OcctCellComplexResult result)
        {
            try
            {
                if (!TryDecode(output, options, result))
                {
                    return false;
                }

                if (options.RetainTopology)
                {
                    result.Topology = output;
                    output = null;
                }

                return true;
            }
            finally
            {
                output?.Dispose();
            }
        }

        private static bool HandleSewStatus(int status, OcctTopology output_Temp, OcctCellComplexResult result, out OcctTopology output)
        {
            output = null;

            if (status != 0)
            {
                output_Temp?.Dispose();
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Error,
                    status == 40 ? "SAM_OCCT_SEW_NO_CLOSED_SHELL" : "SAM_OCCT_SEW_NATIVE_FAILED",
                    string.Format("Native OCCT sew-and-heal returned status {0} ({1}).", status, OcctOpenShellAnalysis.DescribeSewStatus(status)));
                return false;
            }

            if (output_Temp == null || output_Temp.IsInvalid)
            {
                output_Temp?.Dispose();
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SEW_NATIVE_FAILED", "Native OCCT sew-and-heal did not return a handle.");
                return false;
            }

            output = output_Temp;
            return true;
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
