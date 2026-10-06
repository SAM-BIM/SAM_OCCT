// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated coverage for Phase 7a (docs/P6_ARCHITECTURE_REVIEW.md §P, sub-step 7a):
    /// <see cref="Panel3DSnapSolver.Cells"/> - the per-cell volume/centre/shell snapshot captured
    /// alongside <see cref="Panel3DSnapSolver.Signature"/> - must be populated, index-aligned with the
    /// signature's <see cref="ClosureSignature3D.CellVolumes"/>, and deterministic across repeat solves,
    /// on both the raw and managed paths. No solver geometry changes with this feature: these tests only
    /// read the new property, they never assert a different <see cref="ClosureSignature3D"/> than the
    /// existing determinism/golden-master suites already lock.
    /// </summary>
    public class SolverCellIntegrationTests
    {
        /// <summary>A watertight 4x4x3 m box with a full-height partition at x = 2 (7 faces): the kernel
        /// closes it into two rooms, so cell count/volume/centre are well-defined and stable.</summary>
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

        private static Panel3DSnapSolver Solve(List<Face3D> face3Ds, bool forceManaged)
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds) { ForceManagedPipeline = forceManaged };
            solver.Execute(new OcctBuildOptions());
            return solver;
        }

        private static void AssertCellsMatchSignature(Panel3DSnapSolver solver)
        {
            Assert.NotNull(solver.Signature);
            Assert.Equal(2, solver.Signature.CellCount);
            Assert.Equal(solver.Signature.CellCount, solver.Cells.Count);

            for (int i = 0; i < solver.Cells.Count; i++)
            {
                SolverCell cell = solver.Cells[i];
                Assert.Equal(i, cell.Index);
                Assert.Equal(solver.Signature.CellVolumes[i], cell.Volume);
                Assert.True(cell.Volume > 0, "Cell volume must be positive.");
                Assert.NotNull(cell.Center);
                Assert.NotNull(cell.Shell);

                // Sanity bound only (7a does not classify cells): the centre must lie within the input
                // box's overall extent (Phase 7b is where "centre lies inside its own cell" is proven via
                // Query.IsPointInside).
                Assert.InRange(cell.Center.X, -1e-6, 4 + 1e-6);
                Assert.InRange(cell.Center.Y, -1e-6, 4 + 1e-6);
                Assert.InRange(cell.Center.Z, -1e-6, 3 + 1e-6);
            }

            Assert.Equal(solver.Signature.TotalVolume, solver.Cells.Sum(x => x.Volume), 6);
        }

        [SkippableFact]
        public void Execute_RawPath_CellsPopulatedAndMatchSignature()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act
            Panel3DSnapSolver solver = Solve(PartitionedBox(), forceManaged: false);

            // Assert
            AssertCellsMatchSignature(solver);
        }

        [SkippableFact]
        public void Execute_ManagedPath_CellsPopulatedAndMatchSignature()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act - ForceManagedPipeline exercises FinalizeAndValidate's cell capture (both the
            // consolidation-rebuild-adopted and DecodeCellVolumes fallback legs feed the same Cells property).
            Panel3DSnapSolver solver = Solve(PartitionedBox(), forceManaged: true);

            // Assert
            AssertCellsMatchSignature(solver);
        }

        [SkippableFact]
        public void Execute_SameInputTwice_RawPathCellsAreDeterministic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act
            Panel3DSnapSolver first = Solve(PartitionedBox(), forceManaged: false);
            Panel3DSnapSolver second = Solve(PartitionedBox(), forceManaged: false);

            // Assert
            Assert.Equal(first.Cells.Count, second.Cells.Count);
            Assert.NotEmpty(first.Cells);
            for (int i = 0; i < first.Cells.Count; i++)
            {
                Assert.Equal(first.Cells[i].Index, second.Cells[i].Index);
                Assert.Equal(first.Cells[i].Volume, second.Cells[i].Volume, 9);
                Assert.True(first.Cells[i].Center.Distance(second.Cells[i].Center) < 1e-9,
                    $"Cell {i} centre differs between runs: {first.Cells[i].Center} vs {second.Cells[i].Center}");
            }
        }

        [SkippableFact]
        public void Execute_SameInputTwice_ManagedPathCellsAreDeterministic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act
            Panel3DSnapSolver first = Solve(PartitionedBox(), forceManaged: true);
            Panel3DSnapSolver second = Solve(PartitionedBox(), forceManaged: true);

            // Assert
            Assert.Equal(first.Cells.Count, second.Cells.Count);
            Assert.NotEmpty(first.Cells);
            for (int i = 0; i < first.Cells.Count; i++)
            {
                Assert.Equal(first.Cells[i].Index, second.Cells[i].Index);
                Assert.Equal(first.Cells[i].Volume, second.Cells[i].Volume, 9);
                Assert.True(first.Cells[i].Center.Distance(second.Cells[i].Center) < 1e-9,
                    $"Cell {i} centre differs between runs: {first.Cells[i].Center} vs {second.Cells[i].Center}");
            }
        }
    }
}
