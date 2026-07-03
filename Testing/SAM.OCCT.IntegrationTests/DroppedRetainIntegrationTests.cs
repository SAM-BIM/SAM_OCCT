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
    /// Phase 5c fixture (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §J.6 "DroppedRetain"): a floating
    /// interior shelf face that bounds no closed cell, so the native MakerVolume drops it. RetainDropped v2
    /// re-adds the ORIGINAL CLEAN shelf geometry (not an extended/conditioned version), tags it
    /// <see cref="Provenance.DroppedRetained"/>, emits a <see cref="DiagnosticCode.DroppedFace"/> diagnostic,
    /// and does not duplicate it. Native-gated.
    /// </summary>
    public class DroppedRetainIntegrationTests
    {
        /// <summary>A closed 4x4x3 m box (one cell) plus a 2x2 m horizontal shelf floating at z = 1.5, not
        /// touching any wall - so it bounds no closed cell and the kernel drops it from the resolved output.</summary>
        private static List<Face3D> BoxWithFloatingShelf()
        {
            List<Face3D> face3Ds = new List<Face3D>(TestGeometry.CreateBox(0, 0, 0, 4, 4, 3).Face3Ds);
            face3Ds.Add(TestGeometry.CreatePlanarFace(
                new Point3D(1, 1, 1.5), new Point3D(3, 1, 1.5), new Point3D(3, 3, 1.5), new Point3D(1, 3, 1.5)));
            return face3Ds;
        }

        [SkippableFact]
        public void Execute_FloatingShelfDroppedByResolve_RetainDroppedV2ReAddsCleanGeometryWithProvenance()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - force the managed pipeline (so RetainDropped v2 runs) and keep the extend passes off so
            // the shelf stays a floating 2x2 cap (not grown into a dividing floor). ConsolidateRebuild is off so
            // the retained face is observed directly in the output rather than re-imprinted by the rebuild.
            List<Face3D> face3Ds = BoxWithFloatingShelf();
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds)
            {
                ForceManagedPipeline = true,
                FillCapsToWalls = false,
                ExtendWallsToWalls = false,
                ExtendToCaps = false,
                ExtendToRoofs = false,
                FillHoles = false,
                ConsolidateRebuild = false
            };

            // Act
            solver.Execute(new OcctBuildOptions());

            // Assert - the box still closes one cell and the kernel ran.
            Assert.True(solver.NativeResolved, "Expected the managed pipeline to resolve natively");
            Assert.True(solver.ResolvedCellCount >= 1, $"Expected the box to close a cell, got {solver.ResolvedCellCount}");

            // The floating shelf (horizontal, centroid at z = 1.5) is re-added EXACTLY ONCE, as clean 2x2 = 4 m2
            // geometry - not extended, and not duplicated by a gap-fill patch.
            List<int> shelfIndices = new List<int>();
            for (int i = 0; i < solver.ResolvedFace3Ds.Count; i++)
            {
                Face3D face3D = solver.ResolvedFace3Ds[i];
                Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
                Point3D centroid = face3D?.GetBoundingBox()?.GetCentroid();
                if (normal != null && centroid != null && System.Math.Abs(normal.Z) > 0.99 && System.Math.Abs(centroid.Z - 1.5) < 0.01)
                {
                    shelfIndices.Add(i);
                }
            }

            Assert.Single(shelfIndices);
            int shelfIndex = shelfIndices[0];
            Assert.Equal(4.0, solver.ResolvedFace3Ds[shelfIndex].GetArea(), 3); // clean 2x2, not an extended footprint

            // Provenance: the retained shelf is recorded DroppedRetained (never a silent re-add).
            Assert.Contains(Provenance.DroppedRetained, solver.SourceMap.ProvenancesOf(new FaceKey(shelfIndex)));

            // A DroppedFace diagnostic was emitted from the heal stage explaining the retention.
            Assert.Contains(solver.Diagnostics.All,
                d => d.Stage == SolverStage.Heal && d.Code == DiagnosticCode.DroppedFace);
        }

        [SkippableFact]
        public void Execute_FloatingShelfDroppedByResolve_ManagedRetentionDoesNotDuplicateBoxFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - same fixture; assert the retention adds exactly one face over the resolved box (the
            // shelf), i.e. it does not re-add any already-represented box face (the IsRepresented dedup).
            List<Face3D> face3Ds = BoxWithFloatingShelf();
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds)
            {
                ForceManagedPipeline = true,
                FillCapsToWalls = false,
                ExtendWallsToWalls = false,
                ExtendToCaps = false,
                ExtendToRoofs = false,
                FillHoles = false,
                ConsolidateRebuild = false
            };

            // Act
            solver.Execute(new OcctBuildOptions());

            // Assert - exactly one retained face carries DroppedRetained provenance (the shelf); the six box
            // faces are represented by the resolved cell and are NOT re-added.
            int droppedRetainedCount = 0;
            for (int i = 0; i < solver.ResolvedFace3Ds.Count; i++)
            {
                if (solver.SourceMap.ProvenancesOf(new FaceKey(i)).Contains(Provenance.DroppedRetained))
                {
                    droppedRetainedCount++;
                }
            }

            Assert.Equal(1, droppedRetainedCount);
        }
    }
}
