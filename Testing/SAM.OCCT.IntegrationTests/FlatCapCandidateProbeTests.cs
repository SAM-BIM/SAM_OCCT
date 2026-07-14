// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>Temporary probe (towers gap-0.5 investigation): which walls lose their cap targets
    /// on whole-level-flat under the sample-point containment gate.</summary>
    public class FlatCapCandidateProbeTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public FlatCapCandidateProbeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string name)
        {
            string path = Path.Combine(FixturesDirectory, name);
            var result = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) result.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) result.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) result.Add(p);
            }
            return result;
        }

        [Theory]
        [InlineData("whole-level-flat.sam")]
        [InlineData("two-level-tilted.sam")]
        [InlineData("whole-level-towers.sam")]
        public void Flat_NoTargetWalls_Dump(string fixture)
        {
            var panels = LoadPanels(fixture);
            Assert.NotEmpty(panels);

            panels.Extend3D(out _, out Solve3DReport report);
            var records = report?.ExtendRecords?.ToList() ?? new List<ExtendRecord>();

            int noTarget = 0;
            foreach (var rec in records)
            {
                if (rec.SkipReason != ExtendSkipReason.NoTargetWithinReach) continue;
                if (rec.Kind != ExtendOperationKind.Top && rec.Kind != ExtendOperationKind.Bottom) continue;
                noTarget++;
                var src = rec.SourceIndex >= 0 && rec.SourceIndex < (report.Sources?.Count ?? 0) ? report.Sources[rec.SourceIndex] : null;
                var bb = src?.GetFace3D()?.GetBoundingBox();
                output.WriteLine("NOTARGET {0} src={1} wall x=[{2:F3},{3:F3}] y=[{4:F3},{5:F3}] z=[{6:F3},{7:F3}]",
                    rec.Kind, rec.SourceIndex,
                    bb?.Min.X ?? double.NaN, bb?.Max.X ?? double.NaN,
                    bb?.Min.Y ?? double.NaN, bb?.Max.Y ?? double.NaN,
                    bb?.Min.Z ?? double.NaN, bb?.Max.Z ?? double.NaN);
            }
            output.WriteLine("total NoTargetWithinReach (top+bottom): {0}", noTarget);

            // Replicate Stage B from the CLEAN faces (exact Execute re-registration) up to the moment
            // wall-to-cap extension evaluates candidacy, and diff the OLD (bbox-overlap) gate against
            // the NEW (sample-point containment) gate per vertical panel and direction.
            double tol = Tolerance.Distance;
            double verticalAngle = 20 * (System.Math.PI / 180);
            var cleanFaces = report.CleanFace3Ds?.ToList() ?? new List<Face3D>();
            var panelsB = new List<SnappedPanel>();
            for (int i = 0; i < cleanFaces.Count; i++)
            {
                panelsB.Add(new SnappedPanel(i, cleanFaces[i], Panel3DSnapSolver.DEFAULT_Weight,
                    Panel3DSnapSolver.DEFAULT_BucketSize, Panel3DSnapSolver.DEFAULT_MaxExtension));
            }
            var tolBudget = new ToleranceBudget();
            Panel3DSnapSolver.ExtendWalls(panelsB, tolBudget.VerticalAngle, 0.05, tolBudget.Distance);

            output.WriteLine("");
            output.WriteLine("walls whose OLD-gate candidacy differs from NEW-gate (post-ExtendWalls clean geometry):");
            for (int w = 0; w < panelsB.Count; w++)
            {
                var wallPanel = panelsB[w];
                if (wallPanel?.Face3D == null || !wallPanel.IsVertical(verticalAngle)) continue;
                var wb = wallPanel.GetBoundingBox();
                if (wb == null) continue;
                double cx = 0.5 * (wb.Min.X + wb.Max.X);
                double cy = 0.5 * (wb.Min.Y + wb.Max.Y);

                foreach (bool up in new[] { true, false })
                {
                    double wallExtreme = up ? wb.Max.Z : wb.Min.Z;
                    int bestOld = -1, bestNew = -1;
                    double bestOldZ = up ? double.MaxValue : double.MinValue;
                    double bestNewZ = up ? double.MaxValue : double.MinValue;
                    for (int i = 0; i < panelsB.Count; i++)
                    {
                        var p = panelsB[i];
                        if (p?.Face3D == null || i == w || p.IsVertical(verticalAngle)) continue;
                        var bb = p.GetBoundingBox();
                        if (bb == null) continue;
                        bool overlaps = bb.Min.X <= wb.Max.X + tol && bb.Max.X >= wb.Min.X - tol
                            && bb.Min.Y <= wb.Max.Y + tol && bb.Max.Y >= wb.Min.Y - tol;
                        if (!overlaps) continue;
                        var n = p.Plane?.Normal?.Unit;
                        var o = p.Plane?.Origin;
                        double capZ = n == null || o == null || System.Math.Abs(n.Z) <= 1e-9
                            ? (up ? bb.Max.Z : bb.Min.Z)
                            : o.Z - (n.X * (cx - o.X) + n.Y * (cy - o.Y)) / n.Z;
                        if (up ? capZ < wallExtreme - tol : capZ > wallExtreme + tol) continue;
                        if (up ? capZ < bestOldZ : capZ > bestOldZ) { bestOldZ = capZ; bestOld = i; }
                        bool contained = cx >= bb.Min.X - tol && cx <= bb.Max.X + tol
                            && cy >= bb.Min.Y - tol && cy <= bb.Max.Y + tol;
                        bool admissible = contained || System.Math.Abs(capZ - wallExtreme) <= 0.5; // banded graze rule
                        if (admissible && (up ? capZ < bestNewZ : capZ > bestNewZ)) { bestNewZ = capZ; bestNew = i; }
                    }

                    if (bestOld == bestNew) continue;
                    var oldBB = bestOld >= 0 ? panelsB[bestOld].GetBoundingBox() : null;
                    double missX = oldBB == null ? double.NaN : System.Math.Max(oldBB.Min.X - cx, cx - oldBB.Max.X);
                    double missY = oldBB == null ? double.NaN : System.Math.Max(oldBB.Min.Y - cy, cy - oldBB.Max.Y);
                    output.WriteLine("  panelB[{0,3}] {1,-6} centre=({2:F4},{3:F4}) z=[{4:F3},{5:F3}] OLD cap[{6}] z={7:F3} → NEW cap[{8}] z={9:F3}  oldMissX={10:F4} oldMissY={11:F4}",
                        w, up ? "TOP" : "BOTTOM", cx, cy, wb.Min.Z, wb.Max.Z,
                        bestOld, bestOld >= 0 ? bestOldZ : double.NaN,
                        bestNew, bestNew >= 0 ? bestNewZ : double.NaN,
                        missX, missY);
                }
            }
        }
    }
}
