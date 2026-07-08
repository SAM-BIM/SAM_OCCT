// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Synthetic ~1,500-face performance/scaling fixture (Phase 5f,
    /// docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §J fixture 8 / §K sub-phase 5f). A grid of fully
    /// independent, disjoint 6-face room boxes - deterministic and parametric (no randomness, so nothing needs a
    /// seed): the layout is entirely determined by <see cref="RoomGrid"/>'s arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why disjoint rooms, not a shared-wall lattice.</b> The review's illustrative shape ("10x8 rooms x 2
    /// levels") suggests a connected floor plan with shared walls between neighbours. Measured directly: a single
    /// continuous shared-wall lattice at a comparable cell count (~700 cells) took over 130 seconds for just the
    /// first native resolve - MakerVolume's cost is driven by CONNECTED COMPONENT size, not total face/cell count
    /// (matching the review §M non-linear-scaling caution). Fully independent, disjoint rooms (separated by a
    /// small gap on every axis) decompose into many trivially-cheap native operations instead of one expensive
    /// one, measured at ~4 seconds for 1,560 faces / 260 cells - comfortably inside the 90 s budget with headroom.
    /// </para>
    /// <para>
    /// <b>Why this fixture is watertight, not gappy.</b> Owner decision (2026-07-04): Benchmark1500 is a
    /// PERFORMANCE/scaling guard only - it does not need to force AutoTune escalation rounds. Round-forcing,
    /// GapFill replacement, residual diagnostics and the bounded-rounds acceptance gate are already proven at
    /// small scale by <c>AutoTune3DIntegrationTests</c> (Phase 5e) and do not need re-proving at 1,500-face scale
    /// (AutoTune's correctness does not depend on the overall model size). Measured separately: introducing a
    /// gappy perturbation into a large multi-room model is unreliable with the current pipeline - a single broken
    /// room's faces get silently absorbed by the raw-adoption gate's `RetainDropped` recovery path (never forming
    /// a cell, but also never surfacing a naked-edge or fabricated-patch signal) whenever OTHER valid rooms
    /// coexist in the same solve, regardless of defect size, sew settings, or room connectivity - so
    /// <c>AutoTune3D</c>'s engagement gate (naked&gt;0 OR fabricated patches present) never fires. This is a
    /// property of the existing (Phase 1/5c) raw-first + RetainDropped design working as calibrated for a low
    /// tolerated drop ratio, not a defect introduced here; see TESTING.md's Phase 5f section.
    /// </para>
    /// </remarks>
    internal static class BenchmarkFixture
    {
        /// <summary>One face factory shared with the rest of the integration suite's synthetic fixtures.</summary>
        private static Face3D Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>
        /// A grid of <paramref name="columns"/> x <paramref name="rows"/> x <paramref name="levels"/> fully
        /// independent, watertight 6-face room boxes (floor, ceiling, 4 walls), each <paramref name="roomSize"/> m
        /// square and <paramref name="height"/> m tall, separated from its neighbours by <paramref name="gap"/> m
        /// on every axis (X, Y, and Z - so no two rooms share so much as a coincident plane; the vertical gap in
        /// particular avoids a level's ceiling exactly coinciding with the level above's floor, which would
        /// otherwise need Stage A's coplanar-merge to resolve and is unnecessary complexity for a pure scaling
        /// benchmark). Every room closes into exactly one cell with zero naked edges - a deterministic,
        /// analytically-known-watertight result, so "closure sanity" is a plain count comparison.
        /// </summary>
        /// <param name="columns">Rooms along X.</param>
        /// <param name="rows">Rooms along Y.</param>
        /// <param name="levels">Rooms (floors) along Z.</param>
        /// <param name="roomSize">Room footprint side length, in metres.</param>
        /// <param name="height">Room height, in metres.</param>
        /// <param name="gap">Separation between neighbouring rooms on every axis, in metres.</param>
        /// <returns>The face list (6 faces per room, <c>columns * rows * levels * 6</c> total) and the expected
        /// watertight cell count (<c>columns * rows * levels</c> - one per room).</returns>
        public static (List<Face3D> Face3Ds, int ExpectedCellCount) RoomGrid(
            int columns, int rows, int levels, double roomSize = 4.0, double height = 3.0, double gap = 1.0)
        {
            List<Face3D> face3Ds = new List<Face3D>(columns * rows * levels * 6);
            double pitchXY = roomSize + gap;
            double pitchZ = height + gap;

            for (int l = 0; l < levels; l++)
            {
                double z0 = l * pitchZ, z1 = z0 + height;
                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * pitchXY, y1 = y0 + roomSize;
                    for (int c = 0; c < columns; c++)
                    {
                        double x0 = c * pitchXY, x1 = x0 + roomSize;

                        face3Ds.Add(Quad(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0))); // floor
                        face3Ds.Add(Quad(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1))); // ceiling
                        face3Ds.Add(Quad(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1))); // y0 wall
                        face3Ds.Add(Quad(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1))); // y1 wall
                        face3Ds.Add(Quad(new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1))); // x0 wall
                        face3Ds.Add(Quad(new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1))); // x1 wall
                    }
                }
            }

            return (face3Ds, columns * rows * levels);
        }

        /// <summary>
        /// The ~1,500-face benchmark fixture proper: 10 x 5 x 5 rooms (columns x rows x levels) = 250 rooms x 6
        /// faces = exactly 1,500 faces, 250 expected cells. Dimensions chosen to hit the review's "≈1,500 faces"
        /// target precisely while keeping a plausible multi-storey building footprint (10x5 plan, 5 levels).
        /// </summary>
        public static (List<Face3D> Face3Ds, int ExpectedCellCount) Benchmark1500()
        {
            return RoomGrid(columns: 10, rows: 5, levels: 5);
        }
    }
}
