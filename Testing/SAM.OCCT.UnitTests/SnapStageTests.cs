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
    }
}
