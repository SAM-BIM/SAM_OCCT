// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Runtime.InteropServices;

namespace SAM.Geometry.OCCT.Native
{
    internal static class OcctNativeMethods
    {
        private static readonly object abiVersionLock = new object();
        private static bool abiVersionProbed;
        private static string abiVersionString;

        /// <summary>
        /// Native ABI revision as a string, probed once per process.
        /// Null when the native library is missing or predates sam_occt_abi_version.
        /// </summary>
        public static string AbiVersionString
        {
            get
            {
                if (!abiVersionProbed)
                {
                    lock (abiVersionLock)
                    {
                        if (!abiVersionProbed)
                        {
                            try
                            {
                                abiVersionString = sam_occt_abi_version().ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }
                            catch
                            {
                                // DllNotFoundException or EntryPointNotFoundException (stale
                                // native build) - version stays null, callers degrade gracefully.
                                abiVersionString = null;
                            }

                            abiVersionProbed = true;
                        }
                    }
                }

                return abiVersionString;
            }
        }

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_abi_version();

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern void sam_occt_shape_release(IntPtr shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_create_cell_complex(
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            double fuzzyTolerance,
            int runParallel,
            int avoidInternalShapes,
            out OcctTopology shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_create_shells(
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            [In] int[] shellFaceCounts,
            int shellCount,
            double fuzzyTolerance,
            int runParallel,
            out OcctTopology shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_union(
            OcctTopology shapeHandle,
            double fuzzyTolerance,
            int runParallel,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_difference(
            OcctTopology targetShapeHandle,
            OcctTopology cutterShapeHandle,
            double fuzzyTolerance,
            int runParallel,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_intersection(
            OcctTopology targetShapeHandle,
            OcctTopology toolShapeHandle,
            double fuzzyTolerance,
            int runParallel,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_repair(
            OcctTopology shapeHandle,
            double fuzzyTolerance,
            int runParallel,
            double minArea,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_imprint(
            OcctTopology shapeHandle,
            double fuzzyTolerance,
            int runParallel,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_make_volume(
            OcctTopology shapeHandle,
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            double fuzzyTolerance,
            int runParallel,
            int avoidInternalShapes,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_extrude(
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            double directionX,
            double directionY,
            double directionZ,
            out OcctTopology shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_offset(
            OcctTopology shapeHandle,
            double offset,
            double tolerance,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_thick_solid(
            OcctTopology shapeHandle,
            double thickness,
            double tolerance,
            out OcctTopology shapeHandleOut);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_solid_count(OcctTopology shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_point_in_solid(
            OcctTopology shapeHandle,
            double x,
            double y,
            double z,
            double tolerance);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_decode(
            OcctTopology shapeHandle,
            double tolerance,
            out IntPtr resultHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_export_step(
            OcctTopology shapeHandle,
            [MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_export_iges(
            OcctTopology shapeHandle,
            [MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_import_step(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            out OcctTopology shapeHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_shape_import_iges(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            out OcctTopology shapeHandle);

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
        public static extern int sam_occt_shells_repair(
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
            double minArea,
            out IntPtr resultHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_triangulate(
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            double linearDeflection,
            double angularDeflection,
            int relativeDeflection,
            double tolerance,
            out IntPtr resultHandle);

        [DllImport("SAM.Occt.Native", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam_occt_merge_coplanar(
            [In] double[] coordinates,
            int pointCount,
            [In] int[] loopPointCounts,
            int loopCount,
            [In] int[] faceLoopCounts,
            int faceCount,
            double tolerance,
            double angularTolerance,
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
}
