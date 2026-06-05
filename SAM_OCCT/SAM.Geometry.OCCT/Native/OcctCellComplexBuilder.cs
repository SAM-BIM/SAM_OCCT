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
            public static extern void sam_occt_free_result(IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_cell_count(IntPtr resultHandle);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_cell_face_count(IntPtr resultHandle, int cellIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern double sam_occt_result_cell_volume(IntPtr resultHandle, int cellIndex);

            [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sam_occt_result_face_loop_count(IntPtr resultHandle, int cellIndex, int faceIndex);

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
                List<Face3D> face3Ds = DecodeFaces(resultHandle, cellIndex, result);
                if (face3Ds == null || face3Ds.Count == 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_CELL_NO_FACES", "Native OCCT cell did not contain any decodable faces.", cellIndex);
                    continue;
                }

                Shell shell = new Shell(face3Ds);
                double volume = NativeMethods.sam_occt_result_cell_volume(resultHandle, cellIndex);
                result.AddCell(new OcctCell(shell, volume));
            }

            if (result.Cells.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NO_SHELLS", "Native OCCT cells could not be converted to SAM shells.");
                return false;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SUCCESS", string.Format("Decoded {0} OCCT cell(s).", result.Cells.Count));
            return true;
        }

        private static List<Face3D> DecodeFaces(IntPtr resultHandle, int cellIndex, OcctCellComplexResult result)
        {
            int faceCount = NativeMethods.sam_occt_result_cell_face_count(resultHandle, cellIndex);
            List<Face3D> face3Ds = new List<Face3D>();

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

                face3Ds.Add(face3D);
            }

            return face3Ds;
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
