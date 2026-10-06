// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="HealStage.RetainDroppedV2"/> - the Phase 5c map-driven, clean-geometry
    /// RetainDropped (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H/§J.7). Pure-managed (Face3D +
    /// hand-built <see cref="SourceMap"/>): <see cref="Panel3DSnapSolver.IsRepresented"/> is native-free, so
    /// no OCCT DLL is required. Covers dropped-source detection from the map, duplicate/degenerate/small-area
    /// filtering, provenance capture, and the raw-path filter-only (1:1 source) shape.
    /// </summary>
    public class RetainDroppedV2Tests
    {
        /// <summary>An axis-aligned rectangle at z = 0 (a horizontal cap face), corner (x0,y0) to (x1,y1).</summary>
        private static Face3D Rect(double x0, double y0, double x1, double y1)
        {
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D>
            {
                new Polygon3D(new List<Point3D>
                {
                    new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x0, y1, 0)
                })
            };

            return Face3D.Create(loops);
        }

        [Fact]
        public void RetainDroppedV2_SourceDroppedInMap_RetainsThatSourcesCleanFace()
        {
            // Arrange - source 0 is represented in the resolved output; source 1 is not (its FacesOf is empty).
            // The resolved face covers source 0's footprint only.
            Face3D resolved0 = Rect(0, 0, 2, 2);
            Face3D clean0 = Rect(0, 0, 2, 2);   // source 0 - represented (skipped map-side)
            Face3D clean1 = Rect(5, 5, 7, 7);   // source 1 - DROPPED, not covered by any resolved face

            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(0), Provenance.Resolved); // source 1 deliberately absent => dropped

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D> { resolved0 },
                new List<Face3D> { clean0, clean1 },
                new List<List<int>> { new List<int> { 0 }, new List<int> { 1 } },
                sourceMap,
                diagnostics);

            // Assert - only the dropped source's clean face is retained, keyed to source 1, and it is the
            // ORIGINAL clean face object (not fabricated/extended geometry).
            Assert.Single(result.RetainedFace3Ds);
            Assert.Same(clean1, result.RetainedFace3Ds[0]);
            Assert.Equal(new List<int> { 1 }, result.RetainedSourceIndices[0]);
        }

        [Fact]
        public void RetainDroppedV2_DroppedSourceAlreadyCoveredByResolvedFace_IsNotRetained()
        {
            // Arrange - source 1 is dropped in the map, but a resolved face geometrically covers the same
            // footprint (a double-cover): retaining it would duplicate the resolved face.
            Face3D resolved = Rect(5, 5, 7, 7);
            Face3D clean1 = Rect(5, 5, 7, 7); // coincident with the resolved face

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D> { resolved },
                new List<Face3D> { clean1 },
                new List<List<int>> { new List<int> { 1 } },
                new SourceMap(), // empty => source 1 dropped
                diagnostics);

            // Assert - the IsRepresented filter suppresses the duplicate.
            Assert.Empty(result.RetainedFace3Ds);
        }

        [Fact]
        public void RetainDroppedV2_TwoCoincidentDroppedCleanFaces_RetainsOnlyOne()
        {
            // Arrange - two dropped clean faces occupying the SAME footprint (e.g. back-to-back partition skins
            // both dropped). Only the first should be retained; the second is a double-cover of the first.
            Face3D clean1 = Rect(5, 5, 7, 7);
            Face3D clean2 = Rect(5, 5, 7, 7);

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D>(),                                   // nothing resolved represents them
                new List<Face3D> { clean1, clean2 },
                new List<List<int>> { new List<int> { 1 }, new List<int> { 2 } },
                new SourceMap(), // empty => both sources dropped
                diagnostics);

            // Assert - exactly one retained (the earlier one), deduped against already-retained geometry.
            Assert.Single(result.RetainedFace3Ds);
            Assert.Same(clean1, result.RetainedFace3Ds[0]);
        }

        [Fact]
        public void RetainDroppedV2_DroppedSourceWithTinyCleanFace_IsFilteredByArea()
        {
            // Arrange - a dropped source whose clean face is a 5 mm x 5 mm sliver (2.5e-5 m2, below the
            // 1e-4 m2 floor): a degenerate artifact, not real geometry worth retaining.
            Face3D tiny = Rect(0, 0, 0.005, 0.005);

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D>(),
                new List<Face3D> { tiny },
                new List<List<int>> { new List<int> { 0 } },
                new SourceMap(), // empty => source 0 dropped
                diagnostics);

            // Assert - the area filter rejects it.
            Assert.Empty(result.RetainedFace3Ds);
        }

        [Fact]
        public void RetainDroppedV2_RetainedFace_RecordsSourceAndEmitsDroppedFaceDiagnostic()
        {
            // Arrange - a single dropped, unrepresented source.
            Face3D clean1 = Rect(5, 5, 7, 7);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D>(),
                new List<Face3D> { clean1 },
                new List<List<int>> { new List<int> { 1 } },
                new SourceMap(),
                diagnostics);

            // Assert - the recovered source is captured for downstream DroppedRetained provenance, and a
            // DroppedFace Info diagnostic names it (never a silent re-add).
            Assert.Single(result.RetainedSourceIndices);
            Assert.Contains(1, result.RetainedSourceIndices[0]);
            Assert.Contains(diagnostics.OfCode(DiagnosticCode.DroppedFace),
                d => d.Severity == OcctDiagnosticSeverity.Info && d.Message.Contains("1"));
        }

        [Fact]
        public void RetainDroppedV2_MixedDroppedAndRepresentedSourcesOnOneCleanFace_RecordsOnlyTheDroppedSource()
        {
            // Arrange - a clean face carrying two sources: source 2 is represented, source 3 is dropped. The
            // face is not covered by the resolved set, so it is retained - but only source 3 (the dropped one)
            // is recorded, so the still-represented source 2 is not perturbed into a spurious split.
            Face3D clean = Rect(5, 5, 7, 7);

            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(2, new FaceKey(0), Provenance.Resolved); // source 2 represented; source 3 dropped

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D> { Rect(0, 0, 1, 1) }, // an unrelated resolved face
                new List<Face3D> { clean },
                new List<List<int>> { new List<int> { 2, 3 } },
                sourceMap,
                diagnostics);

            // Assert - retained, but keyed only to the dropped source 3.
            Assert.Single(result.RetainedFace3Ds);
            Assert.Equal(new List<int> { 3 }, result.RetainedSourceIndices[0]);
        }

        [Fact]
        public void RetainDroppedV2_RawPathShape_OneSourcePerFace_RetainsOnlyDroppedUnrepresentedFaces()
        {
            // Arrange - the raw-path shape: every clean face IS its own source (1:1). Sources 0 and 2 are
            // represented; source 1 is dropped and not covered by any resolved face. RetainDroppedV2 filters
            // to exactly the dropped-and-unrepresented face - no fabrication, pure filtering.
            Face3D f0 = Rect(0, 0, 2, 2);
            Face3D f1 = Rect(5, 5, 7, 7);
            Face3D f2 = Rect(10, 10, 12, 12);

            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(0), Provenance.Resolved);
            sourceMap.Record(2, new FaceKey(2), Provenance.Resolved); // source 1 absent => dropped

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act - resolved covers f0 and f2 only.
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D> { Rect(0, 0, 2, 2), Rect(10, 10, 12, 12) },
                new List<Face3D> { f0, f1, f2 },
                new List<List<int>> { new List<int> { 0 }, new List<int> { 1 }, new List<int> { 2 } },
                sourceMap,
                diagnostics);

            // Assert - exactly the dropped source 1's original face, filtered from the raw candidates.
            Assert.Single(result.RetainedFace3Ds);
            Assert.Same(f1, result.RetainedFace3Ds[0]);
            Assert.Equal(new List<int> { 1 }, result.RetainedSourceIndices[0]);
        }

        [Fact]
        public void RetainDroppedV2_NoDroppedSources_RetainsNothing()
        {
            // Arrange - every source is represented in the map.
            Face3D clean0 = Rect(0, 0, 2, 2);
            Face3D clean1 = Rect(5, 5, 7, 7);

            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(0), Provenance.Resolved);
            sourceMap.Record(1, new FaceKey(1), Provenance.Resolved);

            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            HealStage.RetainDroppedResult result = HealStage.RetainDroppedV2(
                new List<Face3D> { Rect(0, 0, 2, 2), Rect(5, 5, 7, 7) },
                new List<Face3D> { clean0, clean1 },
                new List<List<int>> { new List<int> { 0 }, new List<int> { 1 } },
                sourceMap,
                diagnostics);

            // Assert - nothing retained, and no DroppedFace diagnostics emitted.
            Assert.Empty(result.RetainedFace3Ds);
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.DroppedFace));
        }
    }
}
