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
        /// under-split gate exists for. <paramref name="tiltDegrees"/> rotates the whole room about world Y so
        /// the room's OWN "up" (its true ceiling-ward direction) is tilted relative to world Z - the codex #7
        /// review-round-2 scenario: a naive world-Z measurement inflates the WORLD axis-aligned bounding box of
        /// a tilted room and can hide the under-split.</summary>
        private static List<Panel> DoorCutPartitionTwoRoom(double tiltDegrees = 0)
        {
            double angle = tiltDegrees * System.Math.PI / 180.0;
            Point3D P(double x, double y, double z)
            {
                if (angle == 0)
                {
                    return new Point3D(x, y, z);
                }

                double c = System.Math.Cos(angle);
                double s = System.Math.Sin(angle);
                return new Point3D((x * c) + (z * s), y, (-x * s) + (z * c)); // rotate about world Y
            }

            return new List<Panel>
            {
                Slab(Rect(P(0, 0, 0), P(8, 0, 0), P(8, 4, 0), P(0, 4, 0)), PanelType.Floor),
                Slab(Rect(P(0, 0, 3), P(8, 0, 3), P(8, 4, 3), P(0, 4, 3)), PanelType.Roof),
                Wall(Rect(P(0, 0, 0), P(8, 0, 0), P(8, 0, 3), P(0, 0, 3))), // y=0
                Wall(Rect(P(0, 4, 0), P(8, 4, 0), P(8, 4, 3), P(0, 4, 3))), // y=4
                Wall(Rect(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))), // x=0
                Wall(Rect(P(8, 0, 0), P(8, 4, 0), P(8, 4, 3), P(8, 0, 3))), // x=8
                Wall(Rect(P(4, 0, 0), P(4, 4, 0), P(4, 4, 2.5), P(4, 0, 2.5))), // partition, 0.5 short of ceiling
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

        [SkippableFact]
        public void Solve3D_TiltedDoorCutPartition_RejectsRawUnderSplitAndManagedSeparatesRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Codex #7 review, round 2: the SAME door-cut room, rigidly tilted 30 deg about world Y. Solve3D
            // auto-detects the level normal and sets Panel3DSnapSolver.Up to it, so this exercises the
            // under-split gate on a genuinely tilted level (not just world-Z-vertical geometry) - the exact
            // scenario a naive world-axis-aligned-bounding-box measurement gets wrong (the room's world AABB is
            // inflated by the tilt, which would hide the under-split if height/plan were measured against it
            // instead of the level's own frame).
            List<Panel> panels = DoorCutPartitionTwoRoom(tiltDegrees: 30);
            panels.Solve3D(out List<Point3D> _, out List<string> diagnostics, out _, out Solve3DReport report);

            Assert.False(report.RawAdopted);
            Assert.Contains(diagnostics, d => d.Contains("UnderSplit") && d.Contains("under-split"));
            Assert.Equal(2, report.ResolvedCellCount);
            Assert.Empty(report.NakedWires);
        }

        /// <summary>The door-cut room rotated <paramref name="yawDegrees"/> about world Z (plan yaw only - the
        /// level stays flat, Up stays world Z). Distinct from <see cref="DoorCutPartitionTwoRoom"/>'s
        /// <c>tiltDegrees</c> (which rotates about a HORIZONTAL axis, tilting Up itself): a pure yaw leaves Up
        /// untouched but rotates the room's footprint away from the world/level X/Y axes, which inflates any
        /// AXIS-ALIGNED bounding box (world OR level-frame) even though Up needs no correction at all - the
        /// codex #7 review, round 3 scenario.</summary>
        private static List<Panel> DoorCutPartitionTwoRoomYawed(double yawDegrees)
        {
            double angle = yawDegrees * System.Math.PI / 180.0;
            double c = System.Math.Cos(angle);
            double s = System.Math.Sin(angle);
            Point3D P(double x, double y, double z) => new Point3D((x * c) - (y * s), (x * s) + (y * c), z);

            return new List<Panel>
            {
                Slab(Rect(P(0, 0, 0), P(8, 0, 0), P(8, 4, 0), P(0, 4, 0)), PanelType.Floor),
                Slab(Rect(P(0, 0, 3), P(8, 0, 3), P(8, 4, 3), P(0, 4, 3)), PanelType.Roof),
                Wall(Rect(P(0, 0, 0), P(8, 0, 0), P(8, 0, 3), P(0, 0, 3))),
                Wall(Rect(P(0, 4, 0), P(8, 4, 0), P(8, 4, 3), P(0, 4, 3))),
                Wall(Rect(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                Wall(Rect(P(8, 0, 0), P(8, 4, 0), P(8, 4, 3), P(8, 0, 3))),
                Wall(Rect(P(4, 0, 0), P(4, 4, 0), P(4, 4, 2.5), P(4, 0, 2.5))),
            };
        }

        [SkippableFact]
        public void Solve3D_YawedDoorCutPartition_RejectsRawUnderSplitAndManagedSeparatesRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Codex #7 review, round 3: the door-cut room rotated 45 deg in PLAN (about world Z - Up stays
            // world Z, untilted). An axis-aligned bounding box of the rotated 8x4 footprint is inflated toward
            // its diagonal (~8.9 m) versus the room's true 8 m/4 m dimensions, which understates the dropped
            // partition's plan-width RATIO against that inflated box and can hide the under-split even though
            // Up itself needed no correction. Exercises the vertex-projection fix (no bounding box anywhere).
            List<Panel> panels = DoorCutPartitionTwoRoomYawed(yawDegrees: 45);
            panels.Solve3D(out List<Point3D> _, out List<string> diagnostics, out _, out Solve3DReport report);

            Assert.False(report.RawAdopted);
            Assert.Contains(diagnostics, d => d.Contains("UnderSplit") && d.Contains("under-split"));
            Assert.Equal(2, report.ResolvedCellCount);
            Assert.Empty(report.NakedWires);
        }
    }
}
