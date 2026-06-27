// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Sections a closed shell by one or more planes using the OCCT engine in a single
        /// BOPAlgo_MakerVolume pass: the shell's boundary faces plus a bounded rectangular
        /// patch per plane (sized to the shell's bounding box) are fed to the native cell
        /// build. The resulting cells are the level shells, and the cell faces lying on a
        /// section plane - trimmed to the shell by OCCT - are the section Face3Ds.
        /// Returns null when the input is invalid or the native build produced no cells
        /// (e.g. the native library is unavailable); callers may then fall back.
        /// </summary>
        public static List<Shell> ShellSectionByPlanes(Shell shell, IEnumerable<Plane> planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, OcctBuildOptions options = null, double angleTolerance = Tolerance.Angle, double distanceTolerance = Tolerance.MacroDistance)
        {
            sectionFace3Ds = null;
            result = new OcctCellComplexResult();

            List<Face3D> shellFace3Ds = shell?.Face3Ds?.FindAll(x => x != null);
            if (shellFace3Ds == null || shellFace3Ds.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SECTION_INPUT_EMPTY", "No shell faces were supplied for plane sectioning.");
                return null;
            }

            List<Plane> planes_Temp = planes?.Where(x => x != null).ToList();
            if (planes_Temp == null || planes_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SECTION_INPUT_EMPTY", "No section planes were supplied.");
                return null;
            }

            BoundingBox3D boundingBox3D = shell.GetBoundingBox();
            if (boundingBox3D == null)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SECTION_INPUT_EMPTY", "Shell bounding box could not be computed.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            // Patch must overhang the shell so MakerVolume cuts cleanly through the boundary.
            double margin = Math.Max(1.0, boundingBox3D.Min.Distance(boundingBox3D.Max) * 0.1);

            List<Point3D> cornerPoint3Ds = boundingBox3D.GetPoints();
            List<Face3D> allFace3Ds = new List<Face3D>(shellFace3Ds);
            foreach (Plane plane in planes_Temp)
            {
                List<Point2D> point2Ds = cornerPoint3Ds?.ConvertAll(x => plane.Convert(x)).FindAll(x => x != null);
                if (point2Ds == null || point2Ds.Count == 0)
                {
                    continue;
                }

                Face3D patchFace3D = new Face3D(plane, new Rectangle2D(new BoundingBox2D(point2Ds, margin)));
                if (patchFace3D != null)
                {
                    allFace3Ds.Add(patchFace3D);
                }
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SECTION_INPUT", string.Format("Sectioning shell with {0} face(s) by {1} plane(s) in one OCCT cell build.", shellFace3Ds.Count, planes_Temp.Count));

            if (!Native.OcctCellComplexBuilder.TryBuild(allFace3Ds, options, result))
            {
                return null;
            }

            if (result.Cells == null || result.Cells.Count == 0)
            {
                return null;
            }

            // Cell faces lying on a section plane are the section faces. Interior section
            // faces are shared by two cells, so dedupe by the OCCT topology key.
            sectionFace3Ds = new List<Face3D>();
            HashSet<int> topologyKeys = new HashSet<int>();
            double minDotProduct = Math.Cos(angleTolerance);
            foreach (OcctCell cell in result.Cells)
            {
                if (cell?.Faces == null)
                {
                    continue;
                }

                foreach (OcctCellFace cellFace in cell.Faces)
                {
                    Face3D face3D = cellFace?.Face3D;
                    Plane facePlane = face3D?.GetPlane();
                    if (facePlane == null)
                    {
                        continue;
                    }

                    bool onSectionPlane = false;
                    foreach (Plane plane in planes_Temp)
                    {
                        if (Math.Abs(facePlane.Normal.DotProduct(plane.Normal)) < minDotProduct)
                        {
                            continue;
                        }

                        if (plane.Distance(facePlane.Origin) > distanceTolerance)
                        {
                            continue;
                        }

                        onSectionPlane = true;
                        break;
                    }

                    if (!onSectionPlane)
                    {
                        continue;
                    }

                    if (cellFace.TopologyKey != 0 && !topologyKeys.Add(cellFace.TopologyKey))
                    {
                        continue;
                    }

                    sectionFace3Ds.Add(face3D);
                }
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SECTION_BUILD_SUCCESS", string.Format("OCCT section produced {0} level shell(s) and {1} section face(s).", result.Cells.Count, sectionFace3Ds.Count));
            return result.Shells?.ToList();
        }
    }
}
