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
    /// Unit tests for the <see cref="Panel3DSnapSolver.InputAlreadyClean"/> condition-only path
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §2). Pure-managed (no native resolve, StopAfterClean). Proves the
    /// stage selection: with the flag OFF, Stage A snap/normalize/merge runs (two overlapping coplanar tiles
    /// merge into one clean face); with it ON, Stage A is SKIPPED (the tiles pass through unchanged, an
    /// identity source map is built, no clean records are produced, a CLEAN-SKIPPED diagnostic is emitted),
    /// while the frames/groups are still clustered for reporting.
    /// </summary>
    public class InputAlreadyCleanTests
    {
        /// <summary>A horizontal tile [x0,x1] x [y0,y1] at z=0.</summary>
        private static Face3D Tile(double x0, double x1, double y0, double y1)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x0, y1, 0));
        }

        /// <summary>Two overlapping coplanar tiles that Stage A's coplanar union merges into one.</summary>
        private static List<Face3D> OverlappingTiles()
        {
            return new List<Face3D> { Tile(0, 4, 0, 4), Tile(2, 6, 0, 4) };
        }

        private static Panel3DSnapSolver Solver(bool inputAlreadyClean)
        {
            return new Panel3DSnapSolver(OverlappingTiles())
            {
                StopAfterClean = true,
                InputAlreadyClean = inputAlreadyClean
            };
        }

        [Fact]
        public void Execute_InputAlreadyCleanFalse_RunsStageAAndMergesOverlap()
        {
            // Arrange
            Panel3DSnapSolver solver = Solver(inputAlreadyClean: false);

            // Act
            solver.Execute(null);

            // Assert - Stage A ran: the two overlapping coplanar tiles merged into a single clean face, and
            // clean records were captured.
            Assert.Single(solver.CleanFace3Ds);
            Assert.DoesNotContain(solver.Diagnostics.All, x => x.Message.Contains("CLEAN3D_SKIPPED"));
        }

        [Fact]
        public void Execute_InputAlreadyCleanTrue_SkipsStageAAndKeepsInputFaces()
        {
            // Arrange
            Panel3DSnapSolver solver = Solver(inputAlreadyClean: true);

            // Act
            solver.Execute(null);

            // Assert - Stage A skipped: BOTH input tiles pass through unchanged (no merge), no clean records,
            // and the CLEAN-SKIPPED diagnostic records the bypass.
            Assert.Equal(2, solver.CleanFace3Ds.Count);
            Assert.Empty(solver.CleanRecords);
            Assert.Contains(solver.Diagnostics.All, x => x.Message.Contains("CLEAN3D_SKIPPED"));
        }

        [Fact]
        public void Execute_InputAlreadyCleanTrue_BuildsIdentitySourceMap()
        {
            // Arrange
            Panel3DSnapSolver solver = Solver(inputAlreadyClean: true);

            // Act
            solver.Execute(null);

            // Assert - each input source maps 1:1 to its own clean face (identity), so the handoff carries exact
            // provenance without a second clean.
            Assert.Equal(new List<FaceKey> { new FaceKey(0) }, solver.SourceMap.FacesOf(0));
            Assert.Equal(new List<FaceKey> { new FaceKey(1) }, solver.SourceMap.FacesOf(1));
        }

        [Fact]
        public void Execute_InputAlreadyCleanTrue_StillClustersFramesAndGroupsForReporting()
        {
            // Arrange - the two tiles are one flat level; grouping is clustered for reporting only (no move).
            Panel3DSnapSolver solver = Solver(inputAlreadyClean: true);

            // Act
            solver.Execute(null);

            // Assert - the level frame/group are reported even though no normalization ran.
            Assert.Single(solver.LevelFrames);
            Assert.Single(solver.LevelGroups);
        }
    }
}
