// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    public static partial class Modify
    {
        /// <summary>
        /// Analytical entry point for the true 3D panel solver. Wraps
        /// <see cref="Panel3DSnapSolver"/>: skips air panels, derives the capture width from each
        /// panel's real construction thickness, snaps lower-weight panels onto higher-weight
        /// backers in full 3D, and resolves junctions through the native OCCT kernel.
        /// </summary>
        /// <param name="panels">Panels to solve. Not modified; new panels are returned.</param>
        /// <param name="nakedPoint3Ds">Locations of naked (free) boundary edges reported by the validator.</param>
        /// <param name="diagnostics">Coded diagnostics describing the solve.</param>
        /// <param name="weights">Optional per-panel backer weights (aligned with the non-air panel order); null uses the default.</param>
        /// <param name="minBucketSize">Lower bound on the capture half-width, in metres.</param>
        /// <param name="thicknessFactor">Fraction of construction thickness used as the capture half-width.</param>
        /// <param name="options">OCCT build options (distance/fuzzy/glue tolerances).</param>
        /// <returns>The resolved panels, or null when no usable panels were supplied.</returns>
        public static List<Panel> Solve3D(
            this IEnumerable<Panel> panels,
            out List<Point3D> nakedPoint3Ds,
            out List<string> diagnostics,
            IEnumerable<double> weights = null,
            double minBucketSize = 0.2,
            double thicknessFactor = 0.6,
            OcctBuildOptions options = null)
        {
            nakedPoint3Ds = new List<Point3D>();
            diagnostics = new List<string>();

            // Air panels carry no real surface to snap to; drop them up front (Query.Air analogue).
            List<Panel> panels_Temp = panels?.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            if (panels_Temp == null || panels_Temp.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_SOLVE3D_INPUT_EMPTY: No non-air panels were supplied.");
                return null;
            }

            List<Face3D> face3Ds = new List<Face3D>();
            List<double> bucketSizes = new List<double>();
            List<Panel> sources = new List<Panel>();
            foreach (Panel panel in panels_Temp)
            {
                Face3D face3D = panel.GetFace3D();
                if (face3D == null || !face3D.IsValid())
                {
                    continue;
                }

                face3Ds.Add(face3D);
                bucketSizes.Add(BucketSize(panel, minBucketSize, thicknessFactor));
                sources.Add(panel);
            }

            if (face3Ds.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_SOLVE3D_INPUT_EMPTY: No valid panel geometry was supplied.");
                return null;
            }

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds, bucketSizes, weights);
            solver.Execute(options);

            nakedPoint3Ds = solver.NakedEdgePoint3Ds ?? new List<Point3D>();

            List<Face3D> resolved = solver.ResolvedFace3Ds;
            if (resolved == null || resolved.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_SOLVE3D_NO_RESULT: The solver produced no resolved faces.");
                return new List<Panel>();
            }

            List<Panel> result = new List<Panel>();
            foreach (Face3D face3D in resolved)
            {
                Panel source = NearestSource(face3D, sources, options?.Tolerance ?? Tolerance.Distance);
                if (source == null)
                {
                    continue;
                }

                Panel panel = global::SAM.Analytical.Create.Panel(source.Construction, source.PanelType, face3D);
                if (panel != null)
                {
                    result.Add(panel);
                }
            }

            diagnostics.Add(string.Format(
                "SAM_OCCT_SOLVE3D_RESULT: Solved {0} panel(s) into {1} resolved panel(s); nativeResolved={2}; {3} naked edge(s).",
                face3Ds.Count,
                result.Count,
                solver.NativeResolved,
                nakedPoint3Ds.Count));

            return result;
        }

        /// <summary>Capture half-width from construction thickness: thickness * factor, floored at a minimum.</summary>
        private static double BucketSize(Panel panel, double minBucketSize, double thicknessFactor)
        {
            double thickness = panel?.Construction?.GetThickness() ?? double.NaN;
            if (double.IsNaN(thickness) || thickness <= 0)
            {
                return minBucketSize;
            }

            return System.Math.Max(minBucketSize, thickness * thicknessFactor);
        }

        /// <summary>
        /// Finds the source panel that best explains a resolved face: parallel supporting planes,
        /// then the source whose centroid sits inside the resolved face's bounding box. Used to
        /// carry construction/type forward, since native boolean resolution loses 1:1 source mapping.
        /// </summary>
        private static Panel NearestSource(Face3D face3D, List<Panel> sources, double tolerance)
        {
            Plane plane = face3D?.GetPlane();
            if (plane == null || sources == null || sources.Count == 0)
            {
                return sources?.FirstOrDefault();
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            Panel best = null;
            double bestDistance = double.MaxValue;
            foreach (Panel source in sources)
            {
                Face3D sourceFace3D = source?.GetFace3D();
                Plane sourcePlane = sourceFace3D?.GetPlane();
                if (sourcePlane == null)
                {
                    continue;
                }

                if (System.Math.Abs(plane.Normal.Unit.DotProduct(sourcePlane.Normal.Unit)) < 0.99)
                {
                    continue;
                }

                Point3D centroid = sourceFace3D.GetBoundingBox()?.GetCentroid();
                if (centroid == null)
                {
                    continue;
                }

                double distance = plane.Distance(centroid);
                if (boundingBox3D != null && Within(boundingBox3D, centroid, tolerance))
                {
                    distance -= 1.0; // prefer a source whose centroid actually lands on the resolved face
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = source;
                }
            }

            return best ?? sources.FirstOrDefault();
        }

        private static bool Within(BoundingBox3D boundingBox3D, Point3D point3D, double tolerance)
        {
            Point3D min = boundingBox3D.Min;
            Point3D max = boundingBox3D.Max;
            return point3D.X >= min.X - tolerance && point3D.X <= max.X + tolerance
                && point3D.Y >= min.Y - tolerance && point3D.Y <= max.Y + tolerance
                && point3D.Z >= min.Z - tolerance && point3D.Z <= max.Z + tolerance;
        }
    }
}
