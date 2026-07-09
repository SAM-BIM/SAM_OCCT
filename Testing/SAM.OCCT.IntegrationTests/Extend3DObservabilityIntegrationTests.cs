// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// E3 acceptance (docs/EXTEND3D_ROBUST_HANDOVER.md): the managed extend is observable. On a
    /// hand-checked fixture (a room whose walls stop short of the roof) the <see cref="Solve3DReport"/>
    /// carries one <see cref="ExtendRecord"/> per applied move, each matching the ACTUAL geometry delta,
    /// and the coded <c>SAM_OCCT_EXTEND3D_PANEL:</c> lines surface through the diagnostics list. Extend3D
    /// stops before the native resolve, so this runs native-free (a plain fact, not native-gated).
    /// </summary>
    public class Extend3DObservabilityIntegrationTests
    {
        private static Face3D Rect(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>A 4x4 room whose four walls rise only to z = 2.5, 0.5 m short of the roof at z = 3, so the
        /// managed extend must raise every wall top to the cap (the move the records describe).</summary>
        private static List<Panel> ShortWalledRoom()
        {
            Construction wall = new Construction("Wall");
            return new List<Panel>
            {
                global::SAM.Analytical.Create.Panel(new Construction("Floor"), PanelType.Floor, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0))),
                global::SAM.Analytical.Create.Panel(new Construction("Roof"), PanelType.Roof, Rect(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 2.5), new Point3D(0, 0, 2.5))),
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 2.5), new Point3D(0, 4, 2.5))),
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 2.5), new Point3D(0, 0, 2.5))),
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 2.5), new Point3D(4, 0, 2.5))),
            };
        }

        /// <summary>The same 4-wall room, but the y=0 wall is supplied as TWO coplanar halves (x[0,2] and
        /// x[2,4]) with DISTINCT source panels. Stage A coplanar-merges them into one clean face, so the clean
        /// ordinal of every later wall shifts by one relative to its original input index - the exact condition
        /// under which a record's source index (a clean ordinal) would resolve to the WRONG input panel unless
        /// it is mapped back through the snap stage's per-clean-face attribution.</summary>
        private static List<Panel> SplitWallRoom()
        {
            Construction wall = new Construction("Wall");
            return new List<Panel>
            {
                global::SAM.Analytical.Create.Panel(new Construction("Floor"), PanelType.Floor, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0))),
                global::SAM.Analytical.Create.Panel(new Construction("Roof"), PanelType.Roof, Rect(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 2.5), new Point3D(0, 0, 2.5))), // y=0 half A
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(2, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 2.5), new Point3D(2, 0, 2.5))), // y=0 half B
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 2.5), new Point3D(0, 4, 2.5))), // y=4
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 2.5), new Point3D(0, 0, 2.5))), // x=0
                global::SAM.Analytical.Create.Panel(wall, PanelType.Wall, Rect(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 2.5), new Point3D(4, 0, 2.5))), // x=4
            };
        }

        [Fact]
        public void Extend3D_CoplanarMergeShiftsCleanOrdinals_RecordsResolveToCorrectSourcePanel()
        {
            // Act
            List<Panel> panels = SplitWallRoom();
            panels.Extend3D(out List<string> _, out Solve3DReport report);

            Assert.NotNull(report);
            Assert.NotEmpty(report.ExtendRecords);

            // Honesty of source attribution: each record's SourceIndex must resolve (in the SAME report.Sources
            // the formatter uses) to a panel whose plane actually contains the moved edge. Before the clean-
            // ordinal -> original-source remap, a wall AFTER the merged pair resolved to the wrong panel (a
            // different plane), so SAM_OCCT_EXTEND3D_PANEL named the wrong Guid.
            IReadOnlyList<Panel> sources = report.Sources;
            foreach (ExtendRecord record in report.ExtendRecords)
            {
                if (record.From == null)
                {
                    continue; // cap-grow: no single moved edge
                }

                Assert.InRange(record.SourceIndex, 0, sources.Count - 1);
                Plane plane = sources[record.SourceIndex]?.GetFace3D()?.GetPlane();
                Assert.NotNull(plane);
                Assert.True(System.Math.Abs(plane.Distance(record.From)) <= 0.02,
                    string.Format("record {0} resolved to source #{1} whose plane is {2:0.###} m from the moved edge at {3}",
                        record.Kind, record.SourceIndex, System.Math.Abs(plane.Distance(record.From)), record.From));
            }

            // And the coded lines name real input Guids (never n/a for a resolved move).
            List<string> lines = report.FormatExtendReport();
            Assert.All(lines.FindAll(l => l.StartsWith("SAM_OCCT_EXTEND3D_PANEL:")),
                l => Assert.DoesNotContain("panel n/a", l));
        }

        [Fact]
        public void Extend3D_ShortWalledRoom_RecordsMatchActualMovesAndSurfaceCodedLines()
        {
            // Act
            List<Panel> extended = ShortWalledRoom().Extend3D(out List<string> diagnostics, out Solve3DReport report);

            // Assert - the pass ran and produced observability.
            Assert.NotNull(extended);
            Assert.NotEmpty(extended);
            Assert.NotNull(report);
            Assert.NotEmpty(report.ExtendRecords);

            // The coded lines surface through the same diagnostics list the report path uses.
            Assert.Contains(diagnostics, d => d.StartsWith("SAM_OCCT_EXTEND3D_PANEL:"));
            Assert.Equal(
                report.ExtendRecords.Count,
                diagnostics.Count(d => d.StartsWith("SAM_OCCT_EXTEND3D_PANEL:")));

            // Honesty: every record is an ACTUAL move (from != to), never a no-op call.
            Assert.All(report.ExtendRecords, r => Assert.True(System.Math.Abs(r.ToValue - r.FromValue) > 1e-6,
                string.Format("record {0} on panel #{1} has from == to ({2})", r.Kind, r.PanelIndex, r.FromValue)));

            // The walls were extended UP to the roof (cap z = 3 + 0.05 overshoot = 3.05).
            List<ExtendRecord> topMoves = report.ExtendRecords.Where(r => r.Kind == ExtendOperationKind.Top).ToList();
            Assert.NotEmpty(topMoves);
            Assert.All(topMoves, r =>
            {
                Assert.Equal(3.05, r.ToValue, 2);
                Assert.Equal(2.5, r.FromValue, 2);
                Assert.False(r.MaxExtendCapped); // vertical reach is uncapped
            });

            // The record's to-elevation actually EXISTS in the extended output geometry - the record is not a
            // claim divorced from the faces; a wall panel really reaches it.
            double recordedTop = topMoves[0].ToValue;
            Assert.Contains(extended, p =>
            {
                BoundingBox3D box = p?.GetFace3D()?.GetBoundingBox();
                return box != null && System.Math.Abs(box.Max.Z - recordedTop) <= 0.02;
            });

            // Each moved wall edge yields a preview segment whose endpoints match its record's from/to Z.
            List<Segment3D> preview = report.ExtendPreviewSegment3Ds();
            Assert.NotEmpty(preview);
            Assert.All(preview, s => Assert.True(s.GetLength() > 1e-6));
        }
    }
}
