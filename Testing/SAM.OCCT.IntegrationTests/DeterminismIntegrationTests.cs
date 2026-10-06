// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated determinism lock (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §J.8): the full
    /// solve - through the native MakerVolume resolve - must produce identical resolved faces AND an
    /// identical <see cref="ClosureSignature3D"/> across two runs of the same input and parameters. This is
    /// the invariant AutoTune3D (Phase 5e) relies on so that changing only an escalated culprit's maxExtend
    /// between rounds moves only that culprit. The unit-level lock (<c>DeterminismTests</c>) covers the
    /// managed stage native-free; this covers the resolved geometry + signature the native kernel produces.
    /// </summary>
    public class DeterminismIntegrationTests
    {
        /// <summary>A watertight 4x4x3 m box with a full-height partition at x = 2 (7 faces): the raw kernel
        /// closes it into two rooms, so the signature is well-defined and stable.</summary>
        private static List<Face3D> PartitionedBox()
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(2, 0, 0), new Point3D(2, 4, 0), new Point3D(2, 4, 3), new Point3D(2, 0, 3)),
            };
        }

        private static List<Point3D> Boundary(Face3D face3D)
        {
            return (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
        }

        private static Panel3DSnapSolver Solve(List<Face3D> face3Ds, bool forceManaged)
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds) { ForceManagedPipeline = forceManaged };
            solver.Execute(new OcctBuildOptions());
            return solver;
        }

        private static void AssertIdenticalResolvedFacesAndSignature(Panel3DSnapSolver first, Panel3DSnapSolver second)
        {
            // Resolved faces: identical count and identical vertex sequences, index for index.
            Assert.Equal(first.ResolvedFace3Ds.Count, second.ResolvedFace3Ds.Count);
            Assert.NotEmpty(first.ResolvedFace3Ds);
            for (int i = 0; i < first.ResolvedFace3Ds.Count; i++)
            {
                List<Point3D> a = Boundary(first.ResolvedFace3Ds[i]);
                List<Point3D> b = Boundary(second.ResolvedFace3Ds[i]);
                Assert.Equal(a.Count, b.Count);
                for (int k = 0; k < a.Count; k++)
                {
                    Assert.True(a[k].Distance(b[k]) < 1e-9,
                        $"Face {i} vertex {k} differs between runs: {a[k]} vs {b[k]}");
                }
            }

            // Signature: identical on every axis (including the Phase-5a sliver term).
            Assert.NotNull(first.Signature);
            Assert.NotNull(second.Signature);
            Assert.Equal(first.Signature.CellCount, second.Signature.CellCount);
            Assert.Equal(first.Signature.NakedEdgeCount, second.Signature.NakedEdgeCount);
            Assert.Equal(first.Signature.FaceCount, second.Signature.FaceCount);
            Assert.Equal(first.Signature.DroppedCount, second.Signature.DroppedCount);
            Assert.Equal(first.Signature.SliverCellCount, second.Signature.SliverCellCount);
            Assert.Equal(first.Signature.TotalVolume, second.Signature.TotalVolume, 6);
        }

        [SkippableFact]
        public void Execute_SameInputTwice_RawPathProducesIdenticalResolvedFacesAndSignature()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act
            Panel3DSnapSolver first = Solve(PartitionedBox(), forceManaged: false);
            Panel3DSnapSolver second = Solve(PartitionedBox(), forceManaged: false);

            // Assert
            AssertIdenticalResolvedFacesAndSignature(first, second);
        }

        [SkippableFact]
        public void Execute_SameInputTwice_ManagedPathProducesIdenticalResolvedFacesAndSignature()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act - ForceManagedPipeline exercises the managed clean/extend/resolve path, whose
            // signature Phase 5a newly populates; it too must be deterministic.
            Panel3DSnapSolver first = Solve(PartitionedBox(), forceManaged: true);
            Panel3DSnapSolver second = Solve(PartitionedBox(), forceManaged: true);

            // Assert
            AssertIdenticalResolvedFacesAndSignature(first, second);
        }
    }
}
