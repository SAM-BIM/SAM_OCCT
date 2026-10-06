// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 5d fixtures (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §F/§J.2/§J.3): the capped adaptive
    /// sew (<see cref="HealStage.SewV2"/>) run through the real native sew/validate. ParallelPairWeld proves a
    /// close (0.08 m) double wall caps the sew below half the gap so a global sew cannot fuse it; ShaftProtection
    /// proves a 0.3-0.4 m cavity is far wider than the sew tolerance and survives. Closing the unrelated wide
    /// gap is left to AutoTune (Phase 5e) - out of 5d scope. Native-gated.
    /// </summary>
    public class SewV2IntegrationTests
    {
        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
        }

        /// <summary>Number of near-axis-aligned vertical faces (normal ~±X) whose centroid sits within
        /// <paramref name="tolerance"/> of the plane x = <paramref name="x"/> - i.e. a surviving wall skin there.</summary>
        private static int VerticalFacesNearX(IEnumerable<Face3D> face3Ds, double x, double tolerance)
        {
            int count = 0;
            foreach (Face3D face3D in face3Ds)
            {
                Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
                Point3D centroid = face3D?.GetBoundingBox()?.GetCentroid();
                if (normal != null && centroid != null && System.Math.Abs(normal.X) > 0.99 && System.Math.Abs(centroid.X - x) < tolerance)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// An open-top room (walls + floor, no ceiling -&gt; naked edges so the sew runs) with a partition
        /// modelled as a double wall - two parallel skins <paramref name="wallGap"/> m apart at x = 2 - and one
        /// exterior wall lifted 0.09 m off the floor (an unrelated residual slot the sew might otherwise close).
        /// </summary>
        private static List<Face3D> RoomWithDoubleWall(double wallGap)
        {
            double x2 = 2.0, x2b = 2.0 + wallGap;
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 3, 0), new Point3D(0, 3, 0)),     // floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)),     // back wall y=0
                TestGeometry.CreatePlanarFace(new Point3D(0, 3, 0.09), new Point3D(4, 3, 0.09), new Point3D(4, 3, 3), new Point3D(0, 3, 3)), // front wall y=3, 0.09 m slot above floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 3, 0), new Point3D(0, 3, 3), new Point3D(0, 0, 3)),     // left wall x=0
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 3, 0), new Point3D(4, 3, 3), new Point3D(4, 0, 3)),     // right wall x=4
                TestGeometry.CreatePlanarFace(new Point3D(x2, 0, 0), new Point3D(x2, 3, 0), new Point3D(x2, 3, 3), new Point3D(x2, 0, 3)), // partition skin A
                TestGeometry.CreatePlanarFace(new Point3D(x2b, 0, 0), new Point3D(x2b, 3, 0), new Point3D(x2b, 3, 3), new Point3D(x2b, 0, 3)) // partition skin B
            };
        }

        [SkippableFact]
        public void SewV2_CloseDoubleWall_CapsBelowHalfTheGapAndKeepsBothSkins()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a partition modelled as two skins 0.08 m apart, plus a 0.09 m residual slot.
            List<Face3D> face3Ds = RoomWithDoubleWall(wallGap: 0.08);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act - the default safety factor (0.5) caps the sew at 0.08 x 0.5 = 0.04 m.
            HealStage.SewResult sew = HealStage.SewV2(face3Ds, Options(), sewExpandTolerance: 0.1, sewSafetyFactor: 0.5, diagnostics: diagnostics);

            // Assert - the double wall was measured and the sew tolerance capped below it.
            Assert.NotNull(sew.MinSeparation);
            Assert.Equal(0.08, sew.MinSeparation.Value, 3);
            Assert.True(sew.SewTolerance <= 0.04 + 1e-9, $"expected the sew capped <= 0.04 m, got {sew.SewTolerance}");

            // The partition survives: BOTH parallel skins are still present (the capped sew cannot bridge the
            // 0.08 m gap, so it never fuses them), and the fusion veto did not need to fire.
            Assert.True(VerticalFacesNearX(sew.ResolvedFace3Ds, 2.0, 0.02) >= 1, "skin A (x=2.00) missing");
            Assert.True(VerticalFacesNearX(sew.ResolvedFace3Ds, 2.08, 0.02) >= 1, "skin B (x=2.08) missing");
            Assert.False(sew.FusionVetoed);
        }

        [SkippableFact]
        public void SewV2_CloseDoubleWall_LeavesTheUnrelatedSlotForAutoTune()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - the 0.08 m double wall caps the sew at 0.04 m, which is also too tight to weld the
            // unrelated 0.09 m slot: that gap is therefore left open (AutoTune closes it in Phase 5e).
            List<Face3D> face3Ds = RoomWithDoubleWall(wallGap: 0.08);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.SewResult sew = HealStage.SewV2(face3Ds, Options(), sewExpandTolerance: 0.1, sewSafetyFactor: 0.5, diagnostics: diagnostics);

            // Assert - the capped sew did not close the wider slot, so it was not adopted (naked did not
            // strictly drop); the pre-sew faces are kept for the next stage. Closure is deferred to AutoTune,
            // not forced by an unsafe sew.
            Assert.False(sew.Adopted);
            Assert.True(sew.NakedAfter >= sew.NakedBefore, $"a capped, unadopted sew must not strictly reduce naked (before {sew.NakedBefore}, after {sew.NakedAfter})");
        }

        [SkippableFact]
        public void SewV2_WideShaftVoid_IsFarWiderThanTheSewToleranceAndSurvives()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a 0.35 m shaft cavity (two parallel skins at x=2.0 and x=2.35) in an open room. The cap
            // is min(0.1, 0.35 x 0.5) = 0.1 m, far below the 0.35 m gap, so the sew cannot collapse the shaft.
            List<Face3D> face3Ds = RoomWithDoubleWall(wallGap: 0.35);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.SewResult sew = HealStage.SewV2(face3Ds, Options(), sewExpandTolerance: 0.1, sewSafetyFactor: 0.5, diagnostics: diagnostics);

            // Assert - the sew tolerance stays well under the shaft width, and both shaft faces survive.
            Assert.True(sew.SewTolerance <= 0.1 + 1e-9, $"expected the sew <= 0.1 m, got {sew.SewTolerance}");
            Assert.True(sew.SewTolerance < 0.35, "the sew must stay below the shaft width");
            Assert.True(VerticalFacesNearX(sew.ResolvedFace3Ds, 2.0, 0.02) >= 1, "shaft skin A (x=2.00) missing");
            Assert.True(VerticalFacesNearX(sew.ResolvedFace3Ds, 2.35, 0.02) >= 1, "shaft skin B (x=2.35) missing");
            Assert.False(sew.FusionVetoed);
        }
    }
}
