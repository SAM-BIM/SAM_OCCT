// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P4 gate-hardening fixture (docs/CELLCOMPLEX_FIRST_HANDOVER.md §9): the codex #7 "under-split" hole -
    /// a raw build that adopts a watertight envelope as ONE cell even though an input partition should have
    /// divided it into two. Built programmatically so it is self-contained. Native-gated.
    ///
    /// Fail-before/pass-after evidence: with the under-split gate disabled the raw path adopts this fixture as
    /// <c>RawAdopted=True, 1 cell</c> (the two rooms silently merged); with it enabled the raw path is rejected
    /// (<c>SAM_OCCT_..._UnderSplit</c>) and the managed pipeline extends the short partition to the ceiling and
    /// separates the rooms into 2 cells. Verified by temporarily neutralising the gate during development.
    /// </summary>
    public class GateHardeningIntegrationTests
    {
        private static Face3D Rect(params Point3D[] pts) => TestGeometry.CreatePlanarFace(pts);

        private static Panel Wall(Face3D f) => global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, f);
        private static Panel Slab(Face3D f, PanelType t) => global::SAM.Analytical.Create.Panel(new Construction(t.ToString()), t, f);

        /// <summary>An 8x4x3 box (watertight envelope = one cell) plus a partition at x=4 that stops 0.5 m
        /// short of the ceiling - so it fails to divide the box and the raw build drops it, silently merging the
        /// two rooms into one watertight cell. Only one face of seven is dropped (ratio ~14%, well under the 30%
        /// dropped-ratio ceiling), so the coarse dropped-ratio check cannot see it - this is exactly the case the
        /// under-split gate exists for.</summary>
        private static List<Panel> DoorCutPartitionTwoRoom()
        {
            return new List<Panel>
            {
                Slab(Rect(new Point3D(0, 0, 0), new Point3D(8, 0, 0), new Point3D(8, 4, 0), new Point3D(0, 4, 0)), PanelType.Floor),
                Slab(Rect(new Point3D(0, 0, 3), new Point3D(8, 0, 3), new Point3D(8, 4, 3), new Point3D(0, 4, 3)), PanelType.Roof),
                Wall(Rect(new Point3D(0, 0, 0), new Point3D(8, 0, 0), new Point3D(8, 0, 3), new Point3D(0, 0, 3))), // y=0
                Wall(Rect(new Point3D(0, 4, 0), new Point3D(8, 4, 0), new Point3D(8, 4, 3), new Point3D(0, 4, 3))), // y=4
                Wall(Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3))), // x=0
                Wall(Rect(new Point3D(8, 0, 0), new Point3D(8, 4, 0), new Point3D(8, 4, 3), new Point3D(8, 0, 3))), // x=8
                Wall(Rect(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 2.5), new Point3D(4, 0, 2.5))), // partition, 0.5 short of ceiling
            };
        }

        [SkippableFact]
        public void Solve3D_DoorCutPartition_RejectsRawUnderSplitAndManagedSeparatesRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = DoorCutPartitionTwoRoom();
            panels.Solve3D(out List<Point3D> _, out List<string> diagnostics, out _, out Solve3DReport report);

            // The raw build is NOT adopted: the under-split gate fired (a partition dropped inside the one
            // adopted cell), emitting a coded diagnostic with the measured values.
            Assert.False(report.RawAdopted);
            Assert.Contains(diagnostics, d => d.Contains("UnderSplit") && d.Contains("under-split"));
            Assert.Contains(diagnostics, d => d.Contains("harbours a dropped") && d.Contains("partition"));

            // And the managed pipeline recovers the correct topology: the two rooms are separated (2 cells),
            // watertight. This is the "must reject raw, managed separates" acceptance (§9 task 3a).
            Assert.Equal(2, report.ResolvedCellCount);
            Assert.Empty(report.NakedWires);
        }

        [SkippableFact]
        public void Solve3D_LargeSingleRoomNoPartition_AdoptsRawAndGateStaysSilent()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // False-positive control (the Fable atrium/warehouse concern): a large single room with NO dropped
            // interior wall must be adopted raw untouched - the under-split gate keys on a dropped room-DIVIDING
            // partition, not on a cell merely being big. Same 8x4x3 box as the under-split fixture, minus the
            // partition, so any spurious firing here would be a direct false positive.
            List<Panel> panels = DoorCutPartitionTwoRoom();
            panels.RemoveAt(panels.Count - 1); // drop the partition -> a plain watertight box (one legitimate room)

            panels.Solve3D(out List<Point3D> _, out List<string> diagnostics, out _, out Solve3DReport report);

            Assert.True(report.RawAdopted);
            Assert.Equal(1, report.ResolvedCellCount);
            Assert.DoesNotContain(diagnostics, d => d.Contains("UnderSplit"));
        }
    }
}
