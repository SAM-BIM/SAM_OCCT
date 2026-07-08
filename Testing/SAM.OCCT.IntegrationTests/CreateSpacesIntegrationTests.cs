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

// SAM.Analytical.Create, SAM.Core.Create, and SAM.Analytical.OCCT[.Solver].Create all declare a static
// Create class - alias every one this file needs.
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using SolverCreate = SAM.Analytical.OCCT.Solver.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated coverage for Phase 7c (docs/P6_ARCHITECTURE_REVIEW.md §P, sub-step 7c):
    /// <see cref="Create.Spaces"/> - the flat golden-master fixture yields exactly 22 spaces matching the
    /// reused <c>AdjacencyCluster</c> path on the same solved panels; the two-level-tilted raw solve
    /// (43 cells / 0 naked) produces spaces for its interior cells; the SAME fixture's pinned managed
    /// baseline (29 naked) is refused with diagnostics; and an input air panel passes through into the
    /// analytical output without becoming (or splitting) a Space. Never asserts a different
    /// <see cref="ClosureSignature3D"/> than the existing golden-master suite already locks.
    /// </summary>
    public class CreateSpacesIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

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
        public void Spaces_FlatFixtureRawPath_Yields22SpacesMatchingAdjacencyCluster()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            // Arrange
            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            // Act
            AdjacencyCluster spacesCluster = SolverCreate.Spaces(panels, out SolverDiagnostics diagnostics);

            // Assert - matches the pinned raw golden master (22 cells / 0 naked).
            Assert.NotNull(spacesCluster);
            List<Space> spaces = spacesCluster.GetSpaces();
            Assert.Equal(22, spaces.Count);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.SpacesRefused);

            // P2: Create.Spaces now sources its topology from the complex the SOLVER adopted
            // (report.ResolvedCellComplex - the topology source of truth), not a second rebuild. Verify the
            // cluster's panels are exactly the complex's unique cell faces that clear minArea, and that every
            // one of the 22 interior cells became a related space.
            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> solveDiagnostics, out _, out Solve3DReport report);
            Assert.Empty(nakedPoint3Ds);
            Assert.NotNull(report.ResolvedCellComplex);
            Assert.Equal(22, report.ResolvedCellComplex.Cells.Count);

            // Panels come from the adopted complex's unique cell faces (one per face that clears minArea);
            // normalisation may fuse a few, so the count is bounded by the complex's faces, not a fresh
            // rebuild's. Every one of the 22 interior cells must be a related space.
            int complexFaceCount = report.ResolvedCellComplex.Faces.Count(x => { double area = x.Face3D.GetArea(); return double.IsNaN(area) || area >= Tolerance.MacroDistance; });
            List<Panel> spacesPanels = spacesCluster.GetPanels();
            Assert.True(spacesPanels.Count > 0 && spacesPanels.Count <= complexFaceCount,
                string.Format("Expected 1 panel per adopted complex face (<= {0}), got {1}.", complexFaceCount, spacesPanels.Count));
            Assert.All(spaces, space => Assert.NotEmpty(spacesCluster.GetPanels(space)));

            // The reference rebuild agrees on the CELL count (22 spaces); its panel granularity can differ,
            // since it re-decodes the reconstructed panels rather than reusing the solver's adopted complex.
            OcctBuildOptions options = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            List<Panel> nonAirSolved = solved.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            AdjacencyCluster referenceCluster = AnalyticalOcctCreate.AdjacencyCluster(null, nonAirSolved, out OcctCellComplexResult referenceResult, null, options);
            try
            {
                Assert.NotNull(referenceCluster);
                Assert.Equal(referenceCluster.GetSpaces().Count, spaces.Count);
            }
            finally
            {
                referenceResult?.Dispose();
            }
        }

        [SkippableFact]
        public void Spaces_TwoLevelTiltedRawPath_ProducesSpacesForInteriorCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "two-level-tilted.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            // Arrange
            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            // Act - raw-first path, matching the pinned golden master (43 cells / 0 naked).
            AdjacencyCluster spacesCluster = SolverCreate.Spaces(panels, out SolverDiagnostics diagnostics, forceManagedPipeline: false);

            // Assert
            Assert.NotNull(spacesCluster);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.SpacesRefused);
            List<Space> spaces = spacesCluster.GetSpaces();
            Assert.Equal(43, spaces.Count);
        }

        [SkippableFact]
        public void Spaces_TwoLevelTiltedManagedPath_RefusesWithDiagnostics()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "two-level-tilted.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            // Arrange
            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            // Act - ForceManagedPipeline exercises the pinned 7-pre managed baseline (29 cells / 29 naked):
            // a degraded, non-closed result the closure gate must refuse rather than turn into 29 spaces.
            AdjacencyCluster spacesCluster = SolverCreate.Spaces(panels, out SolverDiagnostics diagnostics, forceManagedPipeline: true);

            // Assert
            Assert.Null(spacesCluster);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.SpacesRefused && x.Severity == OcctDiagnosticSeverity.Warning);
        }

        [SkippableFact]
        public void Spaces_AirPanelInput_PassesThroughUnchangedAndDoesNotBecomeASpace()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a single watertight 4x4x3 m room (one real space) plus one PanelType.Air panel (an
            // internal virtual boundary, e.g. a zoning marker) that must bypass solving/space-creation but
            // still surface, unchanged, in the analytical output.
            List<Face3D> boxFace3Ds = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)), // floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3)), // roof
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)),
            };

            List<Panel> panels = new List<Panel>
            {
                global::SAM.Analytical.Create.Panel(null, PanelType.Floor, boxFace3Ds[0]),
                global::SAM.Analytical.Create.Panel(null, PanelType.Roof, boxFace3Ds[1]),
                global::SAM.Analytical.Create.Panel(null, PanelType.Wall, boxFace3Ds[2]),
                global::SAM.Analytical.Create.Panel(null, PanelType.Wall, boxFace3Ds[3]),
                global::SAM.Analytical.Create.Panel(null, PanelType.Wall, boxFace3Ds[4]),
                global::SAM.Analytical.Create.Panel(null, PanelType.Wall, boxFace3Ds[5]),
            };

            Face3D airFace3D = TestGeometry.CreatePlanarFace(new Point3D(2, 0, 0), new Point3D(2, 4, 0), new Point3D(2, 4, 3), new Point3D(2, 0, 3));
            Panel airPanel = global::SAM.Analytical.Create.Panel(null, PanelType.Air, airFace3D);
            panels.Add(airPanel);

            // Act
            AdjacencyCluster spacesCluster = SolverCreate.Spaces(panels, out SolverDiagnostics diagnostics);

            // Assert
            Assert.NotNull(spacesCluster);
            List<Space> spaces = spacesCluster.GetSpaces();
            Assert.Single(spaces); // the one real room - the air panel does not fragment it or gain a space

            List<Panel> outputPanels = spacesCluster.GetPanels();
            Assert.Contains(outputPanels, x => x != null && x.Guid == airPanel.Guid && x.PanelType == PanelType.Air);
        }
    }
}
