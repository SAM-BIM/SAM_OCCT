// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated fixtures for Phase 5b wire-based gap filling + FinalizeAndValidate
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §G/§H/§J fixtures 4 &amp; 5). Exercise the real
    /// kernel: a planar patch closing an open-top box through the consolidation rebuild (final naked 0),
    /// and a non-planar rim taking the tagged fan fallback with the final validation reporting truthfully.
    /// </summary>
    public class GapFillIntegrationTests
    {
        /// <summary>An open-top box: floor + 4 walls of a 4x4x3 m room, no ceiling (the top rim is one
        /// closed planar naked loop the gap-fill must patch).</summary>
        private static List<Face3D> OpenTopBox()
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)), // floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)), // y=0
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3)), // y=4
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3)), // x=0
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)), // x=4
            };
        }

        /// <summary>An open-top box whose top rim is NON-planar: the (0,4) top corner is raised 0.1 m, so the
        /// four top edges do not share a plane - the fan-fallback case.</summary>
        private static List<Face3D> RaisedRimBox()
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)), // floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)),   // y=0 (top z=3,3)
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)),   // x=4 (top z=3,3)
                TestGeometry.CreatePlanarFace(new Point3D(4, 4, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3.1), new Point3D(4, 4, 3)), // y=4 (top z=3.1,3)
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(0, 0, 0), new Point3D(0, 0, 3), new Point3D(0, 4, 3.1)), // x=0 (top z=3,3.1)
            };
        }

        [SkippableFact]
        public void Execute_OpenTopBox_PlanarPatchClosesRoomWithZeroFinalNaked()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act - the raw solve cannot close an open box (no cell), so the managed pipeline runs,
            // the top rim is gap-filled with a planar patch, and the consolidation rebuild closes the room.
            Panel3DSnapSolver solver = new Panel3DSnapSolver(OpenTopBox());
            solver.Execute(new OcctBuildOptions());

            // Assert - the room closed into a cell and the FINAL (post-patch) validation reports zero naked edges.
            Assert.True(solver.NativeResolved, "Expected the native resolve to run");
            Assert.True(solver.ResolvedCellCount >= 1, $"Expected the patch to close the room into a cell, got {solver.ResolvedCellCount}");
            Assert.Empty(solver.NakedEdgePoint3Ds);
            // A gap-fill patch was produced (the top lid) ...
            Assert.NotEmpty(solver.HoleFillFace3Ds);
            // ... and it is a single planar patch, diagnosed (Info), not a fan fallback.
            Assert.Contains(solver.Diagnostics.All,
                d => d.Code == DiagnosticCode.NakedLoop && d.Severity == OcctDiagnosticSeverity.Info && d.Message.Contains("planar"));
        }

        [SkippableFact]
        public void Execute_NonPlanarRim_UsesFanFallbackAndValidatesTruthfully()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange & Act
            Panel3DSnapSolver solver = new Panel3DSnapSolver(RaisedRimBox());
            solver.Execute(new OcctBuildOptions());

            // Assert - the non-planar rim is fan-patched, and this is surfaced as a tagged warning
            // (never a silent success). The final validation still ran and produced a (truthful) result.
            Assert.True(solver.NativeResolved, "Expected the native resolve to run");
            Assert.NotEmpty(solver.HoleFillFace3Ds);
            Assert.Contains(solver.Diagnostics.All,
                d => d.Code == DiagnosticCode.NakedLoop && d.Severity == OcctDiagnosticSeverity.Warning && d.Message.Contains("fan-patched"));
            // The final naked count is whatever the post-patch validation measured - reported, not hidden.
            Assert.NotNull(solver.NakedEdgePoint3Ds);
            Assert.NotNull(solver.Signature);
        }
    }
}
