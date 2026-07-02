// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage C (Phase 2 scope) - HEAL: re-attach input faces the native MakerVolume dropped. The
    /// kernel returns only faces that bound a closed cell, so a face whose cell fails to form (a
    /// stepped/tilted region it cannot close, or a lid the cells cap off) is silently discarded,
    /// leaving a hole. This re-adds every conditioned face with no representation in the resolved
    /// output (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§F). In Phase 2 this is the
    /// pre-existing RetainDropped behaviour extracted verbatim as a façade seam; Phase 5 replaces it
    /// with the imprinted, provenance-tagged RetainDropped v2. (The sew and gap-fill sub-passes remain
    /// in <see cref="ResolveStage"/> for now - see its remarks.)
    /// </summary>
    public static class HealStage
    {
        /// <summary>Outcome of <see cref="RetainDropped"/>: the augmented face set plus the faces that were re-added.</summary>
        public class Result
        {
            public List<Face3D> ResolvedFace3Ds { get; set; } = new List<Face3D>();

            public List<Face3D> RetainedFace3Ds { get; } = new List<Face3D>();
        }

        /// <summary>
        /// Re-adds every face in <paramref name="candidateFace3Ds"/> (the conditioned faces fed to the resolve)
        /// that is not represented in <paramref name="resolvedFace3Ds"/>, using its (extended) geometry so the
        /// re-added face overshoots its neighbours and closes the gap - the pre-Phase-2 RetainDropped contract.
        /// Records the re-added faces (and, when a <paramref name="sourceMap"/> is supplied, tags them
        /// <see cref="Provenance.DroppedRetained"/> keyed by their output index for downstream reconstruction).
        /// </summary>
        public static Result RetainDropped(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> candidateFace3Ds,
            SourceMap sourceMap = null)
        {
            Result result = new Result();
            List<Face3D> resolved = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();
            result.ResolvedFace3Ds = resolved;

            if (candidateFace3Ds == null || candidateFace3Ds.Count == 0)
            {
                return result;
            }

            foreach (Face3D face3D in candidateFace3Ds)
            {
                if (face3D != null && face3D.IsValid() && !Panel3DSnapSolver.IsRepresented(face3D, resolved))
                {
                    result.RetainedFace3Ds.Add(face3D);
                }
            }

            if (result.RetainedFace3Ds.Count != 0)
            {
                int baseIndex = resolved.Count;
                result.ResolvedFace3Ds = resolved.Concat(result.RetainedFace3Ds).ToList();

                for (int i = 0; i < result.RetainedFace3Ds.Count; i++)
                {
                    sourceMap?.RecordFabricated(new FaceKey(baseIndex + i), Provenance.DroppedRetained);
                }
            }

            return result;
        }
    }
}
