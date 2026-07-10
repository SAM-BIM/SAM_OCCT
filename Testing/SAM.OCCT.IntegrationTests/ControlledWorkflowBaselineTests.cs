// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P0 baseline capture for the controlled workflow (docs/CONTROLLED_WORKFLOW_PLAN.md §10-P0).
    /// Runs the CURRENT pipeline against the 9-space fixture and REPORTS what happens — it never
    /// asserts pipeline success (only fixture integrity). Two extend paths are measured side by side:
    /// path A = Extend3D(original panels) [one internal Clean], path B = Extend3D(Clean3D(original))
    /// [the current double-Clean chain] — the evidence base for P2's inputAlreadyClean mode.
    /// </summary>
    public class ControlledWorkflowBaselineTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        /// <summary>Expected level-group datums for the fixture (plan §1): raw frames
        /// 12.24/12.436/15.29/15.473/18.34 grouping to 12.24 / 15.29 / 18.34 at band 0.21.</summary>
        private static readonly double[] ExpectedDatums = new double[] { 12.24, 15.29, 18.34 };

        private const double LevelBand = 0.21;

        /// <summary>The double-height space's identity (plan §0.1) — GUID-backed per plan §6, not name-based,
        /// so a re-export that renames or regenerates the space still fails P0 loudly instead of silently
        /// validating the wrong space.</summary>
        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        private const double VerticalNormalZ = 0.342; // sin(20°) — a wall's |normal.Z| stays below this
        private const double CapNormalZ = 0.939;      // cos(20°) — a cap's |normal.Z| stays above this

        private readonly ITestOutputHelper output;

        public ControlledWorkflowBaselineTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void ControlledWorkflow_NineSpacesFixture_BaselineReportCapture()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange — fixture integrity (the only assertions in this test; pipeline success is REPORTED, not asserted).
            string panelsPath = Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam");
            string spacesPath = Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam");
            Assert.True(File.Exists(panelsPath), "Missing fixture: " + panelsPath);
            Assert.True(File.Exists(spacesPath), "Missing fixture: " + spacesPath);

            List<Panel> panels = SAM.Core.Convert.ToSAM(panelsPath).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(spacesPath).OfType<Space>().ToList();
            Assert.NotEmpty(panels);
            Assert.Equal(9, spaces.Count);
            Assert.All(spaces, x => Assert.False(string.IsNullOrWhiteSpace(x.Name), "Space with empty Name"));
            Assert.All(spaces, x => Assert.False(x.Name.Contains("\n") || x.Name.Contains("\r"), "Multiline Space name: " + x.Name));
            Assert.Equal(9, spaces.Select(x => x.Name).Distinct().Count());

            // GUID-backed selection (plan §6/§0.1): the double-height space is identified by GUID, not by
            // name, so a re-export that changes GUIDs or mislabels West3 fails loudly here rather than
            // silently validating the wrong space.
            Space west3 = spaces.FirstOrDefault(x => x.Guid == West3Guid);
            Assert.True(west3 != null, string.Format("Expected double-height space with GUID {0} not found in the fixture (fixture drift).", West3Guid));
            Assert.Equal("West3", west3.Name);

            // (a) Expected-space table.
            output.WriteLine("=== (a) Expected spaces ({0}) ===", spaces.Count);
            double doubleHeightMidZ = (ExpectedDatums[0] + ExpectedDatums[ExpectedDatums.Length - 1]) / 2.0;
            foreach (Space space in spaces.OrderBy(x => x.Name))
            {
                Point3D location = space.Location;
                bool doubleHeightProfile = location != null && System.Math.Abs(location.Z - doubleHeightMidZ) <= 0.1;
                output.WriteLine("SPACE guid={0} name={1} loc={2} band={3} doubleHeightProfile={4}",
                    space.Guid, space.Name, Format(location), LevelBandLabel(location), doubleHeightProfile);
            }

            output.WriteLine("WEST3_GUID: {0} loc={1}", west3.Guid, Format(west3.Location));
            output.WriteLine("");

            // (b) Input panel breakdown.
            output.WriteLine("=== (b) Input panels ({0}) ===", panels.Count);
            foreach (IGrouping<PanelType, Panel> group in panels.GroupBy(x => x.PanelType).OrderBy(x => x.Key.ToString()))
            {
                output.WriteLine("PANELTYPE {0}: {1}", group.Key, group.Count());
            }

            List<Panel> caps = panels.Where(x => IsCap(x)).ToList();
            output.WriteLine("Caps (|normal.Z| >= cos 20 deg): {0}; walls/other: {1}", caps.Count, panels.Count - caps.Count);
            foreach (IGrouping<double, Panel> group in caps.GroupBy(x => System.Math.Round(PlaneElevation(x), 3)).OrderBy(x => x.Key))
            {
                output.WriteLine("CAP_ELEVATION {0:0.###}: {1} cap(s), area {2:0.###} m2",
                    group.Key, group.Count(), group.Sum(x => x.GetFace3D()?.GetArea() ?? 0));
            }

            output.WriteLine("");

            // (c) Clean3D with current defaults.
            output.WriteLine("=== (c) Clean3D(original panels), current defaults ===");
            List<string> cleanDiagnostics;
            Solve3DReport cleanReport;
            List<Panel> cleanedPanels = panels.Clean3D(out cleanDiagnostics, out cleanReport);
            output.WriteLine("Clean3D output panels: {0}", cleanedPanels?.Count.ToString() ?? "null");
            WriteLines("CLEAN3D_DIAG", cleanDiagnostics);
            WriteLines("CLEAN3D_LEVELFRAMES", SolverReportFormat.FormatLevelFrames(cleanReport?.LevelFrames));
            output.WriteLine("");

            // (d) Both extend paths side by side.
            output.WriteLine("=== (d) Extend paths ===");
            List<string> diagnosticsA;
            Solve3DReport reportA;
            List<Panel> extendedA = panels.Extend3D(out diagnosticsA, out reportA);
            output.WriteLine("--- path A: Extend3D(original) [one internal Clean] ---");
            DumpExtendPath("A", extendedA, diagnosticsA, reportA);

            List<Panel> extendedB = null;
            List<string> diagnosticsB = new List<string>();
            Solve3DReport reportB = null;
            if (cleanedPanels != null && cleanedPanels.Count != 0)
            {
                extendedB = cleanedPanels.Extend3D(out diagnosticsB, out reportB);
            }

            output.WriteLine("--- path B: Extend3D(Clean3D(original)) [current double-Clean chain] ---");
            DumpExtendPath("B", extendedB, diagnosticsB, reportB);

            output.WriteLine("--- path A vs path B diff ---");
            DiffPanels(extendedA, extendedB);
            output.WriteLine("");

            // (e)+(f) Adjacency + containment + separator scan, per path.
            AnalyzeAdjacency("path A (Extend3D(original))", extendedA, spaces, panels, west3);
            AnalyzeAdjacency("path B (Extend3D(Clean3D(original)))", extendedB, spaces, panels, west3);

            output.WriteLine("=== Baseline capture complete (report-only; no success assertions) ===");
        }

        private void DumpExtendPath(string label, List<Panel> extendedPanels, List<string> diagnostics, Solve3DReport report)
        {
            output.WriteLine("PATH_{0}_PANELS: {1}", label, extendedPanels?.Count.ToString() ?? "null");
            WriteLines("PATH_" + label + "_DIAG", diagnostics);
            WriteLines("PATH_" + label + "_LEVELFRAMES", SolverReportFormat.FormatLevelFrames(report?.LevelFrames));
            WriteLines("PATH_" + label + "_EXTENDREPORT", report?.FormatExtendReport(), 250);
            WriteLines("PATH_" + label + "_SOURCEMAP", report?.FormatSourceMap(), 120);

            if (extendedPanels == null)
            {
                return;
            }

            foreach (string line in PanelDumpLines(extendedPanels))
            {
                output.WriteLine("PATH_{0}_PANEL: {1}", label, line);
            }
        }

        /// <summary>Deterministic one-line-per-panel dump: type, plane, area, stamped solver parameters.</summary>
        private static List<string> PanelDumpLines(List<Panel> panels)
        {
            List<string> result = new List<string>();
            foreach (Panel panel in panels.Where(x => x?.GetFace3D() != null)
                .OrderBy(x => x.PanelType.ToString())
                .ThenBy(x => System.Math.Round(PlaneElevation(x), 3))
                .ThenBy(x => System.Math.Round(x.GetFace3D().GetArea(), 3)))
            {
                Face3D face3D = panel.GetFace3D();
                Plane plane = face3D.GetPlane();
                Vector3D normal = plane.Normal.Unit;
                double bucketSize = panel.TryGetValue(SolverParameter.BucketSize, out double b) ? b : double.NaN;
                double weight = panel.TryGetValue(SolverParameter.Weight, out double w) ? w : double.NaN;
                double maxExtend = panel.TryGetValue(SolverParameter.MaxExtend, out double m) ? m : double.NaN;
                result.Add(string.Format(
                    "type={0} elev={1:0.###} n=({2:0.##},{3:0.##},{4:0.##}) area={5:0.###} bucket={6:0.###} weight={7:0.###} maxExtend={8:0.###}",
                    panel.PanelType, PlaneElevation(panel), normal.X, normal.Y, normal.Z, face3D.GetArea(), bucketSize, weight, maxExtend));
            }

            return result;
        }

        private void DiffPanels(List<Panel> pathA, List<Panel> pathB)
        {
            if (pathA == null || pathB == null)
            {
                output.WriteLine("DIFF: skipped (a path produced null).");
                return;
            }

            output.WriteLine("DIFF_COUNT: A={0} B={1}", pathA.Count, pathB.Count);
            output.WriteLine("DIFF_AREA: A={0:0.###} B={1:0.###}", SumArea(pathA), SumArea(pathB));

            List<Panel> unmatchedA = pathA.Where(x => !HasGeometricTwin(x, pathB)).ToList();
            List<Panel> unmatchedB = pathB.Where(x => !HasGeometricTwin(x, pathA)).ToList();
            output.WriteLine("DIFF_UNMATCHED: inAOnly={0} inBOnly={1} (coplanar within 0.02 m / 2 deg, area within 1%, centroid within 0.05 m)",
                unmatchedA.Count, unmatchedB.Count);
            foreach (Panel panel in unmatchedA.Take(12))
            {
                output.WriteLine("DIFF_A_ONLY: {0}", DescribePanel(panel));
            }

            foreach (Panel panel in unmatchedB.Take(12))
            {
                output.WriteLine("DIFF_B_ONLY: {0}", DescribePanel(panel));
            }
        }

        private void AnalyzeAdjacency(string label, List<Panel> builderInput, List<Space> expectedSpaces, List<Panel> originalPanels, Space west3)
        {
            output.WriteLine("=== (e) CreateAdjacencyCluster rebuild — {0} ===", label);
            if (builderInput == null || builderInput.Count == 0)
            {
                output.WriteLine("SKIPPED: extend path produced no panels.");
                output.WriteLine("");
                return;
            }

            Log log = new Log();
            OcctCellComplexResult result;
            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                expectedSpaces,
                builderInput,
                out result,
                log,
                new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

            output.WriteLine("BUILD_SUCCESS: {0}; cells={1}; clusterSpaces={2}; clusterPanels={3}",
                result?.Success.ToString() ?? "null",
                result?.Cells?.Count.ToString() ?? "null",
                cluster?.GetSpaces()?.Count.ToString() ?? "null",
                cluster?.GetPanels()?.Count.ToString() ?? "null");
            WriteLines("BUILD_DIAG", result?.Diagnostics?.Select(x => x.ToString()).ToList(), 60);

            IReadOnlyList<OcctCell> cells = result?.Cells;
            if (cells == null || cells.Count == 0)
            {
                output.WriteLine("NO CELLS — containment analysis skipped.");
                output.WriteLine("");
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                BoundingBox3D box = cells[i]?.Shell?.GetBoundingBox();
                output.WriteLine("CELL {0}: centre={1} volume={2:0.###} z=[{3:0.###},{4:0.###}]",
                    i, Format(cells[i]?.Center), cells[i]?.Volume ?? double.NaN, box?.Min?.Z ?? double.NaN, box?.Max?.Z ?? double.NaN);
            }

            // Containment matrix: which cells contain each expected location (FindSeedSpace's predicate).
            Dictionary<Space, List<int>> containment = new Dictionary<Space, List<int>>();
            foreach (Space space in expectedSpaces)
            {
                List<int> hits = new List<int>();
                if (space?.Location != null)
                {
                    for (int i = 0; i < cells.Count; i++)
                    {
                        Shell shell = cells[i]?.Shell;
                        if (shell != null && (shell.Inside(space.Location, Tolerance.MacroDistance, Tolerance.Distance) || shell.On(space.Location, Tolerance.Distance)))
                        {
                            hits.Add(i);
                        }
                    }
                }

                containment[space] = hits;
            }

            // Classify: pick each space's cell (nearest centre when in several), then group by cell.
            Dictionary<Space, int> assigned = new Dictionary<Space, int>();
            foreach (KeyValuePair<Space, List<int>> pair in containment)
            {
                if (pair.Value.Count == 0)
                {
                    int nearest = NearestCellIndex(cells, pair.Key.Location);
                    double distance = nearest >= 0 && pair.Key.Location != null && cells[nearest].Center != null
                        ? pair.Key.Location.Distance(cells[nearest].Center)
                        : double.NaN;
                    output.WriteLine("MISSING: name={0} loc={1} nearestCell={2} centreDistance={3:0.###}",
                        pair.Key.Name, Format(pair.Key.Location), nearest, distance);
                }
                else
                {
                    if (pair.Value.Count > 1)
                    {
                        output.WriteLine("BOUNDARY: name={0} inside cells [{1}] — assigned to nearest centre.",
                            pair.Key.Name, string.Join(",", pair.Value));
                    }

                    assigned[pair.Key] = pair.Value.Count == 1
                        ? pair.Value[0]
                        : pair.Value.OrderBy(i => pair.Key.Location.Distance(cells[i].Center ?? pair.Key.Location)).ThenBy(i => i).First();
                }
            }

            List<IGrouping<int, KeyValuePair<Space, int>>> byCell = assigned.GroupBy(x => x.Value).ToList();
            List<List<Space>> mergedGroups = new List<List<Space>>();
            foreach (IGrouping<int, KeyValuePair<Space, int>> group in byCell.OrderBy(x => x.Key))
            {
                List<Space> members = group.Select(x => x.Key).OrderBy(x => x.Name).ToList();
                if (members.Count == 1)
                {
                    BoundingBox3D box = cells[group.Key]?.Shell?.GetBoundingBox();
                    output.WriteLine("MATCHED: name={0} cell={1} z=[{2:0.###},{3:0.###}]",
                        members[0].Name, group.Key, box?.Min?.Z ?? double.NaN, box?.Max?.Z ?? double.NaN);
                }
                else
                {
                    mergedGroups.Add(members);
                    output.WriteLine("MERGED: names={0} cell={1} ({2} expected locations in one cell)",
                        string.Join("+", members.Select(x => x.Name)), group.Key, members.Count);
                }
            }

            List<int> occupiedCells = assigned.Values.Distinct().ToList();
            foreach (int i in Enumerable.Range(0, cells.Count).Where(i => !occupiedCells.Contains(i)))
            {
                output.WriteLine("EXTRA: cell={0} centre={1} volume={2:0.###} (contains no expected location)",
                    i, Format(cells[i]?.Center), cells[i]?.Volume ?? double.NaN);
            }

            // Double-height status for West3.
            if (assigned.TryGetValue(west3, out int west3Cell))
            {
                BoundingBox3D box = cells[west3Cell]?.Shell?.GetBoundingBox();
                bool spansBottom = box?.Min != null && box.Min.Z <= ExpectedDatums[0] + LevelBand;
                bool spansTop = box?.Max != null && box.Max.Z >= ExpectedDatums[ExpectedDatums.Length - 1] - LevelBand;
                output.WriteLine("DOUBLE_HEIGHT: name=West3 cell={0} z=[{1:0.###},{2:0.###}] spansBottom={3} spansTop={4} ok={5}",
                    west3Cell, box?.Min?.Z ?? double.NaN, box?.Max?.Z ?? double.NaN, spansBottom, spansTop, spansBottom && spansTop);
            }
            else
            {
                output.WriteLine("DOUBLE_HEIGHT: name=West3 — no containing cell (see MISSING above).");
            }

            // Orphan cluster panels (related to zero spaces).
            List<Panel> clusterPanels = cluster?.GetPanels() ?? new List<Panel>();
            List<Panel> orphans = clusterPanels.Where(x => (cluster.GetSpaces(x)?.Count ?? 0) == 0).ToList();
            output.WriteLine("ORPHAN_CLUSTER_PANELS: {0} of {1}", orphans.Count, clusterPanels.Count);
            foreach (Panel panel in orphans.Take(15))
            {
                output.WriteLine("ORPHAN: {0}", DescribePanel(panel));
            }

            // Unused builder-input panels (no coplanar overlapping contribution to any cluster face) — approximate geometric test.
            List<Panel> unused = builderInput.Where(x => x?.GetFace3D() != null && !clusterPanels.Any(y => IsCoplanarOverlap(x, y))).ToList();
            output.WriteLine("UNUSED_INPUT_PANELS (approximate): {0} of {1}", unused.Count, builderInput.Count);
            foreach (Panel panel in unused.Take(15))
            {
                output.WriteLine("UNUSED: {0}", DescribePanel(panel));
            }

            // (f) Separator scan for every merged pair, against the ORIGINAL input panels.
            foreach (List<Space> group in mergedGroups)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        ScanSeparator(group[i], group[j], originalPanels);
                    }
                }
            }

            // For each MISSING space, scan separators toward every plan-neighbour (<= 8 m) — evidence for
            // "wall genuinely absent" vs "present but unused" around an unenclosed region.
            foreach (Space missing in expectedSpaces.Where(x => x?.Location != null && containment[x].Count == 0))
            {
                foreach (Space other in expectedSpaces.Where(x => !ReferenceEquals(x, missing) && x?.Location != null))
                {
                    double planDistance = System.Math.Sqrt(
                        System.Math.Pow(missing.Location.X - other.Location.X, 2)
                        + System.Math.Pow(missing.Location.Y - other.Location.Y, 2));
                    if (planDistance <= 8.0)
                    {
                        output.WriteLine("MISSING_NEIGHBOUR_SCAN: {0} vs {1} (plan distance {2:0.###})", missing.Name, other.Name, planDistance);
                        ScanSeparator(missing, other, originalPanels);
                    }
                }
            }

            output.WriteLine("");
        }

        private void ScanSeparator(Space a, Space b, List<Panel> panels)
        {
            Point3D locationA = a?.Location;
            Point3D locationB = b?.Location;
            if (locationA == null || locationB == null)
            {
                return;
            }

            List<string> candidates = new List<string>();
            List<string> partials = new List<string>();

            // Compare against the expected floor-to-ceiling span of the level(s) the two spaces occupy, not
            // just the (much tighter) band around the two seed elevations - a wall-like panel that only
            // covers a ~0.1 m gap between two same-floor seeds is a partial-height fragment/upstand that
            // cannot bound a room, and must not be reported as a usable separator candidate (codex review,
            // PR #57).
            double[] spanA = ExpectedLevelSpan(locationA);
            double[] spanB = ExpectedLevelSpan(locationB);
            double zMin = System.Math.Min(spanA[0], spanB[0]);
            double zMax = System.Math.Max(spanA[1], spanB[1]);
            Point3D midpoint = new Point3D((locationA.X + locationB.X) / 2, (locationA.Y + locationB.Y) / 2, (locationA.Z + locationB.Z) / 2);

            foreach (Panel panel in panels)
            {
                Face3D face3D = panel?.GetFace3D();
                Plane plane = face3D?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (System.Math.Abs(normal.Z) > VerticalNormalZ)
                {
                    continue; // not a wall
                }

                double offsetA = SignedOffset(plane, locationA);
                double offsetB = SignedOffset(plane, locationB);
                if (offsetA * offsetB >= -1e-9)
                {
                    continue; // not strictly between the two locations
                }

                BoundingBox3D box = face3D.GetBoundingBox();
                if (box == null || box.Min.Z > zMin + LevelBand || box.Max.Z < zMax - LevelBand)
                {
                    continue; // does not cover the pair vertically
                }

                Point3D projected = plane.Project(midpoint);
                if (projected != null && face3D.Inside(projected))
                {
                    candidates.Add(DescribePanel(panel));
                }
                else if (projected != null
                    && projected.X >= box.Min.X - 0.5 && projected.X <= box.Max.X + 0.5
                    && projected.Y >= box.Min.Y - 0.5 && projected.Y <= box.Max.Y + 0.5)
                {
                    partials.Add(DescribePanel(panel));
                }
            }

            if (candidates.Count == 0 && partials.Count == 0)
            {
                output.WriteLine("SEPARATOR_SCAN: {0}|{1}: no candidate panel found — wall genuinely missing from input.", a.Name, b.Name);
            }

            foreach (string candidate in candidates)
            {
                output.WriteLine("SEPARATOR_SCAN: {0}|{1}: candidate (present but unused): {2}", a.Name, b.Name, candidate);
            }

            foreach (string partial in partials)
            {
                output.WriteLine("SEPARATOR_SCAN: {0}|{1}: PARTIAL candidate (lateral near-miss): {2}", a.Name, b.Name, partial);
            }
        }

        private void WriteLines(string prefix, IEnumerable<string> lines, int cap = 400)
        {
            if (lines == null)
            {
                output.WriteLine("{0}: (none)", prefix);
                return;
            }

            int count = 0;
            foreach (string line in lines)
            {
                if (++count > cap)
                {
                    output.WriteLine("{0}: ... truncated after {1} lines.", prefix, cap);
                    break;
                }

                output.WriteLine("{0}: {1}", prefix, line);
            }

            if (count == 0)
            {
                output.WriteLine("{0}: (none)", prefix);
            }
        }

        private static bool IsCap(Panel panel)
        {
            Vector3D normal = panel?.GetFace3D()?.GetPlane()?.Normal?.Unit;
            return normal != null && System.Math.Abs(normal.Z) >= CapNormalZ;
        }

        /// <summary>Elevation of the panel plane along its +Z-hemisphere normal (matches LevelFrame's datum measure).</summary>
        private static double PlaneElevation(Panel panel)
        {
            Plane plane = panel?.GetFace3D()?.GetPlane();
            if (plane == null)
            {
                return double.NaN;
            }

            Vector3D normal = plane.Normal.Unit;
            if (normal.Z < 0)
            {
                normal = normal.GetNegated();
            }

            return normal.X * plane.Origin.X + normal.Y * plane.Origin.Y + normal.Z * plane.Origin.Z;
        }

        private static double SignedOffset(Plane plane, Point3D point3D)
        {
            Vector3D normal = plane.Normal.Unit;
            return normal.X * (point3D.X - plane.Origin.X) + normal.Y * (point3D.Y - plane.Origin.Y) + normal.Z * (point3D.Z - plane.Origin.Z);
        }

        private static double SumArea(List<Panel> panels)
        {
            return panels.Where(x => x?.GetFace3D() != null).Sum(x => x.GetFace3D().GetArea());
        }

        private static bool HasGeometricTwin(Panel panel, List<Panel> others)
        {
            Face3D face3D = panel?.GetFace3D();
            Plane plane = face3D?.GetPlane();
            if (plane == null)
            {
                return false;
            }

            Vector3D normal = plane.Normal.Unit;
            double area = face3D.GetArea();
            Point3D centroid = face3D.GetBoundingBox()?.GetCentroid();

            foreach (Panel other in others)
            {
                Face3D otherFace3D = other?.GetFace3D();
                Plane otherPlane = otherFace3D?.GetPlane();
                if (otherPlane == null)
                {
                    continue;
                }

                if (System.Math.Abs(normal.DotProduct(otherPlane.Normal.Unit)) < 0.9994) // 2 deg
                {
                    continue;
                }

                if (System.Math.Abs(SignedOffset(plane, otherPlane.Origin)) > 0.02)
                {
                    continue;
                }

                double otherArea = otherFace3D.GetArea();
                if (area > 1e-6 && System.Math.Abs(otherArea - area) / area > 0.01)
                {
                    continue;
                }

                Point3D otherCentroid = otherFace3D.GetBoundingBox()?.GetCentroid();
                if (centroid != null && otherCentroid != null && centroid.Distance(otherCentroid) > 0.05)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>Approximate contribution test: the input panel's plane coincides with the cluster panel's
        /// (within 0.02 m / 2 deg) and their bounding boxes overlap (expanded 0.05 m).</summary>
        private static bool IsCoplanarOverlap(Panel inputPanel, Panel clusterPanel)
        {
            Face3D inputFace3D = inputPanel?.GetFace3D();
            Face3D clusterFace3D = clusterPanel?.GetFace3D();
            Plane inputPlane = inputFace3D?.GetPlane();
            Plane clusterPlane = clusterFace3D?.GetPlane();
            if (inputPlane == null || clusterPlane == null)
            {
                return false;
            }

            if (System.Math.Abs(inputPlane.Normal.Unit.DotProduct(clusterPlane.Normal.Unit)) < 0.9994)
            {
                return false;
            }

            if (System.Math.Abs(SignedOffset(inputPlane, clusterPlane.Origin)) > 0.02)
            {
                return false;
            }

            BoundingBox3D inputBox = inputFace3D.GetBoundingBox();
            BoundingBox3D clusterBox = clusterFace3D.GetBoundingBox();
            if (inputBox == null || clusterBox == null)
            {
                return false;
            }

            return inputBox.Min.X <= clusterBox.Max.X + 0.05 && clusterBox.Min.X <= inputBox.Max.X + 0.05
                && inputBox.Min.Y <= clusterBox.Max.Y + 0.05 && clusterBox.Min.Y <= inputBox.Max.Y + 0.05
                && inputBox.Min.Z <= clusterBox.Max.Z + 0.05 && clusterBox.Min.Z <= inputBox.Max.Z + 0.05;
        }

        private static int NearestCellIndex(IReadOnlyList<OcctCell> cells, Point3D location)
        {
            if (location == null)
            {
                return -1;
            }

            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                Point3D centre = cells[i]?.Center;
                if (centre == null)
                {
                    continue;
                }

                double distance = location.Distance(centre);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private string LevelBandLabel(Point3D location)
        {
            if (location == null)
            {
                return "null-location";
            }

            double z = location.Z;
            for (int i = 0; i < ExpectedDatums.Length - 1; i++)
            {
                if (z >= ExpectedDatums[i] && z <= ExpectedDatums[i + 1])
                {
                    return string.Format("level {0} ({1:0.###}-{2:0.###})", i + 1, ExpectedDatums[i], ExpectedDatums[i + 1]);
                }
            }

            return z < ExpectedDatums[0] ? "below levels" : "above levels";
        }

        /// <summary>The expected [floor, ceiling] datum pair for the level a location sits in (used by the
        /// separator scan to require a candidate wall span the FULL level height, not just the narrow band
        /// between two seed elevations on the same floor - a partial-height fragment/upstand cannot bound a
        /// room). Falls back to a band bracketing the location itself when it sits outside every known
        /// datum pair.</summary>
        private static double[] ExpectedLevelSpan(Point3D location)
        {
            if (location == null)
            {
                return new double[] { double.NegativeInfinity, double.PositiveInfinity };
            }

            double z = location.Z;
            for (int i = 0; i < ExpectedDatums.Length - 1; i++)
            {
                if (z >= ExpectedDatums[i] - LevelBand && z <= ExpectedDatums[i + 1] + LevelBand)
                {
                    return new double[] { ExpectedDatums[i], ExpectedDatums[i + 1] };
                }
            }

            return new double[] { z - LevelBand, z + LevelBand };
        }

        private static string DescribePanel(Panel panel)
        {
            Face3D face3D = panel?.GetFace3D();
            return string.Format("guid={0} type={1} area={2:0.###} elev={3:0.###} centroid={4}",
                panel?.Guid.ToString() ?? "?", panel?.PanelType.ToString() ?? "?",
                face3D?.GetArea() ?? double.NaN,
                panel == null ? double.NaN : PlaneElevation(panel),
                Format(face3D?.GetBoundingBox()?.GetCentroid()));
        }

        private static string Format(Point3D point3D)
        {
            return point3D == null ? "(null)" : string.Format("({0:0.###},{1:0.###},{2:0.###})", point3D.X, point3D.Y, point3D.Z);
        }
    }
}
