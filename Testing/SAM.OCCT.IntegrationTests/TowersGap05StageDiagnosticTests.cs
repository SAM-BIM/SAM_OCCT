// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Focused regression for the towers doubleWallGap=0.5 root-cause fix (B1 junction follow + B2 cap
    /// selection), on the real <c>Extend3D</c> output. After the 0.474 m north-strip pair consolidates,
    /// the room's own bounding walls must stay within their local podium storey and reach the consolidated
    /// tower-face plane, rather than being extended one-to-four storeys to tower floor plates they only graze.
    /// </summary>
    public class TowersGap05StageDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private const double Bucket = 0.4;
        private const double Align = 0.3;
        private const double Band = 0.4;   // bucketBetweenLevels
        private const double Fill = 0.4;   // fillMargin
        private const bool DirGrow = false;

        private static List<Panel> LoadPanels()
        {
            string path = Path.Combine(FixturesDirectory, "whole-level-towers.sam");
            var result = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) result.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) result.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Pins the north-strip room's own walls to their LOCAL podium storey at gap 0.5:
        /// <list type="bullet">
        /// <item>the south (y≈-9.41) and north (y≈-2.26) walls stay within the podium storey
        /// (MinZ≈12.19, MaxZ≈15.29-15.47) instead of extending to tower floor plates they only graze
        /// (pre-fix MaxZ 18.39 / 27.54) — Root Cause B2 (actual-face cap selection);</item>
        /// <item>both reach the consolidated tower-face plane at x=-0.345 (MinX≤-0.34), proving the junction
        /// was dragged onto the moved plane — Root Cause B1 (foot-line overhang);</item>
        /// <item>no interior strip-room wall spans more than 1.5× the 3.05 m storey pitch.</item>
        /// </list>
        /// The tower-face plane wall (x≈-0.345, y≈-13.2) legitimately reaches its own cap and is outside the
        /// strip-room y-band, so it is not part of this pin. This test needs no native library (Extend3D is
        /// managed), so it is not native-gated.
        /// </summary>
        [Fact]
        public void Towers_Gap05_StripRoomWalls_StayWithinLocalStorey()
        {
            var panels = LoadPanels();
            Assert.NotEmpty(panels);

            var extended = panels.Extend3D(out _, out _,
                minBucketSize: Bucket, alignColinearOffset: Align,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow,
                doubleWallGap: 0.5);
            Assert.NotNull(extended);

            const double superTall = 1.5 * 3.05; // 4.575 m

            // The strip room's interior bounding walls, excluding the tower-face column at x≈-0.345.
            var stripWalls = (extended ?? new List<Panel>())
                .Where(p => p?.PanelType == PanelType.Wall && p.GetFace3D() != null)
                .Select(p => p.GetFace3D().GetBoundingBox())
                .Where(bb => bb != null)
                .Where(bb =>
                {
                    var c = bb.GetCentroid();
                    return c.X > 0.3 && c.X < 4.0 && c.Y > -10.0 && c.Y < -1.8 && bb.Min.Z < 13.0;
                })
                .ToList();

            Assert.True(stripWalls.Count >= 3,
                string.Format("Expected the strip room's own bounding walls; found {0}.", stripWalls.Count));

            foreach (var bb in stripWalls)
            {
                double h = bb.Max.Z - bb.Min.Z;
                Assert.True(h <= superTall,
                    string.Format("Strip-room wall at ({0:F3},{1:F3}) spans {2:F3} m (> {3:F3}) — abnormal cross-storey extension.",
                        bb.GetCentroid().X, bb.GetCentroid().Y, h, superTall));
                Assert.InRange(bb.Min.Z, 12.0, 12.35);
                Assert.InRange(bb.Max.Z, 15.2, 15.9);
            }

            var south = stripWalls.Where(bb => System.Math.Abs(bb.GetCentroid().Y - (-9.414)) < 0.5).ToList();
            var north = stripWalls.Where(bb => System.Math.Abs(bb.GetCentroid().Y - (-2.260)) < 0.5).ToList();
            Assert.NotEmpty(south);
            Assert.NotEmpty(north);
            Assert.All(south, bb => Assert.True(bb.Min.X <= -0.34,
                string.Format("South wall must reach the tower-face plane x=-0.345; MinX={0:F4}.", bb.Min.X)));
            Assert.All(north, bb => Assert.True(bb.Min.X <= -0.34,
                string.Format("North wall must reach the tower-face plane x=-0.345; MinX={0:F4}.", bb.Min.X)));
        }
    }
}
