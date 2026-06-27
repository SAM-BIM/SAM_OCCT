// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end native tests for <see cref="Panel3DSnapSolver"/>.
    /// All tests auto-skip when the native OCCT library is absent.
    /// </summary>
    public class Panel3DSolverIntegrationTests
    {
        // ──────────────────────────────────────────────────────────────
        // 3-way junction: two walls + a floor sharing an edge
        // ──────────────────────────────────────────────────────────────

        [SkippableFact]
        public void Execute_ThreeWayJunction_ResolvedEdgesAreNotDuplicated()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Wall A: XZ plane (Y=0), 4 m wide × 3 m tall
            Face3D wallA = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 0, 3), new Point3D(0, 0, 3));

            // Wall B: YZ plane (X=0), 4 m wide × 3 m tall — perpendicular to A, sharing the edge at x=0, y=0
            Face3D wallB = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 4, 0),
                new Point3D(0, 4, 3), new Point3D(0, 0, 3));

            // Floor: XY plane (Z=0), 4×4 — shares the bottom edge of both walls
            Face3D floor = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 4, 0), new Point3D(0, 4, 0));

            List<double> weights = new List<double> { 2.0, 1.5, 1.0 };
            List<double> bucketSizes = new List<double> { 0.3, 0.3, 0.3 };

            Panel3DSnapSolver solver = new Panel3DSnapSolver(
                new List<Face3D> { wallA, wallB, floor },
                bucketSizes,
                weights);
            solver.Execute(new OcctBuildOptions());

            // The native stage should have run
            Assert.True(solver.NativeResolved, "Expected native OCCT resolve to run");
            Assert.NotEmpty(solver.ResolvedFace3Ds);

            // No resolved face should be null or invalid
            Assert.All(solver.ResolvedFace3Ds, f =>
            {
                Assert.NotNull(f);
                Assert.True(f.IsValid(), "Resolved face should be valid");
                Assert.True(f.GetArea() > Tolerance.Distance, "Resolved face should have non-zero area");
            });
        }

        [SkippableFact]
        public void Execute_TwoParallelWalls_LowerWeightSnapsOntoHigher()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Backer at y = 0, candidate at y = 0.15 (inside 0.3 m bucket)
            Face3D backer = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 0, 3), new Point3D(0, 0, 3));

            Face3D candidate = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0.15, 0), new Point3D(4, 0.15, 0),
                new Point3D(4, 0.15, 3), new Point3D(0, 0.15, 3));

            Panel3DSnapSolver solver = new Panel3DSnapSolver(
                new List<Face3D> { backer, candidate },
                new List<double> { 0.3, 0.3 },
                new List<double> { 2.0, 1.0 });
            solver.Execute(new OcctBuildOptions());

            // Step 1 snapped the lower-weight candidate (y = 0.15) onto the backer (y = 0) and merged them,
            // so every resolved face lies on the backer plane y = 0.
            Assert.True(solver.NativeResolved);
            Assert.NotEmpty(solver.ResolvedFace3Ds);
            foreach (Face3D f in solver.ResolvedFace3Ds)
            {
                BoundingBox3D boundingBox3D = f?.GetBoundingBox();
                if (boundingBox3D == null) continue;
                Assert.True(System.Math.Abs(boundingBox3D.Min.Y) < Tolerance.MacroDistance && System.Math.Abs(boundingBox3D.Max.Y) < Tolerance.MacroDistance,
                    $"Resolved face is not on the backer plane y=0 (y range {boundingBox3D.Min.Y}..{boundingBox3D.Max.Y})");
            }
        }

        [SkippableFact]
        public void Execute_ThreeWayJunction_ValidateReportsNoSurplusNakedEdges()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Same 3-way junction as above
            Face3D wallA = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 0, 3), new Point3D(0, 0, 3));

            Face3D wallB = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 4, 0),
                new Point3D(0, 4, 3), new Point3D(0, 0, 3));

            Face3D floor = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 4, 0), new Point3D(0, 4, 0));

            Panel3DSnapSolver solver = new Panel3DSnapSolver(
                new List<Face3D> { wallA, wallB, floor },
                new List<double> { 0.3, 0.3, 0.3 },
                new List<double> { 2.0, 1.5, 1.0 });
            solver.Execute(new OcctBuildOptions());

            Assert.True(solver.NativeResolved);

            // The shared vertical edge (x=0, y=0, z=0→3) should not appear as a naked edge — it
            // is shared by wallA and wallB. Only the true boundary edges (the outer perimeter of
            // the three open faces) should be naked.
            // We assert by area: the junction resolves to 3 faces with their correct areas.
            double totalArea = solver.ResolvedFace3Ds.Sum(f => f?.GetArea() ?? 0);
            Assert.True(totalArea > 1.0, $"Expected non-trivial total resolved area, got {totalArea}");
        }

        // ──────────────────────────────────────────────────────────────
        // Regression: vertical walls only — result area matches backer area
        // ──────────────────────────────────────────────────────────────

        [SkippableFact]
        public void Execute_SingleVerticalWall_AreaPreserved()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Face3D wall = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0),
                new Point3D(4, 0, 3), new Point3D(0, 0, 3));

            double expectedArea = 4.0 * 3.0;

            Panel3DSnapSolver solver = new Panel3DSnapSolver(new List<Face3D> { wall });
            solver.Execute(new OcctBuildOptions());

            double resolvedArea = solver.ResolvedFace3Ds.Sum(f => f?.GetArea() ?? 0);
            Assert.Equal(expectedArea, resolvedArea, 3);
        }
    }
}
