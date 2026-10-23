// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// The residual cap-edge closure on <c>whole-level-towers.sam</c> (record: documentation/Towers-Residual-Closure-Q4.md).
    /// The middle room's merged floor (ten sources incl. <c>efdf7fef</c>) was authored ON the north wall
    /// <c>9407a38c</c>; Clean3D snapped the wall 0.1646 m away and the whole-cap Fill left a 12-28 mm slot, so the room
    /// (119.364 m³) was lost at every gap. The one-step uniform chain now closes exactly that edge. The two-step chain
    /// (Clean3D -> Extend3D(inputAlreadyClean)) is gated off and is the control: its processed panels are today's, so
    /// every one of its cells must reappear with an identical complete boundary in the one-step result, plus the room.
    /// </summary>
    public class TowersResidualContactClosureIntegrationTests
    {
        private const string ClosureMessage = "Residual cap-edge closure";
        private static readonly Point3D RoomPoint = new Point3D(19.5, -5.0, 13.8);
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public TowersResidualContactClosureIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableTheory]
        [InlineData(0.0)]
        [InlineData(0.4)]
        [InlineData(0.47)]
        [InlineData(0.5)]
        public void OneStepUniform_MiddleRoomEdgeAuthoredOnSnappedWall_RecoversRoomAndKeepsEveryOtherCell(double doubleWallGap)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            OcctBuildOptions options = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };

            // Act: the one-step chain of the towers tests, and the gated-off two-step control.
            List<Panel> oneStep = panels.Extend3D(out List<string> diagnostics, minBucketSize: 0.4, alignColinearOffset: 0.3, bucketBetweenLevels: 0.4, fillMargin: 0.4, directionalCapGrow: false, doubleWallGap: doubleWallGap);
            List<Panel> cleaned = panels.Clean3D(out _, out _, minBucketSize: 0.4, alignColinearOffset: 0.3, bucketBetweenLevels: 0.4, doubleWallGap: doubleWallGap);
            List<Panel> twoStep = cleaned.Extend3D(out List<string> twoStepDiagnostics, minBucketSize: 0.4, alignColinearOffset: 0.3, bucketBetweenLevels: 0.4, fillMargin: 0.4, inputAlreadyClean: true, directionalCapGrow: false, doubleWallGap: doubleWallGap);
            List<(Shell Shell, double Volume)> withClosure = Cells(oneStep, options, out int invalidCells);
            List<(Shell Shell, double Volume)> control = Cells(twoStep, options, out _);

            List<string> closures = diagnostics.Where(x => x.Contains("[Snap/Gap/Info] " + ClosureMessage)).ToList();
            foreach (string line in closures) output.WriteLine(line);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "gap {0}: one-step cells {1}, two-step control cells {2}", doubleWallGap, withClosure.Count, control.Count));

            // Assert: exactly one closure, on the middle room's floor, none on the gated-off chain.
            Assert.Single(closures);
            Assert.DoesNotContain(twoStepDiagnostics, x => x.Contains(ClosureMessage));

            // The control is today's result: no room cell there, one cell fewer.
            Assert.DoesNotContain(control, x => x.Shell.Inside(RoomPoint));
            Assert.Equal(control.Count + 1, withClosure.Count);

            // Every control cell keeps its complete boundary (face loops, holes, 1e-6 m); exactly one cell is added.
            List<string> pool = withClosure.Select(x => ShellKey(x.Shell)).ToList();
            foreach ((Shell Shell, double Volume) cell in control)
            {
                Assert.True(pool.Remove(ShellKey(cell.Shell)), "a control cell lost or changed its boundary");
            }

            (Shell Shell, double Volume) added = Assert.Single(withClosure, x => pool.Contains(ShellKey(x.Shell)));
            Assert.True(added.Shell.Inside(RoomPoint));
            Assert.InRange(added.Volume, 119.364 - 0.05, 119.364 + 0.05);
            BoundingBox3D box = added.Shell.GetBoundingBox();
            Assert.InRange(box.Min.X, 17.1427 - 1e-3, 17.1427 + 1e-3);
            Assert.InRange(box.Min.Y, -8.9172 - 1e-3, -8.9172 + 1e-3);
            Assert.InRange(box.Max.X, 21.9509 - 1e-3, 21.9509 + 1e-3);
            Assert.InRange(box.Max.Y, -0.7442 - 1e-3, -0.7442 + 1e-3);
            Assert.Equal(10, added.Shell.Face3Ds.Count);
            Assert.Equal(0, invalidCells);
        }

        /// <summary>
        /// The forced-managed Solve3D chains on towers (record: documentation/Towers-Residual-Closure-Q4.md, managed path).
        /// The middle room closes here too, at a chain-specific volume, and every baseline room survives in the complex
        /// the solver adopted (<see cref="Solve3DReport.ResolvedCellComplex"/>, which Create.Spaces consumes) and in a
        /// guarded rebuild of the returned panels. The unguarded golden-master recount of the same panels is measured
        /// separately (GoldenMasterIntegrationTests) and is not asserted here.
        /// </summary>
        [SkippableTheory]
        [InlineData(0.0, 24)]
        [InlineData(0.21, 27)]
        public void Solve3D_ForceManagedTowers_ResidualClosure_KeepsBaselineRoomsAndAddsMiddleRoom(double bucketBetweenLevels, int expectedCellCount)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            OcctBuildOptions guardedOptions = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, ValidateSewCoverage = true };

            // Act: the golden masters' managed call, then the returned non-air panels rebuilt with the sew guard on.
            List<Panel> solved = panels.Solve3D(out _, out List<string> diagnostics, out _, out Solve3DReport report, forceManagedPipeline: true, bucketBetweenLevels: bucketBetweenLevels);
            ResolvedCellComplex complex = report?.ResolvedCellComplex;
            List<OcctCell> adopted = AdoptedCells(complex);
            List<string> adoptedFailures = OcctUnderSplitGuard.ValidateCells(adopted, guardedOptions, Tolerance.MacroDistance);

            List<Face3D> face3Ds = solved.Where(x => x != null && x.PanelType != PanelType.Air).Select(x => x.GetFace3D()).Where(x => x != null && x.IsValid()).ToList();
            SAM.Geometry.OCCT.Create.Shells(face3Ds, out OcctCellComplexResult rebuilt, guardedOptions);
            List<OcctCell> guarded;
            List<string> guardedFailures;
            using (rebuilt)
            {
                guarded = rebuilt.Cells.ToList();
                guardedFailures = OcctUnderSplitGuard.ValidateCells(guarded, guardedOptions, Tolerance.MacroDistance);
            }

            foreach (string line in diagnostics.Where(x => x.Contains(ClosureMessage))) output.WriteLine(line);
            foreach (string failure in adoptedFailures.Concat(guardedFailures)) output.WriteLine(failure);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "bucketBetweenLevels {0}: adopted cells {1}, guarded rebuild cells {2}", bucketBetweenLevels, adopted.Count, guarded.Count));

            // Assert: every decoded face of the adopted complex is keyed and owned, so the owned faces are each cell's
            // whole boundary.
            Assert.NotNull(complex);
            Assert.Equal(0, complex.TopologyKeyZeroFaceCount);
            HashSet<int> cellIndices = new HashSet<int>(complex.Cells.Select(x => x.Index));
            Assert.All(complex.Faces, face => Assert.InRange(face.OwnerCellIndices.Count, 1, 2));
            Assert.All(complex.Faces, face => Assert.All(face.OwnerCellIndices, x => Assert.Contains(x, cellIndices)));

            // The rooms first, so a missing room is reported by name; then the counts and the cell validation.
            AssertManagedRooms("adopted complex", adopted, bucketBetweenLevels);
            AssertManagedRooms("guarded rebuild", guarded, bucketBetweenLevels);
            Assert.Equal(expectedCellCount, adopted.Count);
            Assert.Equal(expectedCellCount, guarded.Count);
            Assert.All(adopted, x => Assert.True(!double.IsNaN(x.Volume) && !double.IsInfinity(x.Volume) && x.Volume > 0));
            Assert.Empty(adoptedFailures);
            Assert.Empty(guardedFailures);
        }

        /// <summary>The room points of the forced-managed towers chains and each chain's measured room volume (m³).
        /// The middle room is the one this rule recovers; the others are the baseline rooms around it.</summary>
        private static readonly (string Name, Point3D Point, double Volume0, double Volume021)[] ManagedRooms =
        {
            ("west", new Point3D(14.0, -5.0, 13.8), 159.479, 174.145),
            ("far-west", new Point3D(9.2, -5.8, 13.8), 76.994, 80.817),
            ("south", new Point3D(34.4, -21.5, 13.8), 238.960, 250.827),
            ("south-east", new Point3D(42.5, -22.5, 13.8), 155.536, 163.261),
            ("north-east", new Point3D(34.5, -5.0, 13.8), 159.373, 159.373),
            ("middle", RoomPoint, 113.717, 119.364),
        };

        private static void AssertManagedRooms(string label, List<OcctCell> cells, double bucketBetweenLevels)
        {
            List<int> containing = new List<int>();
            foreach ((string Name, Point3D Point, double Volume0, double Volume021) room in ManagedRooms)
            {
                List<int> indices = Enumerable.Range(0, cells.Count).Where(i => cells[i].Shell.Inside(room.Point)).ToList();
                Assert.True(indices.Count == 1, string.Format(CultureInfo.InvariantCulture, "{0}: the {1} room point {2} lies in {3} cell(s), expected exactly one", label, room.Name, room.Point, indices.Count));

                double volume = bucketBetweenLevels == 0.0 ? room.Volume0 : room.Volume021;
                Assert.InRange(cells[indices[0]].Volume, volume - 0.05, volume + 0.05);
                containing.Add(indices[0]);
            }

            // No cell holds two distinct rooms.
            Assert.Equal(containing.Count, containing.Distinct().Count());
        }

        /// <summary>Each adopted cell as an <see cref="OcctCell"/>: the shell is the complex faces that name the cell as an
        /// owner (as Create.AdjacencyCluster reads them); the volume is the complex's own.</summary>
        private static List<OcctCell> AdoptedCells(ResolvedCellComplex complex)
        {
            List<OcctCell> result = new List<OcctCell>();
            foreach (ResolvedCell cell in complex?.Cells ?? new List<ResolvedCell>())
            {
                List<Face3D> face3Ds = complex.Faces.Where(x => x.OwnerCellIndices.Contains(cell.Index)).Select(x => x.Face3D).ToList();
                result.Add(new OcctCell(new Shell(face3Ds), cell.Volume, center: cell.Centre));
            }

            return result;
        }

        private static List<(Shell Shell, double Volume)> Cells(List<Panel> panels, OcctBuildOptions options, out int invalidCells)
        {
            List<(Shell, double)> result = new List<(Shell, double)>();
            SAM.Analytical.OCCT.Create.AdjacencyCluster(null, panels.Where(x => x?.GetFace3D() != null).ToList(), out OcctCellComplexResult complex, new Log(), options);
            using (complex)
            {
                invalidCells = OcctUnderSplitGuard.ValidateCells(complex.Cells, options, Tolerance.MacroDistance).Count;
                foreach (OcctCell cell in complex.Cells)
                {
                    result.Add((cell.Shell, cell.Volume));
                }
            }

            return result;
        }

        /// <summary>A complete-boundary signature: every face's external loop and holes, cyclic order and winding
        /// canonicalised, coordinates rounded to 1e-6 m, faces sorted.</summary>
        private static string ShellKey(Shell shell) => string.Join("|", shell.Face3Ds.Select(FaceKey).OrderBy(x => x, StringComparer.Ordinal));

        private static string FaceKey(Face3D face3D)
        {
            string external = Cycle(((ISegmentable3D)face3D.GetExternalEdge3D()).GetPoints());
            IEnumerable<string> holes = (face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>()).Select(h => Cycle(((ISegmentable3D)h).GetPoints())).OrderBy(x => x, StringComparer.Ordinal);
            return external + "{" + string.Join(";", holes) + "}";
        }

        private static string Cycle(List<Point3D> points)
        {
            List<string> p = points.Select(x => string.Format(CultureInfo.InvariantCulture, "{0:0.000000},{1:0.000000},{2:0.000000}", Round(x.X), Round(x.Y), Round(x.Z))).ToList();
            string best = null;
            foreach (List<string> q in new[] { p, Enumerable.Reverse(p).ToList() })
            {
                for (int i = 0; i < q.Count; i++)
                {
                    string candidate = string.Join(" ", q.Skip(i).Concat(q.Take(i)));
                    if (best == null || string.CompareOrdinal(candidate, best) < 0) best = candidate;
                }
            }

            return best;
        }

        private static double Round(double value) => System.Math.Round(value, 6) + 0.0;

        private static List<Panel> LoadPanels(string path)
        {
            List<Panel> panels = new List<Panel>();
            foreach (IJSAMObject o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel model) panels.AddRange(model.GetPanels());
                else if (o is AdjacencyCluster cluster) panels.AddRange(cluster.GetPanels());
                else if (o is Panel panel) panels.Add(panel);
            }

            return panels;
        }
    }
}
