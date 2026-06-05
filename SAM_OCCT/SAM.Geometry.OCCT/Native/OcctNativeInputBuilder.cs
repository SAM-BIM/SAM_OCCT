// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Native
{
    internal static class OcctNativeInputBuilder
    {
        public static bool TryBuild(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result, out OcctNativeInput input)
        {
            input = null;

            List<double> coordinates = new List<double>();
            List<int> loopPointCounts = new List<int>();
            List<int> faceLoopCounts = new List<int>();

            int faceIndex = 0;
            foreach (Face3D face3D in face3Ds)
            {
                int loopCount = 0;
                if (TryAppendLoop(face3D?.GetExternalEdge3D(), options, result, faceIndex, coordinates, loopPointCounts))
                {
                    loopCount++;
                }

                List<IClosedPlanar3D> internalEdges = face3D?.GetInternalEdge3Ds();
                if (internalEdges != null)
                {
                    foreach (IClosedPlanar3D internalEdge in internalEdges)
                    {
                        if (TryAppendLoop(internalEdge, options, result, faceIndex, coordinates, loopPointCounts))
                        {
                            loopCount++;
                        }
                    }
                }

                if (loopCount == 0)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_FACE_NO_LOOPS", "Face3D has no serializable loops.", faceIndex);
                }
                else
                {
                    faceLoopCounts.Add(loopCount);
                }

                faceIndex++;
            }

            if (faceLoopCounts.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No serializable face loops were found.");
                return false;
            }

            input = new OcctNativeInput
            {
                Coordinates = coordinates.ToArray(),
                LoopPointCounts = loopPointCounts.ToArray(),
                FaceLoopCounts = faceLoopCounts.ToArray(),
                FaceCount = faceLoopCounts.Count
            };

            return true;
        }

        public static bool TryBuild(IEnumerable<Shell> shells, OcctBuildOptions options, OcctCellComplexResult result, string emptyCode, string emptyMessage, out OcctNativeInput input)
        {
            input = null;

            if (shells == null)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, emptyCode, emptyMessage);
                return false;
            }

            List<double> coordinates = new List<double>();
            List<int> loopPointCounts = new List<int>();
            List<int> faceLoopCounts = new List<int>();
            List<int> shellFaceCounts = new List<int>();

            int faceIndex = 0;
            foreach (Shell shell in shells)
            {
                List<Face3D> face3Ds = shell?.Face3Ds;
                if (face3Ds == null || face3Ds.Count == 0)
                {
                    continue;
                }

                int shellFaceCount = 0;
                foreach (Face3D face3D in face3Ds)
                {
                    int loopCount = 0;
                    if (TryAppendLoop(face3D?.GetExternalEdge3D(), options, result, faceIndex, coordinates, loopPointCounts))
                    {
                        loopCount++;
                    }

                    List<IClosedPlanar3D> internalEdges = face3D?.GetInternalEdge3Ds();
                    if (internalEdges != null)
                    {
                        foreach (IClosedPlanar3D internalEdge in internalEdges)
                        {
                            if (TryAppendLoop(internalEdge, options, result, faceIndex, coordinates, loopPointCounts))
                            {
                                loopCount++;
                            }
                        }
                    }

                    if (loopCount == 0)
                    {
                        result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_FACE_NO_LOOPS", "Face3D has no serializable loops.", faceIndex);
                    }
                    else
                    {
                        faceLoopCounts.Add(loopCount);
                        shellFaceCount++;
                    }

                    faceIndex++;
                }

                if (shellFaceCount != 0)
                {
                    shellFaceCounts.Add(shellFaceCount);
                }
            }

            if (shellFaceCounts.Count == 0 || faceLoopCounts.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, emptyCode, emptyMessage);
                return false;
            }

            input = new OcctNativeInput
            {
                Coordinates = coordinates.ToArray(),
                LoopPointCounts = loopPointCounts.ToArray(),
                FaceLoopCounts = faceLoopCounts.ToArray(),
                FaceCount = faceLoopCounts.Count,
                ShellFaceCounts = shellFaceCounts.ToArray(),
                ShellCount = shellFaceCounts.Count
            };

            return true;
        }

        private static bool TryAppendLoop(IClosedPlanar3D closedPlanar3D, OcctBuildOptions options, OcctCellComplexResult result, int sourceIndex, List<double> coordinates, List<int> loopPointCounts)
        {
            List<Point3D> points = null;

            if (closedPlanar3D is ISegmentable3D)
            {
                points = ((ISegmentable3D)closedPlanar3D).GetPoints();
            }
            else if (closedPlanar3D is ICurvable3D)
            {
                List<ICurve3D> curves = ((ICurvable3D)closedPlanar3D).GetCurves();
                points = curves?.ConvertAll(x => x?.GetStart());
            }

            if (points == null)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_LOOP_UNSUPPORTED", "Only segmentable/curvable planar loops can be sent to OCCT.", sourceIndex);
                return false;
            }

            points.RemoveAll(x => x == null);

            if (points.Count > 1 && points[0].Distance(points[points.Count - 1]) <= options.Tolerance)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_LOOP_TOO_SMALL", "Loop has fewer than three unique points.", sourceIndex);
                return false;
            }

            foreach (Point3D point in points)
            {
                coordinates.Add(point.X);
                coordinates.Add(point.Y);
                coordinates.Add(point.Z);
            }

            loopPointCounts.Add(points.Count);
            return true;
        }
    }
}
