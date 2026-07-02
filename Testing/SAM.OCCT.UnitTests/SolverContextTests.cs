// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="SolverContext"/> - the shared stage carrier that keys per-source
    /// parameters by source identity (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §G, Phase 2).
    /// Pure-managed.
    /// </summary>
    public class SolverContextTests
    {
        private static Face3D Quad()
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 1), new Point3D(0, 0, 1));
        }

        [Fact]
        public void Accessors_InRange_ReturnKeyedValues()
        {
            SolverContext context = new SolverContext(
                new List<Face3D> { Quad(), Quad() },
                weights: new List<double> { 2.0, 3.0 },
                bucketSizes: new List<double> { 0.2, 0.4 },
                maxExtensions: new List<double> { 0.5, 1.5 });

            Assert.Equal(2, context.SourceCount);
            Assert.Equal(3.0, context.WeightOf(1), 6);
            Assert.Equal(0.2, context.BucketSizeOf(0), 6);
            Assert.Equal(1.5, context.MaxExtensionOf(1), 6);
        }

        [Fact]
        public void Accessors_OutOfRange_FallBackToDefaults()
        {
            SolverContext context = new SolverContext(new List<Face3D> { Quad() }, null, null, null);

            Assert.Equal(Panel3DSnapSolver.DEFAULT_Weight, context.WeightOf(5), 6);
            Assert.Equal(Panel3DSnapSolver.DEFAULT_BucketSize, context.BucketSizeOf(5), 6);
            Assert.Equal(Panel3DSnapSolver.DEFAULT_MaxExtension, context.MaxExtensionOf(5), 6);
        }

        [Fact]
        public void Constructor_ProvidesNonNullMapDiagnosticsAndTolerances()
        {
            SolverContext context = new SolverContext(null, null, null, null);

            Assert.NotNull(context.SourceMap);
            Assert.NotNull(context.Diagnostics);
            Assert.NotNull(context.Tolerances);
            Assert.Equal(0, context.SourceCount);
        }
    }
}
