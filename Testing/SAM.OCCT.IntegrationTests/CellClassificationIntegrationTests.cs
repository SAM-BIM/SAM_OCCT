// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// SAM.Core.OCCT and SAM.Geometry.OCCT both declare a static Create class.
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated coverage for Phase 7b (docs/P6_ARCHITECTURE_REVIEW.md §P, sub-step 7b):
    /// <see cref="CellClassifier.ClassifyCells"/> against real resolved geometry - a genuine sliver is
    /// reported and excluded, a genuinely enclosed room classifies Interior, and the flat golden-master
    /// fixture's 22 real rooms all classify Interior (matching the plan's Phase 7 acceptance: "exterior
    /// cell(s) excluded; sliver cells reported, not spaced"). Never asserts a different
    /// <see cref="ClosureSignature3D"/> than the existing golden-master suite already locks.
    /// </summary>
    public class CellClassificationIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        /// <summary>A watertight 4x4x3 m box split by a partition at x = d: a large room (volume (4-d)*4*3)
        /// and, when d is hairline-thin, a sliver room (volume d*4*3) below the default MinCellVolume
        /// (0.05 m3). Same 7-face pattern as <c>DeterminismIntegrationTests.PartitionedBox</c>.</summary>
        private static List<Face3D> PartitionedBox(double d)
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(d, 0, 0), new Point3D(d, 4, 0), new Point3D(d, 4, 3), new Point3D(d, 0, 3)),
            };
        }

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;
                    case AdjacencyCluster adjacencyCluster:
                        result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;
                    case Panel panel:
                        result.Add(panel);
                        break;
                }
            }

            return result
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
        }

        [SkippableFact]
        public void ClassifyCells_HandBuiltSliverAndRealRoom_SliverDiagnosedRealRoomInterior()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - analytically known cell metadata (axis-aligned box slices, so volume/centre are
            // exact) for a large real room and a hairline sliver, sitting either side of the same partition.
            const double d = 0.003; // 3 mm: above OCCT's fuzzy tolerance, well below MinCellVolume (0.05 m3)
            const double minCellVolume = 0.05;
            List<Face3D> resolvedFace3Ds = PartitionedBox(d);

            List<SolverCell> cells = new List<SolverCell>
            {
                new SolverCell(0, volume: d * 4 * 3, center: new Point3D(d / 2.0, 2, 1.5), shell: null),
                new SolverCell(1, volume: (4 - d) * 4 * 3, center: new Point3D(d + (4 - d) / 2.0, 2, 1.5), shell: null),
            };

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            IReadOnlyList<CellRole> roles = CellClassifier.ClassifyCells(cells, resolvedFace3Ds, minCellVolume, new OcctBuildOptions(), diagnostics);

            // Assert
            Assert.Equal(2, roles.Count);
            Assert.Equal(CellRole.Sliver, roles[0]);
            Assert.Equal(CellRole.Interior, roles[1]);

            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.SliverCell && x.Severity == OcctDiagnosticSeverity.Warning);
        }

        [SkippableFact]
        public void ClassifyCells_RawPathTwoRoomBox_BothRoomsClassifyInterior()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act - a genuine two-room box (no sliver): both cells' centres must fall inside the
            // box's own outer envelope.
            Panel3DSnapSolver solver = new Panel3DSnapSolver(PartitionedBox(2.0));
            solver.Execute(new OcctBuildOptions());

            Assert.Equal(2, solver.Cells.Count);

            IReadOnlyList<CellRole> roles = CellClassifier.ClassifyCells(solver.Cells, solver.ResolvedFace3Ds, solver.MinCellVolume, new OcctBuildOptions(), solver.Diagnostics);

            // Assert
            Assert.Equal(2, roles.Count);
            Assert.All(roles, x => Assert.Equal(CellRole.Interior, x));
        }

        [SkippableFact]
        public void ClassifyCells_FlatFixtureRawPath_All22RoomsClassifyInterior()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            // Arrange - solve via the analytical entry point (the same one the pinned golden master uses),
            // so bucket/weight/maxExtend are the fixture's own derived values, not solver defaults.
            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);
            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);
            Assert.NotNull(solved);
            Assert.Empty(nakedPoint3Ds);

            List<Face3D> face3Ds = solved
                .Where(x => x != null && x.PanelType != PanelType.Air)
                .Select(x => x.GetFace3D())
                .Where(x => x != null && x.IsValid())
                .ToList();

            // Act - independently decode the solved panels into a cell complex (matching
            // GoldenMasterIntegrationTests.CaptureSignature's own re-decode), then classify.
            OcctBuildOptions options = new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);
            List<SolverCell> cells;
            try
            {
                Assert.NotNull(result);
                Assert.True(result.NativeAvailable);
                Assert.Equal(22, result.Cells.Count);
                cells = result.Cells.Select((x, idx) => new SolverCell(idx, x.Volume, x.Center, x.Shell)).ToList();
            }
            finally
            {
                result.Dispose();
            }

            IReadOnlyList<CellRole> roles = CellClassifier.ClassifyCells(cells, face3Ds, minCellVolume: 0.05, options);

            // Assert - every real room in this golden-master fixture is a genuine, fully-enclosed space.
            Assert.Equal(22, roles.Count);
            Assert.All(roles, x => Assert.Equal(CellRole.Interior, x));
        }
    }
}
