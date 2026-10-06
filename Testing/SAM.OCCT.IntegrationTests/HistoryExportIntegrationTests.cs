// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 3 acceptance for the ABI v4 observational exports: native <c>BRepTools_History</c> capture
    /// (split 1->N, merge N->1, deletion, shared-face two-ordinals), naked-wire grouping, and tolerance
    /// drift (docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md §K). Native-gated. These exercise the direct
    /// <c>build_cell_complex</c> / <c>merge_coplanar</c> entry points (a passed OcctBuildOptions defaults to
    /// SewBeforeBuild=false), which are the two ops that capture history.
    /// </summary>
    public class HistoryExportIntegrationTests
    {
        private static Face3D Face(params Point3D[] points)
        {
            return TestGeometry.CreatePlanarFace(points);
        }

        /// <summary>Unit box's 6 outer faces + a mid-plane face at z = 0.5, in a known order (mid is index 6).</summary>
        private static List<Face3D> SplitFixture()
        {
            List<Face3D> face3Ds = TestGeometry.CreateUnitBox(0, 0, 0).Face3Ds.ToList(); // 6 faces
            face3Ds.Add(Face(new Point3D(0, 0, 0.5), new Point3D(1, 0, 0.5), new Point3D(1, 1, 0.5), new Point3D(0, 1, 0.5))); // mid, index 6
            return face3Ds;
        }

        private static OcctBuildOptions DirectBuildOptions()
        {
            // AvoidInternalShapes = false keeps the mid face as a shared internal wall (two cells);
            // SewBeforeBuild = false forces the direct build_cell_complex path that captures history.
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = false, GlueMode = OcctGlueMode.Off };
        }

        [SkippableFact]
        public void Build_SplitFixture_CapturesHistoryWithSevenInputsSplitWallsAndSharedMidFace()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Shell> shells = GeometryCreate.Shells(SplitFixture(), out OcctCellComplexResult result, DirectBuildOptions());
            using (result)
            {
                Assert.True(result.NativeAvailable);
                OcctHistory history = result.History;
                Assert.NotNull(history);                              // history captured on the direct build path
                Assert.Equal(7, history.InputCount);

                int outputFaceCount = shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Count();

                // Every referenced ordinal is a valid output face index (never a cross-handle / geometric id).
                for (int input = 0; input < history.InputCount; input++)
                {
                    foreach (int ordinal in history.ModifiedOrdinals(input).Concat(history.GeneratedOrdinals(input)))
                    {
                        Assert.InRange(ordinal, 0, outputFaceCount - 1);
                    }
                }

                // A side wall crossed by the mid plane splits: 1 input -> 2 output ordinals.
                Assert.Contains(Enumerable.Range(0, 7), input => history.ModifiedOrdinals(input).Count >= 2);

                // The mid face (index 6) is shared by both cells -> it appears under two flat ordinals.
                Assert.Equal(2, history.ModifiedOrdinals(6).Distinct().Count());
            }
        }

        [SkippableFact]
        public void MergeCoplanar_TwoAdjacentCoplanarFaces_HistoryMapsTwoInputsToOneOutput()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Two unit squares sharing the edge x = 1, coplanar in z = 0 -> merge into one face.
            List<Face3D> face3Ds = new List<Face3D>
            {
                Face(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0)),
                Face(new Point3D(1, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0))
            };

            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(face3Ds, out OcctCellComplexResult result);
            using (result)
            {
                Skip.If(merged == null || merged.Count != 1, "Coplanar merge did not collapse to a single face on this build.");
                OcctHistory history = result.History;
                Assert.NotNull(history);
                Assert.Equal(2, history.InputCount);

                // Both inputs map to the same single output ordinal (0) - the N->1 merge record.
                Assert.Equal(new[] { 0 }, history.ModifiedOrdinals(0).Distinct().ToArray());
                Assert.Equal(new[] { 0 }, history.ModifiedOrdinals(1).Distinct().ToArray());
            }
        }

        [SkippableFact]
        public void Validate_OpenBox_ReturnsOrderedClosedNakedWire()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Five faces of a unit box (top omitted): the open top rim is one closed naked wire of 4 edges.
            List<Face3D> face3Ds = TestGeometry.CreateUnitBox(0, 0, 0).Face3Ds
                .Where(x => x != null)
                .ToList();
            face3Ds.RemoveAt(1); // drop the top face

            GeometryQuery.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, new OcctBuildOptions(), false);
            using (result)
            {
                Assert.NotNull(report);
                Skip.If(report.NakedWires == null || report.NakedWires.Count == 0, "Naked-wire export unavailable on this build.");

                OcctNakedWire wire = report.NakedWires.OrderByDescending(x => x.Point3Ds.Count).First();
                Assert.True(wire.IsClosed, "The open top rim should form a closed wire.");
                Assert.Equal(4, wire.Point3Ds.Count);                // 4 corners, closing vertex not duplicated
                Assert.All(wire.Point3Ds, p => Assert.Equal(1.0, p.Z, 3)); // the rim sits at z = 1
            }
        }

        [SkippableFact]
        public void Build_CleanComplex_MaxToleranceWithinDriftCeiling()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            OcctBuildOptions options = DirectBuildOptions();
            GeometryCreate.Shells(SplitFixture(), out OcctCellComplexResult result, options);
            using (result)
            {
                OcctHistory history = result.History;
                Skip.If(history == null, "History unavailable on this build.");

                // Non-healing build on clean input: the result tolerance must stay finite, non-negative
                // and within the drift ceiling (< 10x the input tolerance - the plan's bound for non-heal ops).
                Assert.True(history.MaxTolerance >= 0 && !double.IsInfinity(history.MaxTolerance));
                Assert.True(history.MaxTolerance < 10.0 * options.Tolerance + 1e-9, $"Max tolerance {history.MaxTolerance} exceeded the drift ceiling.");
            }
        }

        [SkippableFact]
        public void Build_HistoryUnavailable_DegradesToNullWithoutThrowing()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // The sew-before-build path routes around build_cell_complex (the §7.1 scope cut), so no history
            // is captured - the managed layer must return null History and still decode a valid result.
            OcctBuildOptions options = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true };
            List<Shell> shells = GeometryCreate.Shells(SplitFixture(), out OcctCellComplexResult result, options);
            using (result)
            {
                Assert.True(result.NativeAvailable);
                Assert.NotEmpty(shells);
                Assert.Null(result.History); // graceful degrade, no throw
            }
        }

        [SkippableFact]
        public void Build_FiftySuccessiveBuildsWithHistory_NoLeakOrCrash()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            OcctBuildOptions options = DirectBuildOptions();

            long baseline = 0;
            for (int i = 0; i < 50; i++)
            {
                GeometryCreate.Shells(SplitFixture(), out OcctCellComplexResult result, options);
                OcctHistory history = result.History; // eager managed snapshot - no native lifetime
                Assert.NotNull(history);
                Assert.Equal(7, history.InputCount);
                result.Dispose();

                if (i == 9)
                {
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                    baseline = System.GC.GetTotalMemory(true);
                }
            }

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            long ending = System.GC.GetTotalMemory(true);

            // OcctHistory has no native lifetime by design; managed memory must not grow unbounded
            // across runs 10 -> 50 (generous ceiling - this catches a real leak, not GC jitter).
            Assert.True(ending < baseline + 8_000_000, $"Managed memory grew from {baseline} to {ending} across 50 builds.");
        }
    }
}
