// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Builders for the contact-aware void guard tests (design rev 5, controls A, B, C, T1, T2, W, F). Walls stand in the XZ
    /// plane (a vertical wall along X, 3 m tall); the sign of the normal is chosen by the winding. Two walls with opposite
    /// signs are an anti-parallel pair, as junction-snapped skins of one stepped wall are.
    /// </summary>
    internal static class ContactFixtures
    {
        /// <summary>The wall pair of Softthouse D203 (#251 and #250): skins 0.324 m apart whose certified slabs touch exactly.</summary>
        public const double YThin = 3.179;
        public const double YThick = 3.503;
        public const double GThin = 0.1;
        public const double GThick = 0.548;
        public const double YMid = (YThin + YThick) / 2;
        public const double Run = 3.0;
        public const double Height = 3.0;

        public static readonly double Angle = 5 * (System.Math.PI / 180);

        /// <summary>The definite region intersection the analytical adapter supplies (<c>Query.DefiniteIntersection</c>).</summary>
        public static readonly Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> DefiniteIntersection =
            (a, b, tolerance) => SAM.Analytical.Query.DefiniteIntersection(a, b, tolerance);

        /// <summary>A polygon of the wall frame (x along the run, z up), counter-clockwise, standing at <paramref name="y"/>.</summary>
        public static Face3D Wall(double y, bool normalPlusY, params (double x, double z)[] xz)
        {
            IEnumerable<(double x, double z)> ordered = normalPlusY ? Enumerable.Reverse(xz) : xz;
            return TestGeometry.CreatePlanarFace(ordered.Select(p => new Point3D(p.x, y, p.z)).ToArray());
        }

        public static Face3D Box(double y, bool normalPlusY, double x0, double x1, double z0 = 0, double z1 = Height)
        {
            return Wall(y, normalPlusY, (x0, z0), (x1, z0), (x1, z1), (x0, z1));
        }

        /// <summary>A wall box whose y runs linearly from <c>yCentre - tilt</c> at <paramref name="x0"/> to <c>yCentre + tilt</c> at <paramref name="x1"/> (planar, vertical).</summary>
        public static Face3D TiltedBox(double yCentre, double tilt, bool normalPlusY, double x0, double x1)
        {
            double y0 = yCentre - tilt;
            double y1 = yCentre + tilt;
            Point3D[] points = { new Point3D(x0, y0, 0), new Point3D(x1, y1, 0), new Point3D(x1, y1, Height), new Point3D(x0, y0, Height) };
            return TestGeometry.CreatePlanarFace(normalPlusY ? Enumerable.Reverse(points).ToArray() : points);
        }

        /// <summary>A wall box with one rectangular opening (an inner loop).</summary>
        public static Face3D BoxWithOpening(double y, bool normalPlusY, double x0, double x1, double ox0, double ox1, double oz0, double oz1)
        {
            Func<(double x, double z)[], Polygon3D> loop = xz => new Polygon3D((normalPlusY ? Enumerable.Reverse(xz) : xz).Select(p => new Point3D(p.x, y, p.z)).ToList());
            Polygon3D outer = loop(new[] { (x0, 0.0), (x1, 0.0), (x1, Height), (x0, Height) });
            Polygon3D inner = loop(new[] { (ox0, oz0), (ox1, oz0), (ox1, oz1), (ox0, oz1) });
            return Face3D.Create(new List<IClosedPlanar3D> { outer, inner });
        }

        /// <summary>
        /// The record of a panel extracted as <paramref name="extracted"/>: frame = its plane, <paramref name="origin"/> (default the
        /// extracted face) and <paramref name="support"/> (default the extracted face), all expressed in that frame.
        /// </summary>
        public static ContactEvidence Evidence(Face3D extracted, double g, IEnumerable<Face3D> support = null, IEnumerable<Face3D> origin = null, Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> intersect = null)
        {
            Plane plane = extracted.GetPlane();
            Func<Face3D, Face2D> toFrame = x => plane.Convert(x);
            return new ContactEvidence(
                plane,
                g,
                (support ?? new[] { extracted }).Select(toFrame),
                (origin ?? new[] { extracted }).Select(toFrame),
                intersect ?? DefiniteIntersection);
        }

        public static SnappedPanel Skin(int index, Face3D current, ContactEvidence evidence, double bucketSize = 0.4)
        {
            return new SnappedPanel(index, current, weight: 1, bucketSize: bucketSize, maxExtension: 0.5, consolidationRange: 0.0, evidence);
        }

        /// <summary>The D203 pair: #251 (thin, y 3.179) and #250 (thick, y 3.503), full records, normals opposite.</summary>
        public static (SnappedPanel thin, SnappedPanel thick) D203Pair(bool thinFacesPlusY = false, double gThick = GThick)
        {
            Face3D thin = Box(YThin, thinFacesPlusY, 0, Run);
            Face3D thick = Box(YThick, !thinFacesPlusY, 0, Run);
            return (Skin(0, thin, Evidence(thin, GThin)), Skin(1, thick, Evidence(thick, gThick)));
        }

        /// <summary>Stage A as <c>SnapStage.Clean</c> runs it up to the weighted fixed point: the opposed pass, then the weighted snap.</summary>
        public static void RunStageA(List<SnappedPanel> panels, out SolverDiagnostics diagnostics, out List<CleanRecord> records)
        {
            diagnostics = new SolverDiagnostics();
            records = new List<CleanRecord>();
            ToleranceBudget tolerances = new ToleranceBudget();
            Panel3DSnapSolver.SnapOpposedPartitions(panels, tolerances.Angle, tolerances.Distance, diagnostics, records);
            SnapStage.SnapToFixedPoint(panels, tolerances, alignColinearOffset: 0.3, diagnostics, SnapStage.MaxSnapIterations, records);
        }
    }
}
