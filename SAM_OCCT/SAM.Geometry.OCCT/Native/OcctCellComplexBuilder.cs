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
                int status = NativeMethods.sam_occt_shells_intersection(
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
                        NativeMethods.sam_occt_free_result(resultHandle);
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
                int status = NativeMethods.sam_occt_shells_union(
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
                        NativeMethods.sam_occt_free_result(resultHandle);
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
                int status = NativeMethods.sam_occt_shells_difference(
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
                        NativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryBuild(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (!OcctNativeInputBuilder.TryBuild(face3Ds, options, result, out OcctNativeInput input))
            {
                return false;
            }

            IntPtr resultHandle = IntPtr.Zero;
            try
            {
                int status = NativeMethods.sam_occt_build_cell_complex(
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

                if (status != 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_FAILED", string.Format("Native OCCT builder returned status {0}.", status));
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
                        NativeMethods.sam_occt_free_result(resultHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static class NativeMethods
        {
            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_build_cell_complex(
                [In] double[] coordinates,
                int pointCount,
                [In] int[] loopPointCounts,
                int loopCount,
                [In] int[] faceLoopCounts,
                int faceCount,
                double tolerance,
                double fuzzyTolerance,
                int runParallel,
                int avoidInternalShapes,
                out IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_shells_difference(
                [In] double[] targetCoordinates,
                int targetPointCount,
                [In] int[] targetLoopPointCounts,
                int targetLoopCount,
                [In] int[] targetFaceLoopCounts,
                int targetFaceCount,
                [In] int[] targetShellFaceCounts,
                int targetShellCount,
                [In] double[] cutterCoordinates,
                int cutterPointCount,
                [In] int[] cutterLoopPointCounts,
                int cutterLoopCount,
                [In] int[] cutterFaceLoopCounts,
                int cutterFaceCount,
                [In] int[] cutterShellFaceCounts,
                int cutterShellCount,
                double tolerance,
                double fuzzyTolerance,
                int runParallel,
                out IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_shells_intersection(
                [In] double[] targetCoordinates,
                int targetPointCount,
                [In] int[] targetLoopPointCounts,
                int targetLoopCount,
                [In] int[] targetFaceLoopCounts,
                int targetFaceCount,
                [In] int[] targetShellFaceCounts,
                int targetShellCount,
                [In] double[] toolCoordinates,
                int toolPointCount,
                [In] int[] toolLoopPointCounts,
                int toolLoopCount,
                [In] int[] toolFaceLoopCounts,
                int toolFaceCount,
                [In] int[] toolShellFaceCounts,
                int toolShellCount,
                double tolerance,
                double fuzzyTolerance,
                int runParallel,
                out IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_shells_union(
                [In] double[] coordinates,
                int pointCount,
                [In] int[] loopPointCounts,
                int loopCount,
                [In] int[] faceLoopCounts,
                int faceCount,
                [In] int[] shellFaceCounts,
                int shellCount,
                double tolerance,
                double fuzzyTolerance,
                int runParallel,
                out IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern void sam_occt_free_result(IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_cell_count(IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_cell_face_count(IntPtr resultHandle, int cellIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern double sam_occt_result_cell_volume(IntPtr resultHandle, int cellIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_cell_center(IntPtr resultHandle, int cellIndex, out double x, out double y, out double z);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_face_loop_count(IntPtr resultHandle, int cellIndex, int faceIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_face_key(IntPtr resultHandle, int cellIndex, int faceIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_loop_point_count(IntPtr resultHandle, int cellIndex, int faceIndex, int loopIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_point(IntPtr resultHandle, int cellIndex, int faceIndex, int loopIndex, int pointIndex, out double x, out double y, out double z);
        }

        private static bool DecodeResult(IntPtr resultHandle, OcctCellComplexResult result)
        {
            int cellCount = NativeMethods.sam_occt_result_cell_count(resultHandle);
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
                double volume = NativeMethods.sam_occt_result_cell_volume(resultHandle, cellIndex);
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
            int success = NativeMethods.sam_occt_result_cell_center(resultHandle, cellIndex, out double x, out double y, out double z);
            if (success == 0 || double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            {
                return null;
            }

            return new Point3D(x, y, z);
        }

        private static List<OcctCellFace> DecodeFaces(IntPtr resultHandle, int cellIndex, OcctCellComplexResult result)
        {
            int faceCount = NativeMethods.sam_occt_result_cell_face_count(resultHandle, cellIndex);
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

                int topologyKey = NativeMethods.sam_occt_result_face_key(resultHandle, cellIndex, faceIndex);
                faces.Add(new OcctCellFace(face3D, topologyKey));
            }

            return faces;
        }

        private static List<IClosedPlanar3D> DecodeLoops(IntPtr resultHandle, int cellIndex, int faceIndex)
        {
            int loopCount = NativeMethods.sam_occt_result_face_loop_count(resultHandle, cellIndex, faceIndex);
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D>();

            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                int pointCount = NativeMethods.sam_occt_result_loop_point_count(resultHandle, cellIndex, faceIndex, loopIndex);
                if (pointCount < 3)
                {
                    continue;
                }

                List<Point3D> points = new List<Point3D>();
                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    int success = NativeMethods.sam_occt_result_point(resultHandle, cellIndex, faceIndex, loopIndex, pointIndex, out double x, out double y, out double z);
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
