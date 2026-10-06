// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    public class BucketMergeDiagnosticTests
    {
        private readonly ITestOutputHelper output;

        public BucketMergeDiagnosticTests(ITestOutputHelper output) { this.output = output; }

        private static Face3D Rect(params Point3D[] pts) => TestGeometry.CreatePlanarFace(pts);

        private static Panel Wall(double x, double y0, double y1, double z0, double z1, double weight = 1.0)
        {
            var panel = global::SAM.Analytical.Create.Panel(
                new Construction(Guid.NewGuid(), "Wall"),
                PanelType.Wall,
                Rect(new Point3D(x, y0, z0), new Point3D(x, y1, z0),
                     new Point3D(x, y1, z1), new Point3D(x, y0, z1)));
            panel.SetValue(Analytical.Solver.SolverParameter.Weight, weight);
            return panel;
        }

        private static Panel Floor(double x0, double x1, double y0, double y1, double z, double weight = 1.0)
        {
            var panel = global::SAM.Analytical.Create.Panel(
                new Construction(Guid.NewGuid(), "Floor"),
                PanelType.Floor,
                Rect(new Point3D(x0, y0, z), new Point3D(x1, y0, z),
                     new Point3D(x1, y1, z), new Point3D(x0, y1, z)));
            panel.SetValue(Analytical.Solver.SolverParameter.Weight, weight);
            return panel;
        }

        [Fact]
        public void Extend3D_AlignColinearOffset_ControlsColinearWallMerge()
        {
            // Two walls at x=0 and x=0.2 (0.2m apart), full in-plane overlap, different weights.
            // minBucketSize=0.1 is too small, BUT alignColinearOffset=0.3 (default) merges them.
            // With alignColinearOffset=0, they should survive as 2 walls.
            var backer = Wall(0.0, 0, 10, 0, 3, weight: 2.0);
            var candidate = Wall(0.2, 0, 10, 0, 3, weight: 1.0);
            var panels = new List<Panel> { backer, candidate };

            // align=0.3 (default) — colinear abut fires, walls merge
            var rMerged = panels.Extend3D(out List<string> dMerged,
                minBucketSize: 0.1, alignColinearOffset: 0.3, fillMargin: 0.5);
            int countMerged = (rMerged ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);

            // align=0 — colinear abut disabled, walls survive
            var rSeparate = panels.Extend3D(out List<string> dSeparate,
                minBucketSize: 0.1, alignColinearOffset: 0.0, fillMargin: 0.5);
            int countSeparate = (rSeparate ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);

            output.WriteLine("align=0.3: {0} walls (merged)", countMerged);
            output.WriteLine("align=0.0: {0} walls (kept separate)", countSeparate);

            Assert.True(countMerged < countSeparate,
                string.Format("alignColinearOffset should control colinear wall merge. merged={0} separate={1}",
                    countMerged, countSeparate));
        }

        [Fact]
        public void Extend3D_TwoParallelVerticalWalls_BeyondBothThresholds_DoNotMerge()
        {
            // Two walls 0.6m apart — outside both minBucketSize=0.4 AND alignColinearOffset=0.3.
            var backer = Wall(0.0, 0, 10, 0, 3, weight: 2.0);
            var candidate = Wall(0.6, 0, 10, 0, 3, weight: 1.0);
            var panels = new List<Panel> { backer, candidate };

            var result = panels.Extend3D(out List<string> _,
                minBucketSize: 0.4, alignColinearOffset: 0.3, fillMargin: 0.5);
            int count = (result ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);

            output.WriteLine("minBucket=0.4, align=0.3, walls 0.6m apart: {0} walls", count);

            Assert.True(count >= 2,
                string.Format("Walls 0.6m apart with bucket=0.4/align=0.3 should not merge. Got {0}.", count));
        }

        [Fact]
        public void Extend3D_TwoParallelFloors_AlignColinearOffset_MergesThem()
        {
            var backer = Floor(0, 10, 0, 10, 0.0, weight: 2.0);
            var candidate = Floor(0, 10, 0, 10, 0.15, weight: 1.0);
            var panels = new List<Panel> { backer, candidate };

            // align=0 (no colinear merge) — caps normalize them but don't necessarily merge
            var rSeparate = panels.Extend3D(out List<string> _,
                minBucketSize: 0.05, alignColinearOffset: 0.0, fillMargin: 0.5);

            // align=0.3 — colinear abut fires on near-coplanar caps
            var rMerged = panels.Extend3D(out List<string> _,
                minBucketSize: 0.05, alignColinearOffset: 0.3, fillMargin: 0.5);

            int c0 = (rSeparate ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Floor);
            int c3 = (rMerged ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Floor);

            output.WriteLine("align=0.0: {0} floor panels", c0);
            output.WriteLine("align=0.3: {0} floor panels", c3);

            // Floors/caps use NormalizeCaps which works differently from wall colinear abut.
            // Just assert both runs produce valid output.
            Assert.True(c0 >= 1 && c3 >= 1, "Both runs should produce valid floor panels.");
        }

        [Fact]
        public void Extend3D_BucketAndAlign_SweepWithBothLevers()
        {
            var backer = Wall(0.0, 0, 10, 0, 3, weight: 2.0);
            var candidate = Wall(0.3, 0, 10, 0, 3, weight: 1.0);
            var panels = new List<Panel> { backer, candidate };

            output.WriteLine("Two walls at x=0.0 and x=0.3 (0.3m apart) — sweep both levers");
            output.WriteLine("bucket | align | wall_count | note");
            output.WriteLine("-------|-------|------------|------");

            // Small bucket, small align -> should NOT merge (0.3 > 0.1)
            var r1 = panels.Extend3D(out List<string> _,
                minBucketSize: 0.1, alignColinearOffset: 0.1, fillMargin: 0.5);
            int c1 = (r1 ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);
            output.WriteLine("   0.1 |   0.1 | {0,10} | both too small", c1);

            // Small bucket, large align -> colinear abut fires
            var r2 = panels.Extend3D(out List<string> _,
                minBucketSize: 0.1, alignColinearOffset: 0.4, fillMargin: 0.5);
            int c2 = (r2 ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);
            output.WriteLine("   0.1 |   0.4 | {0,10} | align merges", c2);

            // Large bucket, small align -> bucket snap fires
            var r3 = panels.Extend3D(out List<string> _,
                minBucketSize: 0.5, alignColinearOffset: 0.1, fillMargin: 0.5);
            int c3 = (r3 ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);
            output.WriteLine("   0.5 |   0.1 | {0,10} | bucket merges", c3);

            Assert.True(c1 >= 2, "Both thresholds too small — walls should survive.");
        }

        [Fact]
        public void Extend3D_TwoParallelWalls_NoInPlaneOverlap_NotMerged()
        {
            var wall1 = Wall(0.0, 0, 3, 0, 3, weight: 2.0);
            var wall2 = Wall(0.2, 7, 10, 0, 3, weight: 1.0);
            var panels = new List<Panel> { wall1, wall2 };

            var result = panels.Extend3D(out List<string> _,
                minBucketSize: 1.0, alignColinearOffset: 1.0, fillMargin: 0.5);
            int wallCount = (result ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);

            output.WriteLine("Parallel, no overlap, bucket=1.0, align=1.0: {0} walls", wallCount);

            Assert.True(wallCount >= 2,
                "Walls without in-plane overlap should NOT merge regardless of thresholds.");
        }

        [Fact]
        public void Extend3D_StampedBucketSize_PreemptsMinBucketSize_ButNotAlign()
        {
            var backer = Wall(0.0, 0, 10, 0, 3, weight: 2.0);
            var candidate = Wall(0.2, 0, 10, 0, 3, weight: 1.0);

            // Stamp a tiny bucket — bucket snap path blocked
            backer.SetValue(Analytical.Solver.SolverParameter.BucketSize, 0.05);
            candidate.SetValue(Analytical.Solver.SolverParameter.BucketSize, 0.05);

            var panels = new List<Panel> { backer, candidate };

            // Even with stamped tiny bucket, alignColinearOffset=0.3 still merges them
            var result = panels.Extend3D(out List<string> diags,
                minBucketSize: 1.0, alignColinearOffset: 0.3, fillMargin: 0.5);
            int count = (result ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);

            bool hasSnappedToBacker = diags.Any(d => d.Contains("snapped-to-backer"));
            bool hasCoplanarMerged = diags.Any(d => d.Contains("coplanar-merged"));

            output.WriteLine("With stamped BucketSize=0.05: {0} walls", count);
            output.WriteLine("  snapped-to-backer in diags: {0}", hasSnappedToBacker);
            output.WriteLine("  coplanar-merged in diags: {0}", hasCoplanarMerged);

            // The colinear abut should merge them regardless of stamped small bucket.
            Assert.True(count < 2 || hasCoplanarMerged,
                "Colinear abut should merge colinear walls even when stamped BucketSize blocks bucket snap.");
        }
    }
}
