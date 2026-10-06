// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Detects inter-storey <b>stacked-slab interfaces</b> (Phase 6d): pairs of near-congruent, opposite-facing
    /// cap faces - the floor of level N and the ceiling of level N-1 - that represent the same physical
    /// inter-storey boundary (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6). It is a pure,
    /// native-free, <b>non-mutating</b> analysis: it identifies and diagnoses interfaces and verifies that both
    /// analytical source panels survive into the output, but it does NOT move or merge any geometry.
    /// <para>
    /// The geometric "treat two skins as one interface for the cell build" is already performed by the proven,
    /// golden-locked mechanisms the solver runs today - the native sew (which stitches coincident/near-coincident
    /// skins on the raw path) and <see cref="Panel3DSnapSolver.SnapOpposedPartitions"/> (which collapses an
    /// opposed, congruent, within-a-wall-thickness pair on the managed path) - and provenance for a merged
    /// interface is already preserved (the geometric attribution maps both coplanar-overlapping sources to the
    /// shared face, and <c>PanelReconstruction</c> keeps the dominant source's Guid and stamps the other as a
    /// merged source). Introducing a NEW collapse here would be redundant within that band and unsafe beyond it
    /// (a wider opposed pair bounds a genuine cavity/shaft the plan requires be kept), and would risk the raw
    /// golden masters this phase must hold byte-identical. So 6d formalises the DETECTION across level frames,
    /// GUARANTEES/verifies the both-sources provenance, and REJECTS the unsafe cases with diagnostics.
    /// </para>
    /// </summary>
    public static class StackedSlabInterfaceDetector
    {
        /// <summary>Half-angle (radians) of the cone within which the two skins count as (anti-)parallel - the
        /// same ~5° the solver uses for opposed partitions.</summary>
        public const double DEFAULT_ConeTolerance = 5.0 * (System.Math.PI / 180.0);

        /// <summary>Maximum perpendicular separation (metres) for an opposed cap pair to be a slab interface
        /// rather than a genuine cavity/shaft. Deliberately equal to
        /// <see cref="Panel3DSnapSolver.OPPOSED_PARTITION_MAX_SEPARATION"/> (0.3 m) so this detector accepts
        /// exactly the band the managed pipeline already collapses and rejects exactly the wider band it (and the
        /// plan's shaft-void rule) protects - never widening the tolerated merge.</summary>
        public const double DEFAULT_MaxSlabSeparation = Panel3DSnapSolver.OPPOSED_PARTITION_MAX_SEPARATION;

        /// <summary>Minimum in-plane overlap-to-larger-footprint ratio for the two skins to count as congruent
        /// (one interface). A split-level landing / partial step is a SMALL cap over a LARGER floor, so its ratio
        /// is far below this; two side-by-side rooms' caps do not overlap at all. 0.8 keeps a door-notched or
        /// slightly-trimmed skin (footprint essentially shared) while rejecting a partial step.</summary>
        public const double DEFAULT_MinOverlapRatio = 0.8;

        /// <summary>Half-angle (radians) within which a face normal counts as (near) horizontal-in-world-Z so the
        /// face is a cap (floor/roof), matching <see cref="Panel3DSnapSolver.VerticalAngleTolerance"/>.</summary>
        public const double DEFAULT_VerticalAngleTolerance = 20.0 * (System.Math.PI / 180.0);

        /// <summary>
        /// Detects the stacked-slab interfaces among <paramref name="face3Ds"/> (indexed as their
        /// <see cref="SourceMap"/> source indices). Conservative: an opposed cap pair is accepted ONLY when it is
        /// anti-parallel within <paramref name="coneTolerance"/>, overlaps at least
        /// <paramref name="minOverlapRatio"/> of the larger footprint, sits within
        /// <paramref name="maxSlabSeparation"/> perpendicular of one another, and both skins assign to a level
        /// frame unambiguously. Every rejected candidate (wide separation -&gt; cavity/shaft; low overlap -&gt;
        /// split-level landing / partial step; ambiguous frame membership) emits a diagnostic - never silent.
        /// Returns the accepted interfaces in a deterministic order (by interface elevation, then source indices).
        /// </summary>
        public static List<StackedSlabInterface> DetectStackedInterfaces(
            IReadOnlyList<Face3D> face3Ds,
            double coneTolerance = DEFAULT_ConeTolerance,
            double maxSlabSeparation = DEFAULT_MaxSlabSeparation,
            double minOverlapRatio = DEFAULT_MinOverlapRatio,
            double verticalAngleTolerance = DEFAULT_VerticalAngleTolerance,
            SolverDiagnostics diagnostics = null)
        {
            List<StackedSlabInterface> result = new List<StackedSlabInterface>();
            if (face3Ds == null || face3Ds.Count < 2)
            {
                return result;
            }

            double sinVertical = System.Math.Sin(verticalAngleTolerance);
            double minParallelDot = System.Math.Cos(coneTolerance); // anti-parallel: dot < -minParallelDot

            // Collect the cap faces (non-vertical in world Z) with their input indices. Wrap each in a
            // SnappedPanel purely to reuse the proven, winding-independent PerpendicularSeparation and
            // InPlaneOverlapRatio primitives; no geometry is mutated (the panels are throwaway).
            List<int> capIndices = new List<int>();
            List<Face3D> capFaces = new List<Face3D>();
            List<SnappedPanel> capPanels = new List<SnappedPanel>();
            List<Point3D> capCentroids = new List<Point3D>();
            List<Vector3D> capNormals = new List<Vector3D>();
            List<BoundingBox3D> capBoxes = new List<BoundingBox3D>();

            for (int i = 0; i < face3Ds.Count; i++)
            {
                Face3D face3D = face3Ds[i];
                Plane plane = face3D?.GetPlane();
                BoundingBox3D box = face3D?.GetBoundingBox();
                Point3D centroid = box?.GetCentroid();
                if (plane == null || box == null || centroid == null || !face3D.IsValid())
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (System.Math.Abs(normal.Z) <= sinVertical)
                {
                    continue; // a wall, not a cap
                }

                capIndices.Add(i);
                capFaces.Add(face3D);
                capPanels.Add(new SnappedPanel(i, face3D, 1.0, Panel3DSnapSolver.DEFAULT_BucketSize, 0.0));
                capCentroids.Add(centroid);
                capNormals.Add(normal);
                capBoxes.Add(box);
            }

            if (capPanels.Count < 2)
            {
                return result;
            }

            // Level frames for context + the ambiguity guard (frame-aware "across LevelFrames").
            List<LevelFrame> frames = LevelFrame.Cluster(capFaces, coneTolerance, LevelFrame.DEFAULT_ElevationBand);

            for (int a = 0; a < capPanels.Count; a++)
            {
                for (int b = a + 1; b < capPanels.Count; b++)
                {
                    // Cheap plan pre-filter: two stacked skins overlap in the XY (plan) projection; two side-by-side
                    // caps do not, and are dropped here before the exact predicates run.
                    if (!PlanBoxesOverlap(capBoxes[a], capBoxes[b]))
                    {
                        continue;
                    }

                    // The two caps must at least be (anti-)parallel; oblique caps are not an interface pair.
                    double dot = capNormals[a].DotProduct(capNormals[b]);
                    if (System.Math.Abs(dot) < minParallelDot)
                    {
                        continue;
                    }

                    double separation = capPanels[a].PerpendicularSeparation(capPanels[b]);
                    double overlap = capPanels[a].InPlaneOverlapRatio(capPanels[b]);

                    // Co-parallel (same-facing) congruent pair within the slab band: a duplicate / intentional
                    // double-skin of ONE room, or a split-level whole floor - NOT an opposite-rooms stacked
                    // interface. Diagnose the ambiguity and leave it alone (the plan's "do not collapse intentional
                    // double skins" / "do not normalize away split-level landings").
                    if (dot > 0)
                    {
                        if (separation <= maxSlabSeparation && overlap >= minOverlapRatio)
                        {
                            diagnostics?.Add(SolverStage.Snap, DiagnosticCode.RejectedCollapse, OcctDiagnosticSeverity.Info,
                                string.Format("Cap pair {0}/{1} is co-parallel (same-facing) and congruent {2:0.###} m apart - a double-skin / split-level, not an opposite-rooms stacked interface; kept separate.",
                                    capIndices[a], capIndices[b], separation),
                                face3D: capFaces[a], toleranceUsed: separation);
                        }

                        continue;
                    }

                    // Anti-parallel (opposite-facing) from here: the two skins face opposite rooms (floor up +
                    // ceiling down), the signature of a genuine inter-storey interface.
                    // Reject: a wider opposed pair bounds a genuine cavity/shaft/plenum, NOT a structural slab -
                    // it must survive (the shaft-void rule, same band as SnapOpposedPartitions).
                    if (separation > maxSlabSeparation)
                    {
                        diagnostics?.Add(SolverStage.Snap, DiagnosticCode.RejectedCollapse, OcctDiagnosticSeverity.Info,
                            string.Format("Stacked-slab candidate {0}/{1} is {2:0.###} m apart (> {3} m slab band) - a real cavity/shaft, not a slab interface; kept separate.",
                                capIndices[a], capIndices[b], separation, maxSlabSeparation),
                            face3D: capFaces[a], toleranceUsed: maxSlabSeparation);
                        continue;
                    }

                    // Reject: a small cap over a larger floor (a split-level landing / partial step) or two
                    // partially-overlapping caps are NOT congruent skins of one interface.
                    if (overlap < minOverlapRatio)
                    {
                        diagnostics?.Add(SolverStage.Snap, DiagnosticCode.RejectedCollapse, OcctDiagnosticSeverity.Info,
                            string.Format("Stacked-slab candidate {0}/{1} overlaps only {2:P0} of the larger footprint (< {3:P0}) - a split-level landing or partial step, not congruent skins; kept separate.",
                                capIndices[a], capIndices[b], overlap, minOverlapRatio),
                            face3D: capFaces[a], toleranceUsed: minOverlapRatio);
                        continue;
                    }

                    // Frame membership (context + ambiguity guard). Suppress the per-cap ambiguity spam from
                    // AssignCapToFrame; a genuinely unassignable skin is reported once here as a rejection.
                    int frameA = LevelFrame.AssignCapToFrame(capFaces[a], frames, coneTolerance, LevelFrame.DEFAULT_ElevationBand);
                    int frameB = LevelFrame.AssignCapToFrame(capFaces[b], frames, coneTolerance, LevelFrame.DEFAULT_ElevationBand);
                    if (frameA < 0 || frameB < 0)
                    {
                        diagnostics?.Add(SolverStage.Snap, DiagnosticCode.AmbiguousLevelFrame, OcctDiagnosticSeverity.Warning,
                            string.Format("Stacked-slab candidate {0}/{1} could not be assigned to level frames unambiguously; interface rejected.",
                                capIndices[a], capIndices[b]),
                            face3D: capFaces[a]);
                        continue;
                    }

                    // Accept: order the two skins by elevation for determinism.
                    bool aIsLower = capCentroids[a].Z <= capCentroids[b].Z;
                    int lowerIndex = aIsLower ? capIndices[a] : capIndices[b];
                    int upperIndex = aIsLower ? capIndices[b] : capIndices[a];
                    int lowerFrame = aIsLower ? frameA : frameB;
                    int upperFrame = aIsLower ? frameB : frameA;
                    double elevation = 0.5 * (capCentroids[a].Z + capCentroids[b].Z);

                    result.Add(new StackedSlabInterface(lowerIndex, upperIndex, lowerFrame, upperFrame, separation, overlap, elevation));

                    diagnostics?.Add(SolverStage.Snap, DiagnosticCode.DuplicateFace, OcctDiagnosticSeverity.Info,
                        string.Format("Stacked slab interface: sources {0}/{1} (frames {2}/{3}) are near-congruent floor/ceiling skins {4:0.###} m apart ({5:P0} overlap) - one inter-storey interface for the cell build.",
                            lowerIndex, upperIndex, lowerFrame, upperFrame, separation, overlap),
                        face3D: capFaces[a], toleranceUsed: separation);
                }
            }

            return result
                .OrderBy(x => x.Elevation)
                .ThenBy(x => x.LowerSourceIndex)
                .ThenBy(x => x.UpperSourceIndex)
                .ToList();
        }

        /// <summary>
        /// Verifies that BOTH analytical source panels of every detected interface survive into the output, per
        /// <paramref name="sourceMap"/> (a source with a non-empty <see cref="SourceMap.FacesOf(int)"/> is
        /// represented). Emits an Info diagnostic when both are represented (the expected case - the geometric
        /// merge preserved both meanings) and a Warning when one is missing (provenance for the merged interface
        /// is incomplete - never silent). Non-mutating: it reports, it does not repair.
        /// </summary>
        public static void VerifyRepresented(
            IEnumerable<StackedSlabInterface> interfaces,
            SourceMap sourceMap,
            SolverDiagnostics diagnostics = null)
        {
            if (interfaces == null || sourceMap == null || diagnostics == null)
            {
                return;
            }

            foreach (StackedSlabInterface stackedSlabInterface in interfaces)
            {
                bool lowerRepresented = sourceMap.FacesOf(stackedSlabInterface.LowerSourceIndex).Count > 0;
                bool upperRepresented = sourceMap.FacesOf(stackedSlabInterface.UpperSourceIndex).Count > 0;

                if (lowerRepresented && upperRepresented)
                {
                    diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                        string.Format("Stacked slab interface {0}/{1}: both analytical source panels are represented in the output (provenance preserved).",
                            stackedSlabInterface.LowerSourceIndex, stackedSlabInterface.UpperSourceIndex));
                }
                else
                {
                    int missing = lowerRepresented ? stackedSlabInterface.UpperSourceIndex : stackedSlabInterface.LowerSourceIndex;
                    diagnostics.Add(SolverStage.Heal, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Warning,
                        string.Format("Stacked slab interface {0}/{1}: source {2} has no representation in the output - provenance for the merged interface is incomplete.",
                            stackedSlabInterface.LowerSourceIndex, stackedSlabInterface.UpperSourceIndex, missing));
                }
            }
        }

        /// <summary>True when two bounding boxes overlap in the XY (plan) projection.</summary>
        private static bool PlanBoxesOverlap(BoundingBox3D a, BoundingBox3D b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
                && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }
    }
}
