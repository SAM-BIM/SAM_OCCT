// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using GeometryOcctCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Sew-before-build under-split regression on a real two-storey house (2026-10-13 investigation).
    /// <para>
    /// The fixture holds the 23 mid-surface panels a GH script derived from the house's wall, slab, ceiling, roof
    /// and gable solids (metres; the UF slab stair opening is filled as in the original run), plus 11 seed spaces:
    /// the 10 labelled rooms and the attic.
    /// </para>
    /// <para>
    /// With SAMOCCT.CreateAdjacencyCluster's recipe (SewBeforeBuild) the native sew path used to merge rooms
    /// across T-junction partitions and drop the UF east bay and the attic: 4 cells and a "successful" result.
    /// The correct complex has 11 cells, one per room plus the attic.
    /// </para>
    /// </summary>
    public class HouseUnderSplitIntegrationTests
    {
        private const double ExpectedTotalVolume = 564.1; // m3, mid-plane cells, stair opening filled

        private static readonly string FixturePath = Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "House-Regression", "House-TwoStorey-23panels.sam");

        private static OcctBuildOptions GrasshopperRecipe()
        {
            // Exactly the options SAMOCCTCreateAdjacencyCluster passes to the rebuild path.
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, MergeCoplanarBeforeBuild = false, ValidateSewCoverage = true };
        }

        private static (List<Panel> Panels, List<Space> Spaces) LoadFixture()
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(FixturePath) ?? new List<IJSAMObject>();
            return (objects.OfType<Panel>().ToList(), objects.OfType<Space>().ToList());
        }

        private static int CountContaining(IEnumerable<Shell> shells, Point3D point3D)
        {
            return shells.Count(x => x.Inside(point3D, Tolerance.MacroDistance, Tolerance.Distance));
        }

        [SkippableFact]
        public void CellComplexByPanels_HouseWithSewRecipe_BuildsOneCellPerRoomPlusAttic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, List<Space> seeds) = LoadFixture();
            Assert.Equal(23, panels.Count);
            Assert.Equal(11, seeds.Count);

            // Act
            List<Shell> shells = GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, GrasshopperRecipe());

            // Assert
            Assert.NotNull(shells);
            Assert.Equal(11, shells.Count);
            foreach (Space seed in seeds)
            {
                Assert.True(CountContaining(shells, seed.Location) == 1, string.Format("Seed '{0}' must lie in exactly one cell.", seed.Name));
            }

            double totalVolume = result.Cells.Sum(x => System.Math.Abs(x.Volume));
            Assert.InRange(totalVolume, ExpectedTotalVolume - 0.5, ExpectedTotalVolume + 0.5);
        }

        [SkippableFact]
        public void CellComplexByPanels_HouseWithSewRecipe_ReportsUnderSplitAndFallback()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, _) = LoadFixture();

            // Act
            GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, GrasshopperRecipe());

            // Assert
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_UNRELIABLE");
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
        }

        [SkippableFact]
        public void FaceCoverage_HouseAdoptedComplex_HasNoInteriorFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, _) = LoadFixture();
            List<Face3D> face3Ds = panels.Select(x => x.GetFace3D()).ToList();
            OcctBuildOptions options = GrasshopperRecipe();

            // Act
            GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, options);
            FaceCoverageReport report = SAM.Geometry.OCCT.Query.FaceCoverage(face3Ds, result, options);

            // Assert
            Assert.Equal(0, report.Count(FaceCoverageVerdict.Missing));
            Assert.Equal(0, report.Count(FaceCoverageVerdict.Partial));
            Assert.Equal(0, report.InteriorArea, 6);
        }

        [SkippableFact]
        public void CellComplexByPanels_HouseRetainTopologySewPath_KeepsSewBuildAndWarnsUnreliable()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, _) = LoadFixture();
            OcctBuildOptions options = GrasshopperRecipe();
            options.RetainTopology = true;

            // Act
            List<Shell> shells = GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, options);

            // Assert: RetainTopology needs the sew path's live handle, so the under-split is not corrected - but
            // it must never read as a clean success.
            using (result)
            {
                Assert.True(shells.Count < 11);
                Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED");
                Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_UNRELIABLE" && x.Severity == OcctDiagnosticSeverity.Warning);
            }
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithSeedSpaces_NamesElevenSpacesIncludingAttic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, List<Space> seeds) = LoadFixture();

            // Act
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(seeds, panels, out OcctCellComplexResult result, null, GrasshopperRecipe());

            // Assert
            Assert.NotNull(adjacencyCluster);
            List<Space> spaces = adjacencyCluster.GetSpaces();
            Assert.Equal(11, spaces.Count);
            Assert.Equal(seeds.Select(x => x.Name).OrderBy(x => x), spaces.Select(x => x.Name).OrderBy(x => x));
            Assert.All(spaces, x => Assert.NotEmpty(adjacencyCluster.GetPanels(x)));

            // Every interior partition separates two spaces: no panel bounds zero spaces.
            Assert.All(adjacencyCluster.GetPanels(), x => Assert.NotEmpty(adjacencyCluster.GetSpaces(x)));
        }

        [SkippableFact]
        public void CellComplexByPanels_HouseSewWithoutCoverageValidation_KeepsLegacySewBuild()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: the solver's own sew builds do not opt in, so their results (and golden masters) are
            // unchanged. This pins that the legacy path still under-splits this house, which is why
            // SAMOCCT.CreateAdjacencyCluster opts in.
            (List<Panel> panels, _) = LoadFixture();
            OcctBuildOptions options = GrasshopperRecipe();
            options.ValidateSewCoverage = false;

            // Act
            List<Shell> shells = GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, options);

            // Assert
            Assert.Equal(4, shells.Count);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED" || x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
        }

        [SkippableFact]
        public void CellComplexByPanels_HouseRepeatedRuns_AreDeterministic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, _) = LoadFixture();

            // Act
            GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult first, GrasshopperRecipe());
            GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult second, GrasshopperRecipe());

            // Assert
            List<double> firstVolumes = first.Cells.Select(x => System.Math.Round(System.Math.Abs(x.Volume), 6)).OrderBy(x => x).ToList();
            List<double> secondVolumes = second.Cells.Select(x => System.Math.Round(System.Math.Abs(x.Volume), 6)).OrderBy(x => x).ToList();
            Assert.Equal(firstVolumes, secondVolumes);
        }
    }
}
