// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Diagnostic-only reproduction of the user's Rhino chain:
    /// Panels -> Extend3D -> CreateAdjacencyCluster -> MergeCoplanarAdjacencyCluster.
    /// This class intentionally changes no production solver behaviour.
    /// </summary>
    public class ManualExtend3DReplicationDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public ManualExtend3DReplicationDiagnosticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableTheory]
        [InlineData("whole-level-towers.sam", 19.532076, -4.830732, 13.813167, 15.127505, -5.238095, 12.24)]
        [InlineData("ControlledWorkflow/Panels-9SpacesModel.sam", 1.892919, -5.763134, 15.29, 1.892919, -5.763134, 15.29)]
        [InlineData("two-level-tilted.sam", 3.777848, -23.272916, 0.214115, 3.777848, -23.272916, 0.214115)]
        [InlineData("whole-level-tilted.sam", 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)]
        public void UserGrasshopperChain_BaselineAndAnchorGeometryReport(
            string fixture,
            double probeX,
            double probeY,
            double probeZ,
            double panelX,
            double panelY,
            double panelZ)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> inputPanels = LoadPanels(fixture);
            Assert.NotEmpty(inputPanels);

            Point3D probe = new Point3D(probeX, probeY, probeZ);
            Point3D panelAnchor = new Point3D(panelX, panelY, panelZ);

            List<Panel> extendedPanels = inputPanels.Extend3D(
                out List<string> extendDiagnostics,
                out Solve3DReport extendReport,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);

            Assert.NotNull(extendedPanels);
            List<Panel> buildPanels = extendedPanels
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();

            OcctBuildOptions options = new OcctBuildOptions
            {
                Tolerance = Tolerance.Distance,
                FuzzyTolerance = Tolerance.MacroDistance,
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01,
                MergeCoplanarBeforeBuild = false,
            };

            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null,
                buildPanels,
                out OcctCellComplexResult result,
                new Log(),
                options);

            List<string> mergeDiagnostics = new List<string>();
            AdjacencyCluster merged = cluster == null
                ? null
                : cluster.MergeCoplanarPanels(
                    out mergeDiagnostics,
                    Tolerance.Distance,
                    Tolerance.Angle);

            try
            {
                output.WriteLine("FIXTURE {0}", fixture);
                output.WriteLine("SETTINGS bucket=0.5 doubleWallGap=0.5 bucketBetweenLevels=0.5 fillMargin=0.5 thicknessFactor=0.6 alignColinearOffset=0.3 normalizeCapOffset=0.3 inputAlreadyClean=false directionalCapGrow=false");
                output.WriteLine("PIPELINE inputPanels={0} extendPanels={1} buildPanels={2} cells={3} clusterSpaces={4} clusterPanels={5} mergedSpaces={6} mergedPanels={7}",
                    inputPanels.Count,
                    extendedPanels.Count,
                    buildPanels.Count,
                    result?.Cells?.Count ?? 0,
                    cluster?.GetSpaces()?.Count ?? 0,
                    cluster?.GetPanels()?.Count ?? 0,
                    merged?.GetSpaces()?.Count ?? 0,
                    merged?.GetPanels()?.Count ?? 0);
                output.WriteLine("MEASURES floorArea={0:F6} volume={1:F6}",
                    CellFloorArea(result?.Cells),
                    result?.Cells?.Sum(x => x.Volume) ?? 0.0);

                if (fixture != "whole-level-tilted.sam")
                {
                    output.WriteLine("PROBE {0} containedByCell={1}", Format(probe), ContainingCellIndex(result?.Cells, probe));
                    DumpNearestPanels("INPUT_NEAR_PANEL_ANCHOR", inputPanels, panelAnchor, 16);
                    DumpNearestPanels("EXTENDED_NEAR_PANEL_ANCHOR", extendedPanels, panelAnchor, 16);
                    DumpNearestPanels("INPUT_NEAR_PROBE", inputPanels, probe, 16);
                    DumpNearestPanels("EXTENDED_NEAR_PROBE", extendedPanels, probe, 16);
                }

                foreach (string line in extendReport?.FormatExtendReport() ?? new List<string>())
                {
                    output.WriteLine("EXTEND_REPORT {0}", line);
                }

                foreach (string line in extendDiagnostics ?? new List<string>())
                {
                    if (line.Contains("SAM_OCCT_EXTEND3D_") || line.Contains("SAM_OCCT_CLEAN3D_LEVEL"))
                    {
                        output.WriteLine("EXTEND_DIAGNOSTIC {0}", line);
                    }
                }

                foreach (string line in mergeDiagnostics ?? new List<string>())
                {
                    output.WriteLine("MERGE_DIAGNOSTIC {0}", line);
                }
            }
            finally
            {
                result?.Dispose();
            }
        }

        [Theory]
        [InlineData("whole-level-towers.sam", 19.532076, -4.830732, 13.813167, 15.127505, -5.238095, 12.24)]
        [InlineData("ControlledWorkflow/Panels-9SpacesModel.sam", 1.892919, -5.763134, 15.29, 1.892919, -5.763134, 15.29)]
        [InlineData("two-level-tilted.sam", 3.777848, -23.272916, 0.214115, 3.777848, -23.272916, 0.214115)]
        public void ExtendedGeometry_AnchorCapAndWallProfilesReport(
            string fixture,
            double probeX,
            double probeY,
            double probeZ,
            double panelX,
            double panelY,
            double panelZ)
        {
            List<Panel> inputPanels = LoadPanels(fixture);
            Assert.NotEmpty(inputPanels);
            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _,
                out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            Assert.NotNull(extendedPanels);

            Point3D probe = new Point3D(probeX, probeY, probeZ);
            Point3D panelAnchor = new Point3D(panelX, panelY, panelZ);
            output.WriteLine("GEOMETRY_FIXTURE {0}", fixture);

            foreach (var item in extendedPanels
                .Select((panel, index) => new
                {
                    Panel = panel,
                    Index = index,
                    ProbeDistance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), probe),
                    AnchorDistance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), panelAnchor),
                })
                .Where(x => IsCap(x.Panel) && System.Math.Min(x.ProbeDistance, x.AnchorDistance) <= 7.0)
                .OrderBy(x => System.Math.Min(x.ProbeDistance, x.AnchorDistance))
                .ThenBy(x => x.Index))
            {
                Face3D face = item.Panel.GetFace3D();
                Plane plane = face.GetPlane();
                List<Point3D> vertices = (face.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
                output.WriteLine(
                    "CAP_CANDIDATE index={0} guid={1} type={2} probeDistance={3:F6} anchorDistance={4:F6} containsProbeProjection={5} containsAnchorProjection={6} area={7:F6} normal={8} bbox={9} vertices={10}",
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    item.ProbeDistance,
                    item.AnchorDistance,
                    ContainsProjection(face, probe),
                    ContainsProjection(face, panelAnchor),
                    face.GetArea(),
                    Format(plane?.Normal?.Unit),
                    Format(face.GetBoundingBox()),
                    string.Join(";", vertices.Select(Format)));
            }

            foreach (var item in extendedPanels
                .Select((panel, index) => new
                {
                    Panel = panel,
                    Index = index,
                    ProbeDistance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), probe),
                    AnchorDistance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), panelAnchor),
                })
                .Where(x => !IsCap(x.Panel) && System.Math.Min(x.ProbeDistance, x.AnchorDistance) <= 3.0)
                .OrderBy(x => System.Math.Min(x.ProbeDistance, x.AnchorDistance))
                .ThenBy(x => x.Index))
            {
                Face3D face = item.Panel.GetFace3D();
                List<Point3D> vertices = (face.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
                output.WriteLine(
                    "WALL_CANDIDATE index={0} guid={1} type={2} probeDistance={3:F6} anchorDistance={4:F6} area={5:F6} normal={6} bbox={7} profileVertices={8}",
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    item.ProbeDistance,
                    item.AnchorDistance,
                    face.GetArea(),
                    Format(face.GetPlane()?.Normal?.Unit),
                    Format(face.GetBoundingBox()),
                    string.Join(";", vertices.Select(Format)));
            }
        }

        [SkippableFact]
        public void MeasuredManualBoundaryCorrections_ReportCurrentReplicationStatus()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            using (ChainRun towers = RunWithManualCorrection(
                "whole-level-towers.sam",
                panelIndex: 5,
                move: point => point.X >= 18.347 - 1e-6 && point.X <= 23.194 + 1e-6 && point.Y >= -0.8 && point.Y <= -0.7
                    ? new Point3D(point.X, point.Y + 0.08, point.Z)
                    : point,
                probe: new Point3D(19.532076, -4.830732, 13.813167),
                correctionLabel: "north slanted floor edge +Y 0.08 m"))
            {
                output.WriteLine("MANUAL_RESULT towers cells={0} clusterSpaces={1} mergedSpaces={2} floorArea={3:F6} nativeVolume={4:F6} clusterShellVolume={5:F6} mergedShellVolume={6:F6} mergedSpaceArea={7:F6} mergedSpaceVolume={8:F6} probeCell={9}",
                    towers.Result?.Cells?.Count ?? 0,
                    towers.Cluster?.GetSpaces()?.Count ?? 0,
                    towers.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(towers.Result?.Cells),
                    towers.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                    ClusterShellVolume(towers.Cluster),
                    ClusterShellVolume(towers.Merged),
                    ClusterSpaceArea(towers.Merged),
                    ClusterSpaceVolume(towers.Merged),
                    ContainingCellIndex(towers.Result?.Cells, towers.Probe));

                Assert.Equal(31, towers.Merged?.GetSpaces()?.Count ?? 0);
                Assert.InRange(CellFloorArea(towers.Result?.Cells), 3210.934717 - 0.001, 3210.934717 + 0.001);
                Assert.InRange(towers.Result?.Cells?.Sum(x => x.Volume) ?? 0.0, 9760.515642 - 0.001, 9760.515642 + 0.001);
                Assert.True(ContainingCellIndex(towers.Result?.Cells, towers.Probe) >= 0);
            }

            using (ChainRun nineSpaces = RunWithOutputGrowth(
                "ControlledWorkflow/Panels-9SpacesModel.sam",
                panelIndices: new[] { 4, 26 },
                margin: 0.5,
                probe: new Point3D(1.892919, -5.763134, 15.29),
                correctionLabel: "two cap pieces enlarged outward 0.5 m to cover the full wall envelope"))
            {
                output.WriteLine("MANUAL_RESULT nine-spaces cells={0} clusterSpaces={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6} probeCell={5}",
                    nineSpaces.Result?.Cells?.Count ?? 0,
                    nineSpaces.Cluster?.GetSpaces()?.Count ?? 0,
                    nineSpaces.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(nineSpaces.Result?.Cells),
                    nineSpaces.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                    ContainingCellIndex(nineSpaces.Result?.Cells, nineSpaces.Probe));

                Assert.Equal(9, nineSpaces.Merged?.GetSpaces()?.Count ?? 0);
                Assert.True(ContainingCellIndex(nineSpaces.Result?.Cells, nineSpaces.Probe) >= 0);
            }

            using (ChainRun twoLevel = RunWithManualCorrection(
                "two-level-tilted.sam",
                panelIndex: 9,
                move: point => System.Math.Abs(point.Y - (-19.674949)) <= 0.001
                    ? new Point3D(point.X, -19.296959, point.Z)
                    : point,
                probe: new Point3D(3.777848, -23.272916, 0.214115),
                correctionLabel: "profiled-wall cap edge +Y 0.377990 m"))
            {
                output.WriteLine("MANUAL_RESULT two-level-tilted cells={0} clusterSpaces={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6} probeCell={5}",
                    twoLevel.Result?.Cells?.Count ?? 0,
                    twoLevel.Cluster?.GetSpaces()?.Count ?? 0,
                    twoLevel.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(twoLevel.Result?.Cells),
                    twoLevel.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                    ContainingCellIndex(twoLevel.Result?.Cells, twoLevel.Probe));

                Assert.Equal(27, twoLevel.Merged?.GetSpaces()?.Count ?? 0);
                Assert.Equal(-1, ContainingCellIndex(twoLevel.Result?.Cells, twoLevel.Probe));
            }

            using (ChainRun control = RunWithManualCorrection(
                "whole-level-tilted.sam",
                panelIndex: -1,
                move: point => point,
                probe: null,
                correctionLabel: "protected control, no edit"))
            {
                output.WriteLine("MANUAL_RESULT whole-level-tilted-control cells={0} clusterSpaces={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6}",
                    control.Result?.Cells?.Count ?? 0,
                    control.Cluster?.GetSpaces()?.Count ?? 0,
                    control.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(control.Result?.Cells),
                    control.Result?.Cells?.Sum(x => x.Volume) ?? 0.0);
                Assert.Equal(22, control.Merged?.GetSpaces()?.Count ?? 0);
            }
        }

        [SkippableFact]
        public void Towers_OriginalAnchoredRoofEastEdgeSweep_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            foreach (double targetX in new[] { 18.5, 19.0, 20.0, 21.0, 21.951086, 21.952, 21.96, 21.98, 22.0, 22.001086 })
            {
                List<Panel> inputPanels = LoadPanels("whole-level-towers.sam");
                const int sourcePanelIndex = 205;
                Panel source = inputPanels[sourcePanelIndex];
                Face3D sourceFace = source.GetFace3D();
                List<Point3D> before = (sourceFace.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
                Assert.NotNull(before);
                double eastX = before.Max(x => x.X);
                List<Point3D> after = before
                    .Select(x => System.Math.Abs(x.X - eastX) <= 0.001 ? new Point3D(targetX, x.Y, x.Z) : x)
                    .ToList();
                Face3D correctedFace = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(after) });
                inputPanels[sourcePanelIndex] = global::SAM.Analytical.Create.Panel(
                    source.Guid,
                    source,
                    correctedFace,
                    source.Apertures,
                    true,
                    Tolerance.MacroDistance,
                    Tolerance.MacroDistance);

                List<Panel> extended = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);
                using (ChainRun run = BuildChain(inputPanels, extended, new Point3D(19.532076, -4.830732, 13.813167)))
                {
                    output.WriteLine("TOWERS_INPUT_SWEEP panelIndex={0} originalEastX={1:F6} targetEastX={2:F6} movement={3:F6} cells={4} mergedSpaces={5} floorArea={6:F6} nativeVolume={7:F6} mergedShellVolume={8:F6} probeCell={9}",
                        sourcePanelIndex,
                        eastX,
                        targetX,
                        targetX - eastX,
                        run.Result?.Cells?.Count ?? 0,
                        run.Merged?.GetSpaces()?.Count ?? 0,
                        CellFloorArea(run.Result?.Cells),
                        run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                        ClusterShellVolume(run.Merged),
                        ContainingCellIndex(run.Result?.Cells, run.Probe));
                }
            }
        }

        [SkippableFact]
        public void NineSpaces_CoplanarRoofPieceSubsetSweep_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            int[] candidates = { 23, 24, 26 };

            for (int mask = 1; mask < (1 << candidates.Length); mask++)
            {
                List<Panel> inputPanels = LoadPanels("ControlledWorkflow/Panels-9SpacesModel.sam");
                List<Panel> extendedPanels = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);

                List<int> moved = new List<int>();
                for (int bit = 0; bit < candidates.Length; bit++)
                {
                    if ((mask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    int panelIndex = candidates[bit];
                    Panel source = extendedPanels[panelIndex];
                    SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                    Assert.True(snappedPanel.GrowOutward(0.5, Tolerance.Distance));
                    extendedPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                        source.Guid,
                        source,
                        snappedPanel.Face3D,
                        source.Apertures,
                        true,
                        Tolerance.MacroDistance,
                        Tolerance.MacroDistance);
                    moved.Add(panelIndex);
                }

                using (ChainRun run = BuildChain(inputPanels, extendedPanels, new Point3D(1.892919, -5.763134, 15.29)))
                {
                    output.WriteLine("NINE_ROOF_SUBSET moved={0} cells={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6} probeCell={5}",
                        string.Join("+", moved),
                        run.Result?.Cells?.Count ?? 0,
                        run.Merged?.GetSpaces()?.Count ?? 0,
                        CellFloorArea(run.Result?.Cells),
                        run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                        ContainingCellIndex(run.Result?.Cells, run.Probe));
                }
            }

            List<Panel> original = LoadPanels("ControlledWorkflow/Panels-9SpacesModel.sam");
            List<Panel> fillOne = original.Extend3D(
                out _, out _,
                minBucketSize: 0.5,
                fillMargin: 1.0,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            using (ChainRun fillOneRun = BuildChain(original, fillOne, new Point3D(1.892919, -5.763134, 15.29)))
            {
                output.WriteLine("NINE_FILL_ONE cells={0} mergedSpaces={1} floorArea={2:F6} volume={3:F6} probeCell={4}",
                    fillOneRun.Result?.Cells?.Count ?? 0,
                    fillOneRun.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(fillOneRun.Result?.Cells),
                    fillOneRun.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                    ContainingCellIndex(fillOneRun.Result?.Cells, fillOneRun.Probe));
            }

            foreach (double datum in new[] { 12.24, 15.29, 18.34, double.NaN })
            {
                List<Panel> inputPanels = LoadPanels("ControlledWorkflow/Panels-9SpacesModel.sam");
                List<Panel> extendedPanels = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);
                List<int> moved = new List<int>();
                for (int panelIndex = 0; panelIndex < extendedPanels.Count; panelIndex++)
                {
                    Panel source = extendedPanels[panelIndex];
                    BoundingBox3D box = source?.GetFace3D()?.GetBoundingBox();
                    if (!IsCap(source) || box == null)
                    {
                        continue;
                    }

                    double elevation = box.GetCentroid().Z;
                    if (!double.IsNaN(datum) && System.Math.Abs(elevation - datum) > 0.01)
                    {
                        continue;
                    }

                    SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                    if (!snappedPanel.GrowOutward(0.5, Tolerance.Distance))
                    {
                        continue;
                    }

                    extendedPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                        source.Guid,
                        source,
                        snappedPanel.Face3D,
                        source.Apertures,
                        true,
                        Tolerance.MacroDistance,
                        Tolerance.MacroDistance);
                    moved.Add(panelIndex);
                }

                using (ChainRun run = BuildChain(inputPanels, extendedPanels, new Point3D(1.892919, -5.763134, 15.29)))
                {
                    output.WriteLine("NINE_ROOF_LEVEL datum={0} moved={1} cells={2} mergedSpaces={3} floorArea={4:F6} volume={5:F6} probeCell={6}",
                        double.IsNaN(datum) ? "ALL" : datum.ToString("F2", CultureInfo.InvariantCulture),
                        string.Join("+", moved),
                        run.Result?.Cells?.Count ?? 0,
                        run.Merged?.GetSpaces()?.Count ?? 0,
                        CellFloorArea(run.Result?.Cells),
                        run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                        ContainingCellIndex(run.Result?.Cells, run.Probe));
                }
            }

            int[] bottomCaps = { 0, 1, 2, 3, 4 };
            for (int mask = 0; mask < (1 << bottomCaps.Length); mask++)
            {
                List<Panel> inputPanels = LoadPanels("ControlledWorkflow/Panels-9SpacesModel.sam");
                List<Panel> extendedPanels = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);
                List<int> moved = new List<int> { 26 };
                for (int bit = 0; bit < bottomCaps.Length; bit++)
                {
                    if ((mask & (1 << bit)) != 0)
                    {
                        moved.Add(bottomCaps[bit]);
                    }
                }

                foreach (int panelIndex in moved)
                {
                    Panel source = extendedPanels[panelIndex];
                    SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                    Assert.True(snappedPanel.GrowOutward(0.5, Tolerance.Distance));
                    extendedPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                        source.Guid,
                        source,
                        snappedPanel.Face3D,
                        source.Apertures,
                        true,
                        Tolerance.MacroDistance,
                        Tolerance.MacroDistance);
                }

                using (ChainRun run = BuildChain(inputPanels, extendedPanels, new Point3D(1.892919, -5.763134, 15.29)))
                {
                    int spaces = run.Merged?.GetSpaces()?.Count ?? 0;
                    if (spaces >= 8)
                    {
                        output.WriteLine("NINE_MINIMAL_SUBSET moved={0} cells={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6} probeCell={5}",
                            string.Join("+", moved.OrderBy(x => x)),
                            run.Result?.Cells?.Count ?? 0,
                            spaces,
                            CellFloorArea(run.Result?.Cells),
                            run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0,
                            ContainingCellIndex(run.Result?.Cells, run.Probe));
                    }
                }
            }
        }

        [SkippableFact]
        public void TwoLevelTilted_ProfiledWallCapSubsetSweep_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            int[] candidates = { 8, 9, 27, 30 };

            for (int mask = 1; mask < (1 << candidates.Length); mask++)
            {
                List<Panel> inputPanels = LoadPanels("two-level-tilted.sam");
                List<Panel> extendedPanels = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);
                List<int> moved = new List<int>();
                for (int bit = 0; bit < candidates.Length; bit++)
                {
                    if ((mask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    int panelIndex = candidates[bit];
                    Panel source = extendedPanels[panelIndex];
                    SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                    Assert.True(snappedPanel.GrowOutward(0.5, Tolerance.Distance));
                    extendedPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                        source.Guid,
                        source,
                        snappedPanel.Face3D,
                        source.Apertures,
                        true,
                        Tolerance.MacroDistance,
                        Tolerance.MacroDistance);
                    moved.Add(panelIndex);
                }

                using (ChainRun run = BuildChain(inputPanels, extendedPanels, new Point3D(3.777848, -23.272916, 0.214115)))
                {
                    output.WriteLine("TWO_LEVEL_CAP_SUBSET margin=0.5 moved={0} cells={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6}",
                        string.Join("+", moved),
                        run.Result?.Cells?.Count ?? 0,
                        run.Merged?.GetSpaces()?.Count ?? 0,
                        CellFloorArea(run.Result?.Cells),
                        run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0);
                }
            }

            List<Panel> original = LoadPanels("two-level-tilted.sam");
            List<Panel> fillOne = original.Extend3D(
                out _, out _,
                minBucketSize: 0.5,
                fillMargin: 1.0,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            using (ChainRun run = BuildChain(original, fillOne, new Point3D(3.777848, -23.272916, 0.214115)))
            {
                output.WriteLine("TWO_LEVEL_FILL_ONE cells={0} mergedSpaces={1} floorArea={2:F6} volume={3:F6}",
                    run.Result?.Cells?.Count ?? 0,
                    run.Merged?.GetSpaces()?.Count ?? 0,
                    CellFloorArea(run.Result?.Cells),
                    run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0);
            }
        }

        [Fact]
        public void TwoLevelTilted_ProfiledWallCapIntersections_Report()
        {
            List<Panel> inputPanels = LoadPanels("two-level-tilted.sam");
            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _, out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            Assert.NotNull(extendedPanels);

            Point3D anchor = new Point3D(3.777848, -23.272916, 0.214115);
            foreach (var item in inputPanels
                .Select((panel, index) => new
                {
                    Panel = panel,
                    Index = index,
                    Distance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), anchor),
                })
                .Where(x => x.Panel?.GetFace3D() != null && x.Distance <= 0.05)
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Index))
            {
                Face3D face = item.Panel.GetFace3D();
                List<Point3D> vertices = (face.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
                output.WriteLine("TWO_LEVEL_INPUT_ANCHOR index={0} guid={1} type={2} distance={3:F6} normal={4} bbox={5} vertices={6}",
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    item.Distance,
                    Format(face.GetPlane()?.Normal?.Unit),
                    Format(face.GetBoundingBox()),
                    string.Join(";", vertices.Select(Format)));
            }

            const int wallIndex = 65;
            Face3D wall = extendedPanels[wallIndex].GetFace3D();
            Assert.NotNull(wall);
            output.WriteLine("TWO_LEVEL_PROFILED_WALL index={0} guid={1} bbox={2}",
                wallIndex,
                extendedPanels[wallIndex].Guid,
                Format(wall.GetBoundingBox()));

            foreach (var item in extendedPanels
                .Select((panel, index) => new { Panel = panel, Index = index })
                .Where(x => IsCap(x.Panel)))
            {
                Face3D cap = item.Panel.GetFace3D();
                List<Point3D> intersections = BoundaryPlaneIntersections(wall, cap.GetPlane());
                if (intersections.Count < 2)
                {
                    continue;
                }

                output.WriteLine("TWO_LEVEL_WALL_CAP_INTERSECTION capIndex={0} capGuid={1} type={2} covered={3} planeNormal={4} capBBox={5} points={6}",
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    intersections.All(x => cap.On(x, Tolerance.MacroDistance)),
                    Format(cap.GetPlane()?.Normal?.Unit),
                    Format(cap.GetBoundingBox()),
                    string.Join(";", intersections.Select(x => string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}:on={1}",
                        Format(x),
                        cap.On(x, Tolerance.MacroDistance)))));
            }
        }

        [SkippableFact]
        public void TwoLevelTilted_EachCapGrowthSweep_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> inputPanels = LoadPanels("two-level-tilted.sam");
            List<Panel> baseline = inputPanels.Extend3D(
                out _, out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            Assert.NotNull(baseline);

            foreach (var item in baseline
                .Select((panel, index) => new { Panel = panel, Index = index })
                .Where(x => IsCap(x.Panel)))
            {
                List<Panel> candidate = baseline.ToList();
                SnappedPanel snappedPanel = new SnappedPanel(item.Index, item.Panel.GetFace3D(), 1.0, 0.5, 0.5);
                if (!snappedPanel.GrowOutward(0.5, Tolerance.Distance))
                {
                    continue;
                }

                candidate[item.Index] = global::SAM.Analytical.Create.Panel(
                    item.Panel.Guid,
                    item.Panel,
                    snappedPanel.Face3D,
                    item.Panel.Apertures,
                    true,
                    Tolerance.MacroDistance,
                    Tolerance.MacroDistance);

                using (ChainRun run = BuildChain(inputPanels, candidate, new Point3D(3.777848, -23.272916, 0.214115)))
                {
                    int cells = run.Result?.Cells?.Count ?? 0;
                    if (cells != 27)
                    {
                        output.WriteLine("TWO_LEVEL_SINGLE_CAP_GROW index={0} guid={1} type={2} cells={3} mergedSpaces={4} floorArea={5:F6} volume={6:F6}",
                            item.Index,
                            item.Panel.Guid,
                            item.Panel.PanelType,
                            cells,
                            run.Merged?.GetSpaces()?.Count ?? 0,
                            CellFloorArea(run.Result?.Cells),
                            run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0);
                    }
                }
            }
        }

        [SkippableFact]
        public void TwoLevelTilted_RawCellsMissingFromExtend_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> inputPanels = LoadPanels("two-level-tilted.sam");
            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _, out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            Point3D anchor = new Point3D(3.777848, -23.272916, 0.214115);
            Vector3D wallNormal = new Vector3D(-0.975006, 0, -0.222177).Unit;

            using (ChainRun raw = BuildChain(inputPanels, inputPanels, anchor))
            using (ChainRun extended = BuildChain(inputPanels, extendedPanels, anchor))
            {
                output.WriteLine("TWO_LEVEL_RAW_VS_EXTEND rawCells={0} rawSpaces={1} extendCells={2} extendSpaces={3}",
                    raw.Result?.Cells?.Count ?? 0,
                    raw.Merged?.GetSpaces()?.Count ?? 0,
                    extended.Result?.Cells?.Count ?? 0,
                    extended.Merged?.GetSpaces()?.Count ?? 0);

                foreach (double offset in new[] { -2.0, -1.0, -0.5, -0.2, 0.2, 0.5, 1.0, 2.0 })
                {
                    Point3D point = new Point3D(
                        anchor.X + wallNormal.X * offset,
                        anchor.Y + wallNormal.Y * offset,
                        anchor.Z + wallNormal.Z * offset);
                    output.WriteLine("TWO_LEVEL_ANCHOR_OFFSET offset={0:F3} point={1} rawCell={2} extendCell={3}",
                        offset,
                        Format(point),
                        ContainingCellIndex(raw.Result?.Cells, point),
                        ContainingCellIndex(extended.Result?.Cells, point));
                }

                foreach (var item in (raw.Result?.Cells ?? new List<OcctCell>())
                    .Select((cell, index) => new { Cell = cell, Index = index })
                    .Where(x => x.Cell?.Center != null && ContainingCellIndex(extended.Result?.Cells, x.Cell.Center) < 0)
                    .OrderBy(x => x.Cell.Center.Distance(anchor)))
                {
                    output.WriteLine("TWO_LEVEL_RAW_CELL_MISSING index={0} center={1} anchorDistance={2:F6} volume={3:F6} sourceFaces={4}",
                        item.Index,
                        Format(item.Cell.Center),
                        item.Cell.Center.Distance(anchor),
                        item.Cell.Volume,
                        string.Join(",", item.Cell.SourceFaceIndexes ?? new List<int>()));

                    if (item.Index == 4 || item.Index == 17)
                    {
                        foreach (var faceItem in (item.Cell.Faces ?? new List<OcctCellFace>())
                            .Select((face, index) => new { Face = face, Index = index }))
                        {
                            Face3D face = faceItem.Face?.Face3D;
                            List<Point3D> vertices = (face?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
                            output.WriteLine("TWO_LEVEL_RAW_CELL_FACE cell={0} face={1} area={2:F6} tilt={3:F6} normal={4} bbox={5} vertices={6}",
                                item.Index,
                                faceItem.Index,
                                faceItem.Face?.Area ?? double.NaN,
                                faceItem.Face?.Tilt ?? double.NaN,
                                Format(faceItem.Face?.Normal?.Unit),
                                Format(face?.GetBoundingBox()),
                                string.Join(";", vertices.Select(Format)));
                            DumpPanelsCoveringPoint("TWO_LEVEL_INPUT_FACE_MATCH", inputPanels, item.Index, faceItem.Index, face);
                            DumpPanelsCoveringPoint("TWO_LEVEL_EXTEND_FACE_MATCH", extendedPanels, item.Index, faceItem.Index, face);
                        }
                    }
                }
            }
        }

        [SkippableFact]
        public void TwoLevelTilted_SourceCapSubsetSweep_Report()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            int[] candidates = { 45, 46, 123, 124 };
            for (int mask = 1; mask < (1 << candidates.Length); mask++)
            {
                List<Panel> inputPanels = LoadPanels("two-level-tilted.sam");
                List<int> moved = new List<int>();
                for (int bit = 0; bit < candidates.Length; bit++)
                {
                    if ((mask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    int panelIndex = candidates[bit];
                    Panel source = inputPanels[panelIndex];
                    SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                    Assert.True(snappedPanel.GrowOutward(1.0, Tolerance.Distance));
                    inputPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                        source.Guid,
                        source,
                        snappedPanel.Face3D,
                        source.Apertures,
                        true,
                        Tolerance.MacroDistance,
                        Tolerance.MacroDistance);
                    moved.Add(panelIndex);
                }

                List<Panel> extendedPanels = inputPanels.Extend3D(
                    out _, out _,
                    minBucketSize: 0.5,
                    fillMargin: 0.5,
                    bucketBetweenLevels: 0.5,
                    doubleWallGap: 0.5);
                using (ChainRun run = BuildChain(inputPanels, extendedPanels, new Point3D(3.777848, -23.272916, 0.214115)))
                {
                    int cells = run.Result?.Cells?.Count ?? 0;
                    output.WriteLine("TWO_LEVEL_SOURCE_CAP_SUBSET margin=1.0 moved={0} cells={1} mergedSpaces={2} floorArea={3:F6} volume={4:F6}",
                        string.Join("+", moved),
                        cells,
                        run.Merged?.GetSpaces()?.Count ?? 0,
                        CellFloorArea(run.Result?.Cells),
                        run.Result?.Cells?.Sum(x => x.Volume) ?? 0.0);
                }
            }
        }

        private static List<Panel> LoadPanels(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture.Replace('/', Path.DirectorySeparatorChar));
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject item in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                switch (item)
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

            return result;
        }

        private static List<Point3D> BoundaryPlaneIntersections(Face3D face, Plane plane)
        {
            List<Point3D> vertices = (face?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
            List<Point3D> result = new List<Point3D>();
            if (vertices.Count < 2 || plane?.Normal == null || plane.Origin == null)
            {
                return result;
            }

            Vector3D normal = plane.Normal.Unit;
            for (int i = 0; i < vertices.Count; i++)
            {
                Point3D a = vertices[i];
                Point3D b = vertices[(i + 1) % vertices.Count];
                double da = normal.X * (a.X - plane.Origin.X)
                    + normal.Y * (a.Y - plane.Origin.Y)
                    + normal.Z * (a.Z - plane.Origin.Z);
                double db = normal.X * (b.X - plane.Origin.X)
                    + normal.Y * (b.Y - plane.Origin.Y)
                    + normal.Z * (b.Z - plane.Origin.Z);

                if (System.Math.Abs(da) <= Tolerance.Distance)
                {
                    AddUnique(result, a);
                }

                if (da * db < 0)
                {
                    double t = da / (da - db);
                    AddUnique(result, new Point3D(
                        a.X + (b.X - a.X) * t,
                        a.Y + (b.Y - a.Y) * t,
                        a.Z + (b.Z - a.Z) * t));
                }
            }

            return result;
        }

        private static void AddUnique(List<Point3D> points, Point3D candidate)
        {
            if (!points.Any(x => x.Distance(candidate) <= Tolerance.Distance))
            {
                points.Add(candidate);
            }
        }

        private void DumpPanelsCoveringPoint(string label, List<Panel> panels, int cellIndex, int faceIndex, Face3D target)
        {
            Point3D center = target?.GetBoundingBox()?.GetCentroid();
            Vector3D targetNormal = target?.GetPlane()?.Normal?.Unit;
            if (center == null || targetNormal == null)
            {
                return;
            }

            foreach (var item in (panels ?? new List<Panel>())
                .Select((panel, index) => new { Panel = panel, Index = index })
                .Where(x =>
                {
                    Face3D face = x.Panel?.GetFace3D();
                    Vector3D normal = face?.GetPlane()?.Normal?.Unit;
                    if (face == null || normal == null)
                    {
                        return false;
                    }

                    double dot = System.Math.Abs(targetNormal.X * normal.X + targetNormal.Y * normal.Y + targetNormal.Z * normal.Z);
                    return dot >= 0.999 && face.On(center, Tolerance.MacroDistance);
                }))
            {
                Face3D face = item.Panel.GetFace3D();
                output.WriteLine("{0} cell={1} face={2} panelIndex={3} guid={4} type={5} area={6:F6} bbox={7}",
                    label,
                    cellIndex,
                    faceIndex,
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    face.GetArea(),
                    Format(face.GetBoundingBox()));
            }
        }

        private ChainRun RunWithManualCorrection(
            string fixture,
            int panelIndex,
            Func<Point3D, Point3D> move,
            Point3D probe,
            string correctionLabel)
        {
            List<Panel> inputPanels = LoadPanels(fixture);
            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _,
                out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            Assert.NotNull(extendedPanels);

            if (panelIndex >= 0)
            {
                Assert.InRange(panelIndex, 0, extendedPanels.Count - 1);
                Panel source = extendedPanels[panelIndex];
                Face3D originalFace = source.GetFace3D();
                List<Point3D> originalVertices = (originalFace.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
                Assert.NotNull(originalVertices);

                List<Point3D> correctedVertices = originalVertices.Select(move).ToList();
                Assert.True(correctedVertices.Where((point, index) => point.Distance(originalVertices[index]) > 1e-9).Any());
                Face3D correctedFace = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(correctedVertices) });
                Assert.NotNull(correctedFace);
                Assert.True(correctedFace.IsValid());

                Panel correctedPanel = global::SAM.Analytical.Create.Panel(
                    source.Guid,
                    source,
                    correctedFace,
                    source.Apertures,
                    true,
                    Tolerance.MacroDistance,
                    Tolerance.MacroDistance);
                Assert.NotNull(correctedPanel);
                extendedPanels[panelIndex] = correctedPanel;

                output.WriteLine("MANUAL_EDIT fixture={0} panelIndex={1} panelGuid={2} panelType={3} correction={4}",
                    fixture, panelIndex, source.Guid, source.PanelType, correctionLabel);
                for (int i = 0; i < originalVertices.Count; i++)
                {
                    Point3D before = originalVertices[i];
                    Point3D after = correctedVertices[i];
                    if (before.Distance(after) <= 1e-9)
                    {
                        continue;
                    }

                    output.WriteLine("MANUAL_VERTEX vertex={0} before={1} after={2} vector={3} distance={4:F6}",
                        i,
                        Format(before),
                        Format(after),
                        Format(new Vector3D(after.X - before.X, after.Y - before.Y, after.Z - before.Z)),
                        before.Distance(after));
                }
            }

            return BuildChain(inputPanels, extendedPanels, probe);
        }

        private ChainRun RunWithInputCorrection(
            string fixture,
            int panelIndex,
            Func<Point3D, Point3D> move,
            Point3D probe,
            string correctionLabel)
        {
            List<Panel> inputPanels = LoadPanels(fixture);
            Assert.InRange(panelIndex, 0, inputPanels.Count - 1);
            Panel source = inputPanels[panelIndex];
            Face3D originalFace = source.GetFace3D();
            List<Point3D> originalVertices = (originalFace.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
            Assert.NotNull(originalVertices);
            List<Point3D> correctedVertices = originalVertices.Select(move).ToList();
            Face3D correctedFace = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(correctedVertices) });
            Assert.NotNull(correctedFace);
            Assert.True(correctedFace.IsValid());
            Panel correctedPanel = global::SAM.Analytical.Create.Panel(
                source.Guid,
                source,
                correctedFace,
                source.Apertures,
                true,
                Tolerance.MacroDistance,
                Tolerance.MacroDistance);
            Assert.NotNull(correctedPanel);
            inputPanels[panelIndex] = correctedPanel;

            output.WriteLine("MANUAL_EDIT fixture={0} stage=input panelIndex={1} panelGuid={2} panelType={3} correction={4}",
                fixture, panelIndex, source.Guid, source.PanelType, correctionLabel);
            for (int i = 0; i < originalVertices.Count; i++)
            {
                Point3D before = originalVertices[i];
                Point3D after = correctedVertices[i];
                if (before.Distance(after) <= 1e-9)
                {
                    continue;
                }

                output.WriteLine("MANUAL_VERTEX vertex={0} before={1} after={2} vector={3} distance={4:F6}",
                    i,
                    Format(before),
                    Format(after),
                    Format(new Vector3D(after.X - before.X, after.Y - before.Y, after.Z - before.Z)),
                    before.Distance(after));
            }

            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _,
                out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);
            return BuildChain(inputPanels, extendedPanels, probe);
        }

        private ChainRun RunWithOutputGrowth(
            string fixture,
            IEnumerable<int> panelIndices,
            double margin,
            Point3D probe,
            string correctionLabel)
        {
            List<Panel> inputPanels = LoadPanels(fixture);
            List<Panel> extendedPanels = inputPanels.Extend3D(
                out _,
                out _,
                minBucketSize: 0.5,
                fillMargin: 0.5,
                bucketBetweenLevels: 0.5,
                doubleWallGap: 0.5);

            foreach (int panelIndex in panelIndices)
            {
                Assert.InRange(panelIndex, 0, extendedPanels.Count - 1);
                Panel source = extendedPanels[panelIndex];
                List<Point3D> originalVertices = (source.GetFace3D().GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
                Assert.NotNull(originalVertices);
                SnappedPanel snappedPanel = new SnappedPanel(panelIndex, source.GetFace3D(), 1.0, 0.5, 0.5);
                Assert.True(snappedPanel.GrowOutward(margin, Tolerance.Distance));
                List<Point3D> correctedVertices = (snappedPanel.Face3D.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
                Assert.NotNull(correctedVertices);
                extendedPanels[panelIndex] = global::SAM.Analytical.Create.Panel(
                    source.Guid,
                    source,
                    snappedPanel.Face3D,
                    source.Apertures,
                    true,
                    Tolerance.MacroDistance,
                    Tolerance.MacroDistance);

                output.WriteLine("MANUAL_EDIT fixture={0} stage=Extend3D-output panelIndex={1} panelGuid={2} panelType={3} correction={4}",
                    fixture, panelIndex, source.Guid, source.PanelType, correctionLabel);
                output.WriteLine("MANUAL_BOUNDARY_BEFORE {0}", string.Join(";", originalVertices.Select(Format)));
                output.WriteLine("MANUAL_BOUNDARY_AFTER {0}", string.Join(";", correctedVertices.Select(Format)));
            }

            return BuildChain(inputPanels, extendedPanels, probe);
        }

        private static ChainRun BuildChain(List<Panel> inputPanels, List<Panel> extendedPanels, Point3D probe)
        {
            List<Panel> buildPanels = extendedPanels
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
            OcctBuildOptions options = new OcctBuildOptions
            {
                Tolerance = Tolerance.Distance,
                FuzzyTolerance = Tolerance.MacroDistance,
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01,
                MergeCoplanarBeforeBuild = false,
            };
            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null,
                buildPanels,
                out OcctCellComplexResult result,
                new Log(),
                options);
            List<string> mergeDiagnostics = new List<string>();
            AdjacencyCluster merged = cluster == null
                ? null
                : cluster.MergeCoplanarPanels(out mergeDiagnostics, Tolerance.Distance, Tolerance.Angle);

            return new ChainRun
            {
                InputPanels = inputPanels,
                ExtendedPanels = extendedPanels,
                Result = result,
                Cluster = cluster,
                Merged = merged,
                Probe = probe,
            };
        }

        private sealed class ChainRun : IDisposable
        {
            public List<Panel> InputPanels { get; set; }
            public List<Panel> ExtendedPanels { get; set; }
            public OcctCellComplexResult Result { get; set; }
            public AdjacencyCluster Cluster { get; set; }
            public AdjacencyCluster Merged { get; set; }
            public Point3D Probe { get; set; }

            public void Dispose()
            {
                Result?.Dispose();
            }
        }

        private void DumpNearestPanels(string label, List<Panel> panels, Point3D anchor, int count)
        {
            foreach (var item in (panels ?? new List<Panel>())
                .Select((panel, index) => new
                {
                    Panel = panel,
                    Index = index,
                    Distance = DistanceToBox(panel?.GetFace3D()?.GetBoundingBox(), anchor),
                })
                .Where(x => x.Panel?.GetFace3D() != null)
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Index)
                .Take(count))
            {
                Face3D face = item.Panel.GetFace3D();
                BoundingBox3D box = face.GetBoundingBox();
                Plane plane = face.GetPlane();
                List<Point3D> vertices = (face.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
                output.WriteLine(
                    "{0} index={1} guid={2} type={3} boxDistance={4:F6} planeDistance={5:F6} area={6:F6} normal={7} bbox={8} vertices={9}",
                    label,
                    item.Index,
                    item.Panel.Guid,
                    item.Panel.PanelType,
                    item.Distance,
                    plane == null ? double.NaN : System.Math.Abs(plane.Distance(anchor)),
                    face.GetArea(),
                    plane?.Normal == null ? "null" : Format(plane.Normal.Unit),
                    Format(box),
                    string.Join(";", vertices.Select(Format)));
            }
        }

        private static int ContainingCellIndex(IReadOnlyList<OcctCell> cells, Point3D point)
        {
            if (cells == null || point == null)
            {
                return -1;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Shell shell = cells[i]?.Shell;
                if (shell != null && (shell.Inside(point, Tolerance.MacroDistance, Tolerance.Distance) || shell.On(point, Tolerance.Distance)))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsCap(Panel panel)
        {
            Face3D face = panel?.GetFace3D();
            Vector3D normal = face?.GetPlane()?.Normal?.Unit;
            return face != null
                && (panel.PanelType == PanelType.Floor
                    || panel.PanelType == PanelType.Roof
                    || (normal != null && System.Math.Abs(normal.Z) >= 0.8));
        }

        private static bool ContainsProjection(Face3D face, Point3D point)
        {
            Plane plane = face?.GetPlane();
            Point3D projected = plane?.Project(point);
            return projected != null && face.On(projected, Tolerance.MacroDistance);
        }

        private static double CellFloorArea(IReadOnlyList<OcctCell> cells)
        {
            double result = 0;
            foreach (OcctCell cell in cells ?? new List<OcctCell>())
            {
                List<OcctCellFace> faces = (cell?.Faces ?? (IReadOnlyList<OcctCellFace>)new List<OcctCellFace>())
                    .Where(x => x != null && !double.IsNaN(x.Area))
                    .ToList();
                double downward = faces
                    .Where(x => !double.IsNaN(x.Tilt) && x.Tilt >= 170.0)
                    .Sum(x => x.Area);

                if (downward > 0)
                {
                    result += downward;
                    continue;
                }

                OcctCellFace mostDownward = faces
                    .Where(x => x.Normal != null)
                    .OrderBy(x => x.Normal.Unit.Z)
                    .FirstOrDefault();
                result += mostDownward?.Area ?? 0.0;
            }

            return result;
        }

        private static double ClusterShellVolume(AdjacencyCluster cluster)
        {
            return cluster?.GetShells()?.Where(x => x != null).Sum(x => x.Volume()) ?? 0.0;
        }

        private static double ClusterSpaceArea(AdjacencyCluster cluster)
        {
            return cluster?.GetSpaces()?.Sum(x => x.CalculatedArea(cluster)) ?? 0.0;
        }

        private static double ClusterSpaceVolume(AdjacencyCluster cluster)
        {
            return cluster?.GetSpaces()?.Sum(x => x.Volume(cluster)) ?? 0.0;
        }

        private static double DistanceToBox(BoundingBox3D box, Point3D point)
        {
            if (box?.Min == null || box.Max == null || point == null)
            {
                return double.MaxValue;
            }

            double dx = System.Math.Max(System.Math.Max(box.Min.X - point.X, 0), point.X - box.Max.X);
            double dy = System.Math.Max(System.Math.Max(box.Min.Y - point.Y, 0), point.Y - box.Max.Y);
            double dz = System.Math.Max(System.Math.Max(box.Min.Z - point.Z, 0), point.Z - box.Max.Z);
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string Format(Point3D point)
        {
            return point == null
                ? "null"
                : string.Format(CultureInfo.InvariantCulture, "({0:F6},{1:F6},{2:F6})", point.X, point.Y, point.Z);
        }

        private static string Format(Vector3D vector)
        {
            return vector == null
                ? "null"
                : string.Format(CultureInfo.InvariantCulture, "({0:F6},{1:F6},{2:F6})", vector.X, vector.Y, vector.Z);
        }

        private static string Format(BoundingBox3D box)
        {
            return box == null ? "null" : string.Format(CultureInfo.InvariantCulture, "[{0}..{1}]", Format(box.Min), Format(box.Max));
        }
    }
}
