// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="SnapStage"/> - Stage A of the true-3D pipeline
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase 2). Pure-managed (the clean bucket is
    /// native-free), so these run everywhere. Cover the source-attribution / MaxExtend-carry behaviour
    /// that fixes the positional-MaxExtend bug, and the shaft-void survival at Stage A.
    /// </summary>
    public class SnapStageTests
    {
        private static readonly ToleranceBudget DefaultTolerances = new ToleranceBudget();

        /// <summary>Wall in the XZ plane at the given Y offset, spanning x0..x1, 3 m tall (normal -Y).</summary>
        private static SnappedPanel Wall(int sourceIndex, double y, double x0, double x1, double maxExtension)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(x0, y, 0), new Point3D(x1, y, 0), new Point3D(x1, y, 3), new Point3D(x0, y, 3));
            return new SnappedPanel(sourceIndex, face3D, weight: 1, bucketSize: 0.3, maxExtension: maxExtension);
        }

        [Theory]
        [InlineData(0.02, 0.001)]
        [InlineData(0.0005, 1e-6)]
        public void Clean_LoneSmallFace_IsKeptAndNotReported(double size, double tolerance)
        {
            // Arrange: a valid small square (20 mm at 1 mm, 0.5 mm at 1e-6 m). Its area (4e-4 and 2.5e-7 m2) is below the
            // distance tolerance read as a number, yet it is many tolerances wide and has an internal point.
            Face3D face3D = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(size, 0, 0), new Point3D(size, 0, size), new Point3D(0, 0, size));
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(0, face3D, weight: 1, bucketSize: 0.3, maxExtension: 0.4) };
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget { Distance = tolerance }, 0, 0, diagnostics, null, 0.0, records);

            // Assert
            Face3D kept = Assert.Single(result.CleanFace3Ds);
            Assert.Equal(size * size, kept.GetArea(), 12);
            Assert.DoesNotContain(records, x => x.Kind == CleanRecordKind.DroppedInvalid);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.SliverFace);
        }

        [Theory]
        [InlineData(1e-6)]
        [InlineData(0.001)]
        public void Clean_ZeroAreaFaceFirst_IsDroppedAndTheOtherWallsStillMerge(double tolerance)
        {
            // Arrange: a wall face collapsed to 7e-15 m wide (valid, no internal point), exactly as junction snapping left
            // one in the Softhouse model, first in the list; then two edge-adjacent coplanar walls at y = 0. Before the
            // guard the coplanar union returned nothing and the clean kept all three faces unmerged.
            Plane plane = new Plane(new Point3D(16.51, -2.749457597732544, 8), new Vector3D(0, -1, 0));
            List<Point3D> collapsed = new List<Point3D>
            {
                new Point3D(16.510114669799805, -2.749457597732544, 6.475000143051148), new Point3D(16.510114669799805, -2.749457597732544, 9.449999809265135),
                new Point3D(16.510114669799798, -2.749457597732544, 9.449999809265135), new Point3D(16.510114669799798, -2.749457597732544, 6.475000143051148),
            };
            Face3D zeroArea = new Face3D(new Polygon3D(plane, collapsed.ConvertAll(x => plane.Convert(x))));
            Assert.True(zeroArea.IsValid());
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(0, zeroArea, weight: 1, bucketSize: 0.3, maxExtension: 0.4),
                Wall(1, y: 0, x0: 0, x1: 2, maxExtension: 0.4),
                Wall(2, y: 0, x0: 2, x1: 4, maxExtension: 0.4),
            };
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget { Distance = tolerance }, 0.3, 0.3, diagnostics, null, 0.0, records);

            // Assert
            Face3D merged = Assert.Single(result.CleanFace3Ds);
            Assert.Equal(12.0, merged.GetArea(), 6);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.SliverFace && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.Contains(records, x => x.Kind == CleanRecordKind.DroppedInvalid && x.SourceIndex == 0);
        }

        [Fact]
        public void Clean_TwoOverlappingCoplanarWallsMerge_MergedFaceCarriesMaxOfSourceMaxExtends()
        {
            // Two coplanar, overlapping walls at y=0 merge into one face; a separate wall sits at y=5. Their
            // MaxExtends differ (0.4, 1.5, 0.4). The merged face must carry the MAX of its two sources' reach
            // (1.5) and the separate face its own (0.4) - carried by SOURCE identity. The pre-Phase-2 code
            // applied the input list positionally to the 2 merged/reordered faces, landing 1.5 on the WRONG
            // face; this asserts the fix.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                Wall(0, y: 0, x0: 0, x1: 2, maxExtension: 0.4),
                Wall(1, y: 0, x0: 1, x1: 3, maxExtension: 1.5),
                Wall(2, y: 5, x0: 0, x1: 2, maxExtension: 0.4)
            };

            SnapStage.Result result = SnapStage.Clean(panels, DefaultTolerances, alignColinearOffset: 0.3, normalizeCapOffset: 0.3);

            Assert.Equal(result.CleanFace3Ds.Count, result.CleanMaxExtensions.Count);

            // The merged wall (its face centre is on y=0) carries 1.5; the separate wall (centre near y=5) carries 0.4.
            for (int i = 0; i < result.CleanFace3Ds.Count; i++)
            {
                Point3D centre = result.CleanFace3Ds[i].GetBoundingBox().GetCentroid();
                if (System.Math.Abs(centre.Y - 0) < 0.5)
                {
                    Assert.Equal(1.5, result.CleanMaxExtensions[i], 6);
                }
                else if (System.Math.Abs(centre.Y - 5) < 0.5)
                {
                    Assert.Equal(0.4, result.CleanMaxExtensions[i], 6);
                }
            }
        }

        [Fact]
        public void Clean_MergedFace_RecordsAllContributingSourcesInSourceMap()
        {
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                Wall(0, y: 0, x0: 0, x1: 2, maxExtension: 0.4),
                Wall(1, y: 0, x0: 1, x1: 3, maxExtension: 1.5)
            };

            SourceMap sourceMap = new SourceMap();
            SnapStage.Result result = SnapStage.Clean(panels, DefaultTolerances, 0.3, 0.3, sourceMap: sourceMap);

            // The single merged clean face is FaceKey(0), attributed to both source panels.
            Assert.Single(result.CleanFace3Ds);
            IReadOnlyList<int> sources = sourceMap.SourcesOf(new FaceKey(0));
            Assert.Equal(new[] { 0, 1 }, sources.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Clean_EveryCleanFace_HasAtLeastOneSource()
        {
            // The mapping-orphan invariant at Stage A: no clean face is left without a source.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                Wall(0, y: 0, x0: 0, x1: 2, maxExtension: 0.4),
                Wall(1, y: 0, x0: 1, x1: 3, maxExtension: 1.5),
                Wall(2, y: 5, x0: 0, x1: 2, maxExtension: 0.4)
            };

            SourceMap sourceMap = new SourceMap();
            SnapStage.Result result = SnapStage.Clean(panels, DefaultTolerances, 0.3, 0.3, sourceMap: sourceMap);

            for (int i = 0; i < result.CleanFace3Ds.Count; i++)
            {
                Assert.True(sourceMap.HasSource(new FaceKey(i)), $"Clean face {i} was left source-orphaned");
            }
        }

        /// <summary>Wall in the XZ plane at the given Y offset (x0..1, 3 m tall), equal weight, given bucket.</summary>
        private static SnappedPanel ParallelWall(int sourceIndex, double y, double bucketSize)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(0, y, 0), new Point3D(1, y, 0), new Point3D(1, y, 3), new Point3D(0, y, 3));
            return new SnappedPanel(sourceIndex, face3D, weight: 1, bucketSize: bucketSize, maxExtension: 0.5);
        }

        private static double CentroidY(SnappedPanel panel)
        {
            return panel.Face3D.GetBoundingBox().GetCentroid().Y;
        }

        /// <summary>Perpendicular spread (max - min centroid Y) of a set of parallel XZ-plane walls.</summary>
        private static double Spread(IEnumerable<SnappedPanel> panels)
        {
            List<double> ys = panels.Select(CentroidY).ToList();
            return ys.Max() - ys.Min();
        }

        [Fact]
        public void SnapToFixedPoint_ChainedOffsetOutsideSinglePassReach_ConvergesInAtLeastTwoIterations()
        {
            // Chained-offset fixture (Phase 2b acceptance): three equal-weight parallel walls where the far
            // wall only comes within the first backer's reach AFTER that backer's bucket grows from merging
            // with the middle wall - a growth that happens LATER in the same pass than the far wall was
            // already checked and rejected. A single greedy pass therefore cannot fully coplanarize them; the
            // fixed-point loop must run a second pass over the now-current geometry. Input order [A, C, B]
            // (all equal weight/bucket/area, so OrderForSnap preserves it) forces the far wall C to be scanned
            // before the middle wall B under backer A.
            // One greedy pass over a clone, to measure what a single pass alone achieves.
            List<SnappedPanel> onePass = new List<SnappedPanel>
            {
                ParallelWall(0, y: 0.0, bucketSize: 0.3),
                ParallelWall(2, y: 0.4, bucketSize: 0.3),
                ParallelWall(1, y: 0.25, bucketSize: 0.3)
            };
            Panel3DSnapSolver.Snap(onePass, DefaultTolerances.Angle, DefaultTolerances.ArcAngle,
                DefaultTolerances.Distance, DefaultTolerances.VerticalAngle, alignColinearOffset: 0.3);
            double onePassSpread = Spread(onePass);

            // The full fixed-point loop over an identical fresh set.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                ParallelWall(0, y: 0.0, bucketSize: 0.3),   // first backer
                ParallelWall(2, y: 0.4, bucketSize: 0.3),   // far wall - outside A's original 0.3 bucket
                ParallelWall(1, y: 0.25, bucketSize: 0.3)   // middle wall - inside A's bucket
            };

            SolverDiagnostics diagnostics = new SolverDiagnostics();
            int iterations = SnapStage.SnapToFixedPoint(panels, DefaultTolerances, alignColinearOffset: 0.3, diagnostics: diagnostics);
            double fixedPointSpread = Spread(panels);

            // Needed more than one pass, reached a genuine fixed point (no cap warning), and the extra passes
            // did real work: the far wall is drawn measurably tighter than a single greedy pass could manage.
            Assert.True(iterations >= 2, $"Chained offset should need >= 2 passes (got {iterations})");
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.BudgetExceeded));
            Assert.True(fixedPointSpread < onePassSpread,
                $"Fixed-point spread ({fixedPointSpread}) should be tighter than a single pass ({onePassSpread})");
        }

        [Fact]
        public void SnapToFixedPoint_SinglePassSuffices_ReturnsOneIteration()
        {
            // Two equal-weight walls within one bucket coplanarize in the first pass; the second pass finds
            // nothing to move, so the loop reports a single effective iteration (2D do-while parity: run,
            // then one confirming pass). No cap warning on a trivially convergent input.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                ParallelWall(0, y: 0.0, bucketSize: 0.3),
                ParallelWall(1, y: 0.15, bucketSize: 0.3)
            };

            SolverDiagnostics diagnostics = new SolverDiagnostics();
            int iterations = SnapStage.SnapToFixedPoint(panels, DefaultTolerances, alignColinearOffset: 0.3, diagnostics: diagnostics);

            Assert.InRange(iterations, 1, 2);
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.BudgetExceeded));
        }

        [Fact]
        public void SnapToFixedPoint_IterationCapReachedOnNonConvergentInput_EmitsBudgetExceededWarning()
        {
            // A cap of 1 pass on an input that provably needs >= 2 (the chained fixture above) leaves work
            // undone, so the loop must surface a BudgetExceeded warning on the Snap stage rather than silently
            // returning an incomplete model (2D SnapIterationCapReached parity).
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                ParallelWall(0, y: 0.0, bucketSize: 0.3),
                ParallelWall(2, y: 0.4, bucketSize: 0.3),
                ParallelWall(1, y: 0.25, bucketSize: 0.3)
            };

            SolverDiagnostics diagnostics = new SolverDiagnostics();
            int iterations = SnapStage.SnapToFixedPoint(panels, DefaultTolerances, alignColinearOffset: 0.3, diagnostics: diagnostics, maxIterations: 1);

            Assert.Equal(1, iterations);
            SolverDiagnostic warning = Assert.Single(diagnostics.OfCode(DiagnosticCode.BudgetExceeded));
            Assert.Equal(SolverStage.Snap, warning.Stage);
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
        }

        [Fact]
        public void SnapToFixedPoint_FewerThanTwoPanels_ReturnsZeroWithoutDiagnostics()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            int iterations = SnapStage.SnapToFixedPoint(
                new List<SnappedPanel> { ParallelWall(0, y: 0.0, bucketSize: 0.3) },
                DefaultTolerances, alignColinearOffset: 0.3, diagnostics: diagnostics);

            Assert.Equal(0, iterations);
            Assert.Empty(diagnostics.All);
        }

        [Fact]
        public void Clean_WideVoidBetweenFacingWalls_SurvivesStageA()
        {
            // Two anti-parallel walls 0.35 m apart bounding a shaft void, within the 0.4 m bucket and
            // overlapping - the pre-Phase-2 clean would have collapsed them onto one plane and deleted the
            // void. The thickness-separation gate keeps both, so Stage A preserves the void.
            SnappedPanel a = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 0, 3), new Point3D(1, 0, 3), new Point3D(1, 0, 0)), 1, 0.4, 0.5);
            SnappedPanel b = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0.35, 0), new Point3D(1, 0.35, 0), new Point3D(1, 0.35, 3), new Point3D(0, 0.35, 3)), 1, 0.4, 0.5);

            SnapStage.Result result = SnapStage.Clean(new List<SnappedPanel> { a, b }, DefaultTolerances, 0.3, 0.3);

            // Both wall planes survive: one clean face on y~0 and one on y~0.35 (not collapsed onto a single plane).
            List<double> distinctYs = result.CleanFace3Ds
                .Select(x => x.GetBoundingBox().GetCentroid().Y)
                .Select(y => System.Math.Round(y, 2))
                .Distinct()
                .ToList();

            Assert.Contains(distinctYs, y => System.Math.Abs(y - 0) < 0.02);
            Assert.Contains(distinctYs, y => System.Math.Abs(y - 0.35) < 0.02);
        }

        // ── Coplanar-merge coverage guard (step 3) ───────────────────────

        /// <summary>
        /// The seed-400 synthetic slab at z = 3 m (xy per piece, bit-exact): a 14 x 9 m slab of six abutting pieces with
        /// +-0.5 um seams - a dominant notched piece (s0, clockwise), its notch filler, two west pieces, an east piece with a
        /// slanted seam, a second east piece - and a detached 2 x 2 m piece. Merging the slab leaves a sub-micrometre sliver
        /// hole that SAM's Face2D conversion turns into a 2-point loop, which invalidates (and so drops) the whole 126 m2
        /// face when s0 is listed first in this orientation.
        /// </summary>
        private static readonly double[][] Seed400Pieces =
        {
            new[] { 9.000000435333426, 9.00000030032906, 8.999999618007468, -3.7129389873769014E-07, 4.999999615938999, -3.5770081119324004E-08, 5.000000274143515, 1.9999996847723955, 7.500000196368946, 2.0000001688898568, 7.5000004494617185, 3.9999998632188802, 5.000000113082614, 3.9999999348384625, 5.000000470897542, 9.000000338725508 },
            new[] { 4.999999505998211, 2.000000495077903, 7.500000005335688, 1.9999996965447544, 7.500000465123198, 4.000000482730804, 5.000000093906317, 4.000000270976929 },
            new[] { 4.0222737363038163E-07, 2.994883283591321E-07, 5.000000340004174, 3.080416144385367E-08, 4.999999532013835, 5.999999724946233, 3.582212286801223E-07, 6.000000316056031 },
            new[] { -3.5324155778226896E-07, 5.999999516793181, 4.999999814146901, 5.999999598170239, 4.99999987011676, 9.000000480531687, 3.439054268028485E-07, 8.999999578686603 },
            new[] { 9.000000083507445, 6.000000345507554, 8.950000477637182, 6.000000039051776, 5.90000030148899, 6.059999550740095, 5.899999925694103, 9.000000223566726, 8.999999724308735, 8.999999589063167 },
            new[] { 9.00000038181994, 4.989084958495482E-07, 13.999999822841723, 4.787328578004235E-07, 13.999999786778648, 9.000000213638142, 9.000000090866077, 9.000000092241951 },
            new[] { 20.0, 0.0, 22.0, 0.0, 22.0, 2.0, 20.0, 2.0 },
        };

        /// <summary>The seed-400 piece faces in list order; <paramref name="reverseDominant"/> lists s0's vertices in
        /// reverse (the same coordinates, anticlockwise).</summary>
        private static List<Face3D> Seed400Faces(bool reverseDominant)
        {
            List<Face3D> result = new List<Face3D>();
            for (int i = 0; i < Seed400Pieces.Length; i++)
            {
                List<Point3D> points = new List<Point3D>();
                for (int j = 0; j < Seed400Pieces[i].Length; j += 2)
                {
                    points.Add(new Point3D(Seed400Pieces[i][j], Seed400Pieces[i][j + 1], 3));
                }

                if (i == 0 && reverseDominant)
                {
                    points.Reverse();
                }

                result.Add(new Face3D(new Polygon3D(points)));
            }

            return result;
        }

        private static List<SnappedPanel> Seed400Panels(bool reverseDominant)
        {
            return Seed400Faces(reverseDominant).Select((x, i) => new SnappedPanel(i, x, weight: 1, bucketSize: 0.4, maxExtension: 0.4)).ToList();
        }

        /// <summary>True when the face's internal point lies inside or on one of <paramref name="cleanFace3Ds"/> (same plane).</summary>
        private static bool Covered(Face3D face3D, IEnumerable<Face3D> cleanFace3Ds)
        {
            Point3D internalPoint = face3D.InternalPoint3D();
            return cleanFace3Ds.Any(x =>
            {
                Plane plane = x.GetPlane();
                if (System.Math.Abs(plane.Distance(internalPoint)) > 0.05)
                {
                    return false;
                }

                SAM.Geometry.Planar.Face2D face2D = plane.Convert(x);
                SAM.Geometry.Planar.Point2D point2D = plane.Convert(internalPoint);
                return SAM.Geometry.Planar.Query.Inside(face2D, point2D, 0.01) || face2D.On(point2D, 0.01);
            });
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.21)]
        public void Clean_CoplanarMergeLeavesDegenerateHoleLoop_KeepsMergedSlabAndWarns(double bucketBetweenLevels)
        {
            // Arrange
            List<SnappedPanel> panels = Seed400Panels(reverseDominant: false);
            List<Face3D> inputs = Seed400Faces(reverseDominant: false);
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            SourceMap sourceMap = new SourceMap();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, DefaultTolerances, 0.3, 0.3, diagnostics, sourceMap, bucketBetweenLevels, new List<CleanRecord>());

            // Assert: the 126 m2 slab survives as one face beside the detached 4 m2 piece, and no piece is lost.
            List<double> areas = result.CleanFace3Ds.Select(x => x.GetArea()).OrderBy(x => x).ToList();
            Assert.Equal(2, areas.Count);
            Assert.Equal(4.0, areas[0], 3);
            Assert.Equal(126.0, areas[1], 2);
            Assert.All(inputs, x => Assert.True(Covered(x, result.CleanFace3Ds), "An input piece was lost by the coplanar merge."));
            for (int i = 0; i < result.CleanFace3Ds.Count; i++)
            {
                Assert.True(sourceMap.HasSource(new FaceKey(i)), $"Clean face {i} was left source-orphaned");
            }

            SolverDiagnostic warning = Assert.Single(diagnostics.OfCode(DiagnosticCode.CoplanarMergeRecovered));
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
            Assert.Equal(SolverStage.Snap, warning.Stage);
            Assert.Contains("degenerate loop(s) removed", warning.Message);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.21)]
        public void Clean_SameSlabDominantPieceReversed_MergesWithoutRecovery(double bucketBetweenLevels)
        {
            // Arrange: bit-identical coordinates, only s0 listed anticlockwise - the legacy merge already keeps the slab.
            List<SnappedPanel> panels = Seed400Panels(reverseDominant: true);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, DefaultTolerances, 0.3, 0.3, diagnostics, null, bucketBetweenLevels, new List<CleanRecord>());

            // Assert
            List<double> areas = result.CleanFace3Ds.Select(x => x.GetArea()).OrderBy(x => x).ToList();
            Assert.Equal(2, areas.Count);
            Assert.Equal(4.0, areas[0], 3);
            Assert.Equal(126.0, areas[1], 2);
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.CoplanarMergeRecovered));
        }

        [Fact]
        public void RecoverLostPanels_RepairRejected_KeepsGroupUnmergedAndWarns()
        {
            // Arrange: every seed-400 piece is reported lost (no clean face) and the injected re-union returns nothing, so
            // the repair is rejected and each group must come back as its own pieces.
            List<SnappedPanel> panels = Seed400Panels(reverseDominant: false);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            List<Face3D> recovered = SnapStage.RecoverLostPanels(panels, new List<Face3D>(), DefaultTolerances.Distance, diagnostics, (face2Ds, tolerance) => new List<SAM.Geometry.Planar.Face2D>());

            // Assert: all seven pieces come back unmerged, in input order, with one Warning for the single coplanar group.
            Assert.Equal(panels.Select(x => x.Face3D), recovered);
            SolverDiagnostic warning = Assert.Single(diagnostics.OfCode(DiagnosticCode.CoplanarMergeRecovered));
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("7 panel face(s)", warning.Message);
            Assert.Contains("kept unmerged", warning.Message);
        }

        [Fact]
        public void RecoverLostPanels_NothingLost_ReturnsEmptyWithoutDiagnostics()
        {
            // Arrange: the legacy merge of the preserved orientation covers every piece.
            List<SnappedPanel> panels = Seed400Panels(reverseDominant: true);
            List<Face3D> clean = SAM.Geometry.Spatial.Query.Union(panels.Select(x => x.Face3D), DefaultTolerances.Distance);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            List<Face3D> recovered = SnapStage.RecoverLostPanels(panels, clean, DefaultTolerances.Distance, diagnostics);

            // Assert
            Assert.Empty(recovered);
            Assert.Empty(diagnostics.All);
        }

        [Fact]
        public void Clean3D_CoplanarMergeRecovered_WarningSurvivesEveryDetailLevel()
        {
            // Arrange: the failing seed-400 orientation as floor panels through the public Clean3D path.
            SAM.Analytical.Construction construction = new SAM.Analytical.Construction("slab");
            List<SAM.Analytical.Panel> panels = Seed400Faces(reverseDominant: false)
                .Select(x => SAM.Analytical.Create.Panel(construction, SAM.Analytical.PanelType.Floor, x))
                .ToList();
            SAM.Core.DiagnosticDetail[] levels = { SAM.Core.DiagnosticDetail.Summary, SAM.Core.DiagnosticDetail.Standard, SAM.Core.DiagnosticDetail.Detailed };

            // Act
            List<SAM.Analytical.Panel> clean = SAM.Analytical.OCCT.Solver.Modify.Clean3D(panels, out List<string> lines, out SAM.Analytical.OCCT.Solver.Solve3DReport report, bucketBetweenLevels: 0.21);
            List<DiagnosticRecord> records = SAM.Analytical.OCCT.Solver.SolverReportFormat.FormatDiagnosticRecords(report.Diagnostics, "SAM_OCCT_CLEAN3D");
            Dictionary<SAM.Core.DiagnosticDetail, List<DiagnosticRecord>> filtered = levels.ToDictionary(x => x, x => records.FilterDiagnosticRecords(x));
            List<SAM.Analytical.Panel> again = SAM.Analytical.OCCT.Solver.Modify.Clean3D(panels, out List<string> _, bucketBetweenLevels: 0.21);

            // Assert: one Warning line, kept at every level, and the panels do not depend on the filtering.
            const string prefix = "SAM_OCCT_CLEAN3D_DIAGNOSTIC: [Snap/CoplanarMergeRecovered/Warning] ";
            string line = Assert.Single(lines, x => x.StartsWith(prefix, System.StringComparison.Ordinal));
            foreach (SAM.Core.DiagnosticDetail level in levels)
            {
                DiagnosticRecord record = Assert.Single(filtered[level], x => x.Text.StartsWith(prefix, System.StringComparison.Ordinal));
                Assert.Equal(line, record.Text);
                Assert.Equal(SAM.Core.DiagnosticSeverity.Warning, record.Diagnostic.Severity);
            }

            Assert.Equal(2, clean.Count);
            Assert.Equal(126.0, clean.Max(x => x.GetArea()), 2);
            Assert.Equal(clean.Select(x => x.GetArea()), again.Select(x => x.GetArea()));
        }
    }
}
