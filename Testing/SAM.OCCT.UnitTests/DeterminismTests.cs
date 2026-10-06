// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Determinism lock (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §J.8): identical inputs and
    /// parameters must produce byte-identical managed geometry across two runs. This underpins AutoTune3D's
    /// "only the escalated culprit panels move" invariant (Phase 5e) - a full-pipeline re-run with unchanged
    /// parameters is a fixed point, so a round that changes only a culprit's maxExtend perturbs only that
    /// culprit. Exercised native-free via <see cref="Panel3DSnapSolver.StopAfterClean"/>, which returns the
    /// managed Stage-A clean faces before any native resolve.
    /// </summary>
    public class DeterminismTests
    {
        /// <summary>A 4x4x3 m box (6 faces) plus an interior partition at x = 2 - enough for the managed
        /// snap stage (opposed-partition handling, coplanar merges, cap normalization) to do real work.</summary>
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

        private static Panel3DSnapSolver RunClean(List<Face3D> face3Ds)
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds) { StopAfterClean = true };
            solver.Execute();
            return solver;
        }

        [Fact]
        public void Execute_SameInputTwice_ProducesIdenticalCleanFaces()
        {
            // Arrange - two independent runs over the SAME input geometry and default parameters.
            Panel3DSnapSolver first = RunClean(PartitionedBox());
            Panel3DSnapSolver second = RunClean(PartitionedBox());

            // Assert - identical face count and identical vertex sequences, index for index.
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
        }

        [Fact]
        public void Execute_SameInputTwice_ProducesIdenticalSourceMap()
        {
            // Arrange
            Panel3DSnapSolver first = RunClean(PartitionedBox());
            Panel3DSnapSolver second = RunClean(PartitionedBox());

            // Assert - the source attribution (which input maps to which clean face) is stable across runs.
            List<int> firstSources = first.SourceMap.Sources.OrderBy(x => x).ToList();
            List<int> secondSources = second.SourceMap.Sources.OrderBy(x => x).ToList();
            Assert.Equal(firstSources, secondSources);

            foreach (int source in firstSources)
            {
                List<int> a = first.SourceMap.FacesOf(source).Select(x => x.Value).OrderBy(x => x).ToList();
                List<int> b = second.SourceMap.FacesOf(source).Select(x => x.Value).OrderBy(x => x).ToList();
                Assert.Equal(a, b);
            }
        }
    }
}
