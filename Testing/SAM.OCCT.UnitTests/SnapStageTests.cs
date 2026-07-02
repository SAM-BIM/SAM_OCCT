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
