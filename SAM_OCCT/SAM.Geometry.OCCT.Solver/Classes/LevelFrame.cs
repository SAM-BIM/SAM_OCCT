// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// One analytical level datum: a canonical plane (an <see cref="Origin"/> point and a
    /// <c>+Z</c>-hemisphere unit <see cref="Normal"/> up-axis) grouping the cap (floor/roof) faces that
    /// share an orientation and an elevation - the Phase 6 replacement for the solver's single global
    /// <c>Up</c> and world-frame verticality (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="LevelFrame"/> is produced by <see cref="Cluster"/>, which groups near-coplanar,
    /// co-elevation caps the way <see cref="Panel3DSnapSolver.NormalizeCaps(System.Collections.Generic.List{SnappedPanel}, double, double, double, double)"/> already groups a level's
    /// tiles - dominant-area-first, by normal cone plus a perpendicular elevation band - but formalises the
    /// grouping into a data structure with its own frame instead of snapping planes. Because the datum plane
    /// carries an up-axis, later sub-phases evaluate verticality / cap classification (6b) and run cap
    /// normalization and extend/fill (6c) <em>in the frame</em>, so a tilted or stacked level is no longer
    /// bound to world Z or the 20° world-frame tilt ceiling.
    /// </para>
    /// <para>
    /// This class is a pure-managed, native-free foundation (6a): it introduces the type, the clustering and
    /// the assignment helpers, and does NOT change any solver geometry behaviour - nothing in the solver
    /// pipeline consumes it yet. A floor of level N and the ceiling of level N-1 fall into the same datum
    /// (they are within a slab thickness of one another), which is exactly the shared inter-storey interface
    /// 6d builds on; two genuinely stacked floors, a roof, or a split-level landing each fall into their own
    /// frame because they are more than the elevation band apart.
    /// </para>
    /// </remarks>
    public class LevelFrame
    {
        /// <summary>Default half-angle (radians) of the normal cone within which two caps count as the same
        /// orientation - ~5° (docs plan §E Phase 6, §M "Cluster angles"). Anti-parallel caps (a floor's
        /// up-normal and a ceiling's down-normal) count as the same orientation; the elevation band then
        /// separates them.</summary>
        public const double DEFAULT_NormalConeTolerance = 5.0 * (System.Math.PI / 180.0);

        /// <summary>
        /// Default perpendicular band (metres) within which two same-orientation caps share one level datum.
        /// Deliberately below a typical deliberate split-level step (~0.25 m and up) so a landing is NEVER
        /// merged away into the main floor, yet above the millimetre-to-centimetre import/tile noise a single
        /// slab carries; strictly tighter than the legacy <see cref="Panel3DSnapSolver.NormalizeCapOffset"/>
        /// (0.3 m), which the plan flags as wide enough to eat a split-level landing (docs plan §E Phase 6,
        /// Risk 5). A floor and the ceiling above it are metres apart, so they always land in separate frames.
        /// </summary>
        public const double DEFAULT_ElevationBand = 0.15;

        /// <summary>Default half-angle (radians) within which a face normal counts as perpendicular to the
        /// frame up-axis, so the face is a wall - ~20°, matching the solver's
        /// <see cref="Panel3DSnapSolver.VerticalAngleTolerance"/>. A cap is anything not vertical (its normal
        /// carries a significant up-component), matching the pipeline's binary wall/cap split.</summary>
        public const double DEFAULT_VerticalAngleTolerance = 20.0 * (System.Math.PI / 180.0);

        private Transform3D toFrame;
        private Transform3D fromFrame;
        private bool transformsBuilt;

        /// <summary>The frame's up-axis: a unit vector in the <c>+Z</c> hemisphere (a floor and a ceiling both
        /// yield an upward axis). World Z for a flat level; the level normal for a tilted one.</summary>
        public Vector3D Normal { get; }

        /// <summary>A point on the datum plane - the dominant (largest-area) member cap's plane origin when
        /// built by <see cref="Cluster"/>.</summary>
        public Point3D Origin { get; }

        /// <summary>The canonical datum plane (<see cref="Origin"/>, <see cref="Normal"/>).</summary>
        public Plane Plane { get; }

        /// <summary>Signed perpendicular offset of <see cref="Origin"/> from the world origin along
        /// <see cref="Normal"/> (<c>Normal·Origin</c>) - the frame's elevation, and the deterministic sort key
        /// <see cref="Cluster"/> orders the returned frames by. Equals <c>Origin.Z</c> for a flat level.</summary>
        public double Elevation { get; }

        /// <summary>Angle (radians) between <see cref="Normal"/> and world Z: 0 for a flat level, the tilt for a
        /// sloped one. Diagnostic/reporting aid (and the signal 6b uses to decide whether to rotate).</summary>
        public double TiltAngle { get; }

        /// <summary>Indices (into the list handed to <see cref="Cluster"/>) of the member cap faces, ascending
        /// for determinism. Empty for a frame built directly from a known up-axis.</summary>
        public IReadOnlyList<int> CapIndices { get; }

        /// <summary>Largest member cap area (m²) - the dominant cap that seeded the frame; 0 for a
        /// directly-constructed frame. Used as a deterministic tie-break when assigning an ambiguous face.</summary>
        public double DominantArea { get; }

        /// <summary>
        /// Builds a level frame from a known up-axis and datum point (no member caps) - the forward-useful
        /// entry point for a caller that already knows the level (e.g. the legacy single <c>Up</c>). The
        /// normal is unit-ised and collapsed to the <c>+Z</c> hemisphere so the frame's up-axis points up
        /// regardless of the source face winding.
        /// </summary>
        public LevelFrame(Vector3D normal, Point3D origin)
            : this(normal, origin, new List<int>(), 0.0)
        {
        }

        private LevelFrame(Vector3D normal, Point3D origin, IReadOnlyList<int> capIndices, double dominantArea)
        {
            if (normal == null)
            {
                throw new System.ArgumentNullException(nameof(normal));
            }

            if (origin == null)
            {
                throw new System.ArgumentNullException(nameof(origin));
            }

            Vector3D unit = normal.Unit;
            if (unit.Z < 0)
            {
                unit = unit.GetNegated(); // axis only: keep the up-axis in the +Z hemisphere
            }

            Normal = unit;
            Origin = origin;
            Plane = new Plane(origin, unit);
            Elevation = unit.X * origin.X + unit.Y * origin.Y + unit.Z * origin.Z;
            TiltAngle = unit.SmallestAngle(new Vector3D(0, 0, 1));
            CapIndices = capIndices ?? new List<int>();
            DominantArea = dominantArea;
        }

        /// <summary>
        /// Signed perpendicular offset (metres) of <paramref name="worldPoint"/> from this datum plane, along
        /// <see cref="Normal"/>: positive above the datum, negative below, ~0 on it. The measure that decides
        /// cap-band membership (<see cref="ContainsCap"/>) and the frames a wall spans
        /// (<see cref="AssignWallToFrames"/>). <see cref="double.NaN"/> for a null point.
        /// </summary>
        public double SignedElevation(Point3D worldPoint)
        {
            if (worldPoint == null)
            {
                return double.NaN;
            }

            return Normal.X * (worldPoint.X - Origin.X)
                + Normal.Y * (worldPoint.Y - Origin.Y)
                + Normal.Z * (worldPoint.Z - Origin.Z);
        }

        /// <summary>
        /// True when <paramref name="cap"/> belongs to this datum: its supporting plane is parallel within
        /// <paramref name="coneTolerance"/> (anti-parallel included) AND its centroid lies within
        /// <paramref name="elevationBand"/> perpendicular of the datum plane.
        /// </summary>
        public bool ContainsCap(Face3D cap, double coneTolerance = DEFAULT_NormalConeTolerance, double elevationBand = DEFAULT_ElevationBand)
        {
            Plane plane = cap?.GetPlane();
            Point3D centroid = cap?.GetBoundingBox()?.GetCentroid();
            if (plane == null || centroid == null)
            {
                return false;
            }

            if (System.Math.Abs(plane.Normal.Unit.DotProduct(Normal)) < System.Math.Cos(coneTolerance))
            {
                return false;
            }

            return System.Math.Abs(SignedElevation(centroid)) <= elevationBand;
        }

        /// <summary>
        /// Frame-aware verticality: true when <paramref name="face"/>'s normal is (near) perpendicular to this
        /// frame's up-axis - a wall <em>of this level</em>. Unlike the world-Z
        /// <see cref="SnappedPanel.IsVertical(double)"/>, this measures against the level up-axis, so a wall on
        /// a tilted level (whose normal is tilted away from horizontal in world Z) is still recognised as a
        /// wall past the 20° world-frame ceiling. Reduces to the world-Z test for a flat frame.
        /// </summary>
        public bool IsVertical(Face3D face, double verticalAngleTolerance = DEFAULT_VerticalAngleTolerance)
        {
            Plane plane = face?.GetPlane();
            if (plane == null)
            {
                return false;
            }

            return System.Math.Abs(plane.Normal.Unit.DotProduct(Normal)) <= System.Math.Sin(verticalAngleTolerance);
        }

        /// <summary>Frame-aware wall test - an alias of <see cref="IsVertical(Face3D, double)"/> (a wall is a
        /// vertical panel), named for call sites that read in terms of walls.</summary>
        public bool IsWall(Face3D face, double verticalAngleTolerance = DEFAULT_VerticalAngleTolerance)
        {
            return IsVertical(face, verticalAngleTolerance);
        }

        /// <summary>
        /// Frame-aware cap test: true when <paramref name="face"/> is NOT vertical in this frame - its normal
        /// carries a significant component along the level up-axis, so it is a floor/roof of this level. The
        /// exact complement of <see cref="IsWall(Face3D, double)"/> for a valid face (a null/degenerate face is
        /// neither), preserving the pipeline's binary wall/cap partition but measured in-frame.
        /// </summary>
        public bool IsCap(Face3D face, double verticalAngleTolerance = DEFAULT_VerticalAngleTolerance)
        {
            Plane plane = face?.GetPlane();
            if (plane == null)
            {
                return false;
            }

            return System.Math.Abs(plane.Normal.Unit.DotProduct(Normal)) > System.Math.Sin(verticalAngleTolerance);
        }

        /// <summary>World→frame transform: maps <see cref="Normal"/> onto world Z and the datum plane onto the
        /// XY plane through the world origin, so in-frame Z is the signed elevation above the datum. Reuses the
        /// same <c>GetOriginToPlane</c> machinery the solver already uses for the single tilted <c>Up</c>.</summary>
        public Transform3D ToFrame
        {
            get
            {
                EnsureTransforms();
                return toFrame;
            }
        }

        /// <summary>Frame→world transform: the inverse of <see cref="ToFrame"/>.</summary>
        public Transform3D FromFrame
        {
            get
            {
                EnsureTransforms();
                return fromFrame;
            }
        }

        /// <summary>Expresses <paramref name="worldPoint"/> in this frame's coordinates (its Z is the signed
        /// elevation above the datum). Null for a null input.</summary>
        public Point3D ToFrameCoordinates(Point3D worldPoint)
        {
            return worldPoint?.Transform(ToFrame);
        }

        /// <summary>Maps <paramref name="framePoint"/> from this frame's coordinates back to world space -
        /// the inverse of <see cref="ToFrameCoordinates"/>. Null for a null input.</summary>
        public Point3D FromFrameCoordinates(Point3D framePoint)
        {
            return framePoint?.Transform(FromFrame);
        }

        private void EnsureTransforms()
        {
            if (transformsBuilt)
            {
                return;
            }

            Plane framePlane = new Plane(Origin, Normal);
            toFrame = Transform3D.GetOriginToPlane(framePlane);
            fromFrame = Transform3D.GetPlaneToOrigin(framePlane);
            transformsBuilt = true;
        }

        /// <summary>
        /// Clusters cap faces into level frames, dominant-area-first: the largest not-yet-assigned cap seeds a
        /// frame (its plane becomes the datum), then every remaining cap that is parallel within
        /// <paramref name="coneTolerance"/> and whose centroid is within <paramref name="elevationBand"/>
        /// perpendicular of that datum joins it. Membership is tested against the seed datum only (not
        /// transitively), so a datum is anchored by its dominant cap and cannot chain across a wide span; a
        /// cap more than the band from every existing datum seeds its own frame. Deterministic: the seed order
        /// (area ↓, elevation ↑, centroid, index) is independent of input order, so the same caps form the
        /// same frames, and the returned list is sorted by <see cref="Elevation"/> ascending. Mirrors
        /// <see cref="Panel3DSnapSolver.NormalizeCaps(System.Collections.Generic.List{SnappedPanel}, double, double, double, double)"/>'s grouping without moving any geometry.
        /// </summary>
        /// <param name="caps">Candidate cap (floor/roof, non-vertical) faces; the caller filters walls out.</param>
        /// <param name="coneTolerance">Half-angle of the same-orientation normal cone (radians).</param>
        /// <param name="elevationBand">Perpendicular band that groups caps onto one datum (metres).</param>
        /// <param name="diagnostics">Optional sink for the frame-count summary.</param>
        public static List<LevelFrame> Cluster(
            IReadOnlyList<Face3D> caps,
            double coneTolerance = DEFAULT_NormalConeTolerance,
            double elevationBand = DEFAULT_ElevationBand,
            SolverDiagnostics diagnostics = null)
        {
            List<LevelFrame> frames = new List<LevelFrame>();
            if (caps == null || caps.Count == 0)
            {
                return frames;
            }

            // Snapshot each valid cap's geometry once, collapsing its normal to the +Z hemisphere so a floor
            // and a ceiling of the same slab share an orientation (the elevation band then separates them).
            List<Candidate> candidates = new List<Candidate>();
            for (int i = 0; i < caps.Count; i++)
            {
                Face3D cap = caps[i];
                Plane plane = cap?.GetPlane();
                Point3D centroid = cap?.GetBoundingBox()?.GetCentroid();
                if (plane == null || centroid == null || !cap.IsValid())
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (normal.Z < 0)
                {
                    normal = normal.GetNegated();
                }

                double elevation = normal.X * plane.Origin.X + normal.Y * plane.Origin.Y + normal.Z * plane.Origin.Z;
                candidates.Add(new Candidate(i, plane.Origin, centroid, normal, cap.GetArea(), elevation));
            }

            if (candidates.Count == 0)
            {
                return frames;
            }

            // Deterministic seed order, independent of the caller's input order: the dominant (largest-area)
            // cap seeds first (mirrors NormalizeCaps), ties broken by elevation, then centroid, then index.
            List<Candidate> ordered = candidates
                .OrderByDescending(x => x.Area)
                .ThenBy(x => x.Elevation)
                .ThenBy(x => x.Centroid.X)
                .ThenBy(x => x.Centroid.Y)
                .ThenBy(x => x.Centroid.Z)
                .ThenBy(x => x.Index)
                .ToList();

            double minDot = System.Math.Cos(coneTolerance);
            bool[] assigned = new bool[ordered.Count];

            for (int i = 0; i < ordered.Count; i++)
            {
                if (assigned[i])
                {
                    continue;
                }

                Candidate seed = ordered[i];
                assigned[i] = true;

                List<int> members = new List<int> { seed.Index };
                double dominantArea = seed.Area;

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    if (assigned[j])
                    {
                        continue;
                    }

                    Candidate other = ordered[j];
                    if (System.Math.Abs(seed.Normal.DotProduct(other.Normal)) < minDot)
                    {
                        continue; // a different orientation - not this level's cap
                    }

                    // Perpendicular offset of the candidate's centroid from the SEED datum (signed distance
                    // along the seed normal); within the band => same datum.
                    double perpendicular = seed.Normal.X * (other.Centroid.X - seed.Origin.X)
                        + seed.Normal.Y * (other.Centroid.Y - seed.Origin.Y)
                        + seed.Normal.Z * (other.Centroid.Z - seed.Origin.Z);
                    if (System.Math.Abs(perpendicular) > elevationBand)
                    {
                        continue;
                    }

                    assigned[j] = true;
                    members.Add(other.Index);
                }

                members.Sort();
                frames.Add(new LevelFrame(seed.Normal, seed.Origin, members, dominantArea));
            }

            // Deterministic output order: by elevation, then orientation, then origin - so the frame list is
            // identical regardless of the caller's input order.
            frames = frames
                .OrderBy(x => x.Elevation)
                .ThenBy(x => x.Normal.X)
                .ThenBy(x => x.Normal.Y)
                .ThenBy(x => x.Normal.Z)
                .ThenBy(x => x.Origin.X)
                .ThenBy(x => x.Origin.Y)
                .ThenBy(x => x.Origin.Z)
                .ToList();

            diagnostics?.Add(SolverStage.Snap, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                string.Format("LevelFrame clustering: {0} cap(s) grouped into {1} level frame(s).", candidates.Count, frames.Count));

            return frames;
        }

        /// <summary>
        /// Assigns a single cap face to the level frame it belongs to and returns that frame's index in
        /// <paramref name="frames"/>, or -1 when no frame matches (the caller may then seed a new frame). A cap
        /// matches a frame when it is parallel within <paramref name="coneTolerance"/> and its centroid is
        /// within <paramref name="elevationBand"/> of the datum. When a cap matches more than one frame (its
        /// centroid falls in the band of two nearby datums) the assignment is ambiguous: it is resolved
        /// deterministically to the nearest datum (ties broken toward the wider frame - more member caps, then
        /// larger dominant area, then lower index) and an <see cref="DiagnosticCode.AmbiguousLevelFrame"/>
        /// warning records the choice - never silent (docs plan §E Phase 6, "ambiguous level membership →
        /// diagnostic + widest-frame fallback").
        /// </summary>
        public static int AssignCapToFrame(
            Face3D cap,
            IReadOnlyList<LevelFrame> frames,
            double coneTolerance = DEFAULT_NormalConeTolerance,
            double elevationBand = DEFAULT_ElevationBand,
            SolverDiagnostics diagnostics = null)
        {
            if (frames == null || frames.Count == 0)
            {
                return -1;
            }

            Plane plane = cap?.GetPlane();
            Point3D centroid = cap?.GetBoundingBox()?.GetCentroid();
            if (plane == null || centroid == null)
            {
                return -1;
            }

            Vector3D normal = plane.Normal.Unit;
            double minDot = System.Math.Cos(coneTolerance);

            int matchCount = 0;
            int best = -1;
            double bestDistance = double.MaxValue;
            int bestMembers = -1;
            double bestArea = double.MinValue;

            for (int i = 0; i < frames.Count; i++)
            {
                LevelFrame frame = frames[i];
                if (frame?.Normal == null)
                {
                    continue;
                }

                if (System.Math.Abs(normal.DotProduct(frame.Normal)) < minDot)
                {
                    continue;
                }

                double distance = System.Math.Abs(frame.SignedElevation(centroid));
                if (distance > elevationBand)
                {
                    continue;
                }

                matchCount++;

                // Nearest datum wins; ties break toward the wider (dominant) frame, then the lower index -
                // deterministic, and the "widest-frame fallback" the plan calls for.
                bool better = best < 0
                    || distance < bestDistance - Core.Tolerance.Distance
                    || (System.Math.Abs(distance - bestDistance) <= Core.Tolerance.Distance
                        && (frame.CapIndices.Count > bestMembers
                            || (frame.CapIndices.Count == bestMembers && frame.DominantArea > bestArea)));
                if (better)
                {
                    best = i;
                    bestDistance = distance;
                    bestMembers = frame.CapIndices.Count;
                    bestArea = frame.DominantArea;
                }
            }

            if (matchCount > 1 && diagnostics != null)
            {
                diagnostics.Add(SolverStage.Snap, DiagnosticCode.AmbiguousLevelFrame, OcctDiagnosticSeverity.Warning,
                    string.Format("Cap lies within the elevation band of {0} level frames; assigned to frame {1} (nearest datum).", matchCount, best),
                    point3Ds: new List<Point3D> { centroid }, face3D: cap, toleranceUsed: elevationBand);
            }

            return best;
        }

        /// <summary>
        /// Assigns a wall (a vertical, non-cap face) to the one or more level frames it spans - every frame
        /// whose datum lies between the wall's lowest and highest points (± <paramref name="elevationBand"/>),
        /// measured in that frame. A storey-height wall reaches both the floor datum below and the ceiling
        /// datum above; a wall passing a mid-level landing reaches that datum too. Returns the matching frame
        /// indices ascending. When a wall spans no datum at all (a floating fragment) it is resolved to the
        /// nearest frame by its centroid and an <see cref="DiagnosticCode.AmbiguousLevelFrame"/> warning
        /// records the fallback - never silent.
        /// </summary>
        public static IReadOnlyList<int> AssignWallToFrames(
            Face3D wall,
            IReadOnlyList<LevelFrame> frames,
            double elevationBand = DEFAULT_ElevationBand,
            SolverDiagnostics diagnostics = null)
        {
            List<int> result = new List<int>();
            if (wall == null || frames == null || frames.Count == 0)
            {
                return result;
            }

            List<Point3D> points = (wall.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
            if (points == null || points.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < frames.Count; i++)
            {
                LevelFrame frame = frames[i];
                if (frame?.Normal == null)
                {
                    continue;
                }

                double minElevation = double.MaxValue;
                double maxElevation = double.MinValue;
                foreach (Point3D point in points)
                {
                    double elevation = frame.SignedElevation(point);
                    if (elevation < minElevation)
                    {
                        minElevation = elevation;
                    }

                    if (elevation > maxElevation)
                    {
                        maxElevation = elevation;
                    }
                }

                // The wall spans this datum when its [min, max] elevation interval straddles 0 (within band):
                // the datum plane passes between the wall's foot and top.
                if (minElevation <= elevationBand && maxElevation >= -elevationBand)
                {
                    result.Add(i);
                }
            }

            if (result.Count == 0)
            {
                // No datum falls within the wall's span: a floating fragment. Fall back to the nearest frame
                // by the wall centroid, and record the ambiguity - never drop the wall silently.
                Point3D centroid = wall.GetBoundingBox()?.GetCentroid();
                int nearest = -1;
                double nearestDistance = double.MaxValue;
                if (centroid != null)
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        LevelFrame frame = frames[i];
                        if (frame?.Normal == null)
                        {
                            continue;
                        }

                        double distance = System.Math.Abs(frame.SignedElevation(centroid));
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearest = i;
                        }
                    }
                }

                if (nearest >= 0)
                {
                    result.Add(nearest);
                    diagnostics?.Add(SolverStage.Snap, DiagnosticCode.AmbiguousLevelFrame, OcctDiagnosticSeverity.Warning,
                        string.Format("Wall spans no level frame's datum; assigned to nearest frame {0}.", nearest),
                        face3D: wall, toleranceUsed: elevationBand);
                }
            }

            return result;
        }

        /// <summary>
        /// Classifies <paramref name="face"/> as a <see cref="FaceRole.Wall"/> or <see cref="FaceRole.Cap"/>
        /// against the most relevant level frame, and reports which frame via <paramref name="frameIndex"/>.
        /// The relevant frame is the one the face geometrically belongs to: the datum a cap sits on
        /// (<see cref="AssignCapToFrame"/>), else a datum a wall spans (<see cref="AssignWallToFrames"/>), else
        /// the nearest datum by centroid; the binary wall/cap decision (<see cref="IsWall(Face3D, double)"/>)
        /// is then made <em>in that frame</em>. When no frames exist the classification falls back to the
        /// world-Z test (the pre-Phase-6 behaviour), so a single-frame / no-frame model is unchanged. A
        /// diagnostic naming the frame is emitted only when the chosen frame is meaningfully tilted (where the
        /// frame-aware answer can differ from world Z) - "diagnostics identify the frame used where useful".
        /// </summary>
        public static FaceRole ClassifyFace(
            Face3D face,
            IReadOnlyList<LevelFrame> frames,
            out int frameIndex,
            double verticalAngleTolerance = DEFAULT_VerticalAngleTolerance,
            SolverDiagnostics diagnostics = null)
        {
            frameIndex = -1;
            Plane plane = face?.GetPlane();
            if (plane == null)
            {
                return FaceRole.Cap; // undefined input: default to the non-wall role (never extended as a wall)
            }

            if (frames == null || frames.Count == 0)
            {
                return WorldZClassification(plane, verticalAngleTolerance); // fallback: no frames -> world Z
            }

            // A cap sits on (and parallel to) a datum; AssignCapToFrame naturally rejects a wall (whose normal
            // is perpendicular to every datum, failing the parallel cone).
            int capFrame = AssignCapToFrame(face, frames);
            if (capFrame >= 0)
            {
                frameIndex = capFrame;
                return RoleInFrame(frames[capFrame], face, verticalAngleTolerance, diagnostics);
            }

            // Otherwise a wall spans one or more datums (its foot-to-top range straddles them).
            IReadOnlyList<int> spanned = AssignWallToFrames(face, frames);
            if (spanned.Count > 0)
            {
                frameIndex = spanned[0];
                return RoleInFrame(frames[spanned[0]], face, verticalAngleTolerance, diagnostics);
            }

            // Neither cleanly a cap nor a spanning wall: use the nearest datum's up-axis.
            int nearest = NearestFrameByCentroid(face, frames);
            if (nearest >= 0)
            {
                frameIndex = nearest;
                return RoleInFrame(frames[nearest], face, verticalAngleTolerance, diagnostics);
            }

            return WorldZClassification(plane, verticalAngleTolerance);
        }

        /// <summary>The binary wall/cap decision for <paramref name="face"/> in <paramref name="frame"/>, with
        /// an Info diagnostic naming the frame when it is meaningfully tilted (so the frame-aware result can
        /// differ from world Z) - flat frames stay quiet.</summary>
        private static FaceRole RoleInFrame(LevelFrame frame, Face3D face, double verticalAngleTolerance, SolverDiagnostics diagnostics)
        {
            FaceRole role = frame.IsWall(face, verticalAngleTolerance) ? FaceRole.Wall : FaceRole.Cap;
            if (diagnostics != null && frame.TiltAngle > Core.Tolerance.Angle)
            {
                diagnostics.Add(SolverStage.Snap, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                    string.Format("LevelFrame: classified face as {0} in a tilted level frame (tilt {1:0.#}°) - frame-aware, not world-Z.",
                        role, frame.TiltAngle * (180.0 / System.Math.PI)),
                    face3D: face);
            }

            return role;
        }

        /// <summary>Index of the frame whose datum is nearest <paramref name="face"/>'s centroid (perpendicular
        /// offset), or -1 when the centroid or every frame is unusable - the last-resort frame pick.</summary>
        private static int NearestFrameByCentroid(Face3D face, IReadOnlyList<LevelFrame> frames)
        {
            Point3D centroid = face?.GetBoundingBox()?.GetCentroid();
            if (centroid == null)
            {
                return -1;
            }

            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < frames.Count; i++)
            {
                LevelFrame frame = frames[i];
                if (frame?.Normal == null)
                {
                    continue;
                }

                double distance = System.Math.Abs(frame.SignedElevation(centroid));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The pre-Phase-6 world-Z wall/cap decision - the fallback when no level frames exist.</summary>
        private static FaceRole WorldZClassification(Plane plane, double verticalAngleTolerance)
        {
            bool wall = System.Math.Abs(plane.Normal.Unit.Z) <= System.Math.Sin(verticalAngleTolerance);
            return wall ? FaceRole.Wall : FaceRole.Cap;
        }

        /// <summary>An input cap's geometry, snapshotted once for <see cref="Cluster"/> (normal collapsed to
        /// the +Z hemisphere).</summary>
        private readonly struct Candidate
        {
            public int Index { get; }

            public Point3D Origin { get; }

            public Point3D Centroid { get; }

            public Vector3D Normal { get; }

            public double Area { get; }

            public double Elevation { get; }

            public Candidate(int index, Point3D origin, Point3D centroid, Vector3D normal, double area, double elevation)
            {
                Index = index;
                Origin = origin;
                Centroid = centroid;
                Normal = normal;
                Area = area;
                Elevation = elevation;
            }
        }
    }
}
