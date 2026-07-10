// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using SAM.Core.OCCT;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="CleanRecord"/> and the Stage A clean recorder (docs/CONTROLLED_WORKFLOW_PLAN.md
    /// §4.4). Pure-managed. The headline guarantee: a NULL recorder is byte-identical to the geometry - proven by
    /// running the same clean twice, once with a null recorder and once with a list, and asserting the clean
    /// faces are identical. Also covers that the real mutation sites (bucket snap, cap normalization) emit their
    /// records.
    /// </summary>
    public class CleanRecordTests
    {
        /// <summary>A vertical wall in the plane x = <paramref name="x"/>, y in [y0,y1], z in [zBottom,zTop].</summary>
        private static Face3D Wall(double x, double y0, double y1, double zBottom, double zTop)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, y0, zBottom),
                new Point3D(x, y1, zBottom),
                new Point3D(x, y1, zTop),
                new Point3D(x, y0, zTop));
        }

        /// <summary>A horizontal cap tile [x0,x0+w] x [y0,y0+d] at elevation z.</summary>
        private static Face3D Cap(double x0, double y0, double w, double d, double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, z),
                new Point3D(x0 + w, y0, z),
                new Point3D(x0 + w, y0 + d, z),
                new Point3D(x0, y0 + d, z));
        }

        /// <summary>A fresh panel list that (a) contains two overlapping parallel walls of unequal weight within
        /// one bucket - the weighted snap collapses the lighter onto the backer (SnappedToBacker); and (b) two
        /// edge-adjacent (non-overlapping) caps within the frame band at different elevations - normalized onto
        /// one datum (CapNormalized). A fresh list each call so two runs are independent (Clean mutates in place).</summary>
        private static List<SnappedPanel> Scenario()
        {
            return new List<SnappedPanel>
            {
                new SnappedPanel(0, Wall(0.0, 0, 2, 0, 3), 1.0, 0.4, 0.4),  // wall backer
                new SnappedPanel(1, Wall(0.1, 0, 2, 0, 3), 0.5, 0.4, 0.4),  // lighter parallel wall -> snaps onto #0
                new SnappedPanel(2, Cap(0, 0, 10, 10, 0.0), 1.0, 0.4, 0.4), // dominant cap datum
                new SnappedPanel(3, Cap(11, 0, 2, 2, 0.1), 1.0, 0.4, 0.4),  // adjacent cap 0.1 m up -> normalized
            };
        }

        /// <summary>Serializes every external/internal loop to the exact coordinate/count arrays handed to OCCT.
        /// Equality of these arrays is a byte-level geometry check: vertices, winding, face order and holes all
        /// participate, unlike an area/centroid fingerprint.</summary>
        private static OcctNativeInput Serialize(IEnumerable<Face3D> face3Ds)
        {
            OcctCellComplexResult result = new OcctCellComplexResult();
            try
            {
                Assert.True(OcctNativeInputBuilder.TryBuild(face3Ds, new OcctBuildOptions(), result, out OcctNativeInput input));
                return input;
            }
            finally
            {
                result.Dispose();
            }
        }

        [Fact]
        public void Clean_NullRecorderVsRecorder_ProducesIdenticalGeometry()
        {
            // Arrange - two independent copies of the same scenario.
            List<SnappedPanel> withoutRecorder = Scenario();
            List<SnappedPanel> withRecorder = Scenario();
            List<CleanRecord> records = new List<CleanRecord>();

            // Act - one clean with a null recorder, one with a live recorder.
            SnapStage.Result a = SnapStage.Clean(withoutRecorder, new ToleranceBudget(), 0.3, 0.3, null, null, 0.0, null);
            SnapStage.Result b = SnapStage.Clean(withRecorder, new ToleranceBudget(), 0.3, 0.3, null, null, 0.0, records);

            // Assert - the recorder changed no face/loop/coordinate byte; and it DID observe something (so the
            // parity is meaningful, not a comparison of two empty runs).
            OcctNativeInput geometryA = Serialize(a.CleanFace3Ds);
            OcctNativeInput geometryB = Serialize(b.CleanFace3Ds);
            Assert.Equal(geometryA.FaceCount, geometryB.FaceCount);
            Assert.Equal(geometryA.FaceLoopCounts, geometryB.FaceLoopCounts);
            Assert.Equal(geometryA.LoopPointCounts, geometryB.LoopPointCounts);
            Assert.Equal(geometryA.Coordinates, geometryB.Coordinates);
            Assert.NotEmpty(records);
        }

        [Fact]
        public void Clean_GroupBand021_Cap0196FromDatum_IsClaimedAndNormalizedToGroupDatum()
        {
            // Arrange - two non-overlapping cap tiles form separate RAW frames because 0.196 > the pinned 0.15
            // band. The larger 12.240 tile seeds the P2 group datum; the 12.436 skin must be claimed over 0.21.
            Face3D datumCap = Cap(0, 0, 10, 10, 12.240);
            Face3D skinCap = Cap(11, 0, 2, 2, 12.436);
            List<LevelFrame> rawFrames = LevelFrame.Cluster(new List<Face3D> { datumCap, skinCap });
            List<LevelGroup> groups = LevelFrame.GroupFrames(rawFrames, 0.21);
            List<LevelFrame> groupDatums = groups.Select(x => x.ToDatumFrame()).ToList();
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act - prove membership directly, then run the real Stage A normalization with a live recorder.
            int assigned = LevelFrame.AssignCapToFrame(skinCap, groupDatums, elevationBand: 0.21, diagnostics: diagnostics);
            FaceRole role = LevelFrame.ClassifyFace(skinCap, groupDatums, out int classifiedFrame, diagnostics: diagnostics, elevationBand: 0.21);
            List<CleanRecord> records = new List<CleanRecord>();
            SnapStage.Result result = SnapStage.Clean(
                new List<SnappedPanel>
                {
                    new SnappedPanel(0, datumCap, 1.0, 0.4, 0.4),
                    new SnappedPanel(1, skinCap, 1.0, 0.4, 0.4)
                },
                new ToleranceBudget(), 0.3, 0.3, diagnostics, null, 0.21, records);

            // Assert - this is assignment through the widened group band, not the nearest-centroid fallback;
            // both clean caps lie exactly on the dominant group datum and the 0.196 m move is recorded.
            Assert.Equal(2, rawFrames.Count);
            Assert.Single(groups);
            Assert.Equal(0, assigned);
            Assert.Equal(FaceRole.Cap, role);
            Assert.Equal(0, classifiedFrame);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.AmbiguousLevelFrame);
            Assert.All(result.CleanFace3Ds, x => Assert.Equal(12.240, x.GetBoundingBox().GetCentroid().Z, 6));
            Assert.Contains(records, x => x.Kind == CleanRecordKind.CapNormalized && x.SourceIndex == 1 && System.Math.Abs(x.DistanceMoved - 0.196) < 1e-9);
        }

        [Fact]
        public void Clean_ParallelWallsInBucket_RecordsSnappedToBacker()
        {
            // Arrange
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            SnapStage.Clean(Scenario(), new ToleranceBudget(), 0.3, 0.3, null, null, 0.0, records);

            // Assert - the lighter wall (#1) snapped onto the backer (#0) is recorded.
            Assert.Contains(records, r => r.Kind == CleanRecordKind.SnappedToBacker && r.SourceIndex == 1 && r.BackerSourceIndex == 0);
        }

        [Fact]
        public void Clean_AdjacentCapsWithinFrameBand_RecordsCapNormalized()
        {
            // Arrange
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            SnapStage.Clean(Scenario(), new ToleranceBudget(), 0.3, 0.3, null, null, 0.0, records);

            // Assert - the adjacent cap (#3) normalized onto the dominant datum (#2) is recorded, with a group.
            Assert.Contains(records, r => r.Kind == CleanRecordKind.CapNormalized && r.SourceIndex == 3);
        }

        [Fact]
        public void KindText_EachKind_ReturnsKebabCaseTag()
        {
            // Arrange & Act & Assert - the tags used in the SAM_OCCT_CLEAN3D_PANEL line.
            Assert.Equal("opposed-collapsed", new CleanRecord(0, 1, CleanRecordKind.OpposedCollapsed, 0.1).KindText());
            Assert.Equal("snapped-to-backer", new CleanRecord(0, 1, CleanRecordKind.SnappedToBacker, 0.1).KindText());
            Assert.Equal("cap-normalized", new CleanRecord(0, 1, CleanRecordKind.CapNormalized, 0.1).KindText());
            Assert.Equal("coplanar-merged", new CleanRecord(0, 1, CleanRecordKind.CoplanarMerged, 0.0).KindText());
            Assert.Equal("dropped-invalid", new CleanRecord(0, -1, CleanRecordKind.DroppedInvalid, 0.0).KindText());
            Assert.Equal("preserved", new CleanRecord(0, -1, CleanRecordKind.Preserved, 0.0).KindText());
        }
    }
}
