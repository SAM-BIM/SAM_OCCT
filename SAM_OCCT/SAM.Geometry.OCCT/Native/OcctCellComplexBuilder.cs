// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
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

                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DECODER_PENDING", "Native call succeeded, but managed result decoding is not implemented yet.");
                return false;
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
        }
    }
}
