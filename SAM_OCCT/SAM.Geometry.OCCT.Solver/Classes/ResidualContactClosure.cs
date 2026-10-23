// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Original-source lineage for <see cref="ResidualContactClosure"/>, keyed by the clean-face ordinal each Step-2
    /// <see cref="SnappedPanel"/> carries as its first <see cref="SnappedPanel.SourceIndices"/> entry. Built only on the
    /// one-step, untilted, uniform-growth path (null everywhere else, so Fill behaves exactly as before).
    /// </summary>
    internal sealed class ResidualContactLineage
    {
        private readonly List<List<Face3D>> allSources = new List<List<Face3D>>();
        private readonly List<List<Face3D>> contactSources = new List<List<Face3D>>();

        /// <param name="sourceFace3Ds">The ORIGINAL input faces (world frame).</param>
        /// <param name="sourceIndicesPerFace">SnapStage attribution: clean ordinal -> original source indices.</param>
        /// <param name="geometricPerFace">SnapStage: true when the attribution was geometric, false for the nearest-panel fallback.</param>
        /// <param name="levelReach">See <see cref="LevelReach"/>.</param>
        /// <param name="maxCapture">See <see cref="MaxCapture"/>.</param>
        /// <param name="angleTolerance">Horizontal/vertical/parallel angle tolerance (the solver's ToleranceAngle).</param>
        internal ResidualContactLineage(IList<Face3D> sourceFace3Ds, IList<List<int>> sourceIndicesPerFace, IList<bool> geometricPerFace, double levelReach, double maxCapture, double angleTolerance)
        {
            LevelReach = levelReach;
            MaxCapture = maxCapture;
            AngleTolerance = angleTolerance;

            // A source attributed to more than one clean face (a centre-inside match for several faces) is ambiguous:
            // it never creates or exempts a contact.
            Dictionary<int, int> uses = new Dictionary<int, int>();
            foreach (int index in sourceIndicesPerFace.SelectMany(x => x ?? new List<int>()))
            {
                uses.TryGetValue(index, out int count);
                uses[index] = count + 1;
            }

            for (int k = 0; k < sourceIndicesPerFace.Count; k++)
            {
                List<int> indices = (sourceIndicesPerFace[k] ?? new List<int>()).Where(i => i >= 0 && i < sourceFace3Ds.Count && sourceFace3Ds[i] != null).ToList();
                bool geometric = k < geometricPerFace.Count && geometricPerFace[k];
                allSources.Add(indices.Select(i => sourceFace3Ds[i]).ToList());
                contactSources.Add(geometric ? indices.Where(i => uses[i] == 1).Select(i => sourceFace3Ds[i]).ToList() : new List<Face3D>());
            }
        }

        /// <summary>|z(source cap) - z(cap)| a source cap may differ by: max(bucketBetweenLevels, normalizeCapOffset).</summary>
        internal double LevelReach { get; }

        /// <summary>The largest Stage-A move that can separate an authored contact (bucket, align, double-wall gap).</summary>
        internal double MaxCapture { get; }

        internal double AngleTolerance { get; }

        /// <summary>One Info line per applied closure (the caller adds them to the solver diagnostics).</summary>
        internal List<string> Messages { get; } = new List<string>();

        /// <summary>Every attributed original source (used where more evidence only blocks: authored uniqueness).</summary>
        internal List<Face3D> AllSources(SnappedPanel panel) => Get(allSources, panel);

        /// <summary>Sources that may create or exempt a contact: geometric attribution and unambiguous.</summary>
        internal List<Face3D> ContactSources(SnappedPanel panel) => Get(contactSources, panel);

        private static List<Face3D> Get(List<List<Face3D>> lists, SnappedPanel panel)
        {
            int ordinal = panel?.SourceIndices != null && panel.SourceIndices.Count > 0 ? panel.SourceIndices[0] : -1;
            return ordinal >= 0 && ordinal < lists.Count ? lists[ordinal] : new List<Face3D>();
        }
    }

    /// <summary>
    /// Residual cap-edge closure (rule E, uniform Fill only). After every cap's existing grow, a cap edge that was
    /// AUTHORED on a wall (source cap edge on the source wall) but is still short of that wall because conditioning
    /// moved the wall away is moved onto the wall plane plus the fill overshoot - but only when the source contact
    /// corresponds to the current cap edge, the wall stands continuously at cap height, the swept region is free of
    /// every other non-horizontal panel and of the cap itself, and the result is a simple polygon. A cap edge that was
    /// never authored in contact (an intentional void) is never touched. Every ambiguous case fails closed.
    /// </summary>
    internal static class ResidualContactClosure
    {
        private const double ContactTolerance = Panel3DSnapSolver.STACK_FOLLOW_END_TOLERANCE; // 0.02 m
        private const double FacingAngle = 15.0 * (System.Math.PI / 180.0); // = SnappedPanel's directional facing cone
        private const double SectionDelta = 1e-4; // sections at z +- delta: a wall ending exactly at cap height counts
        private const double InteriorShrink = 1e-5; // geometry merely touching a region boundary does not count
        private const double FlatTolerance = 1e-6; // a cap is flat when its z-range is within this

        private static readonly GeometryFactory factory = new GeometryFactory();

        internal sealed class Closure
        {
            internal int CapIndex, WallIndex;
            internal double AreaBefore, AreaAfter, Move, J0, J1, ShortfallMin, ShortfallMax;
            internal Point3D From, To;
            internal string Message;
        }

        private sealed class Contact
        {
            internal Face3D CapSource;
            internal Point3D EdgeA, EdgeB, A, B;
        }

        private sealed class Decision
        {
            internal int Cap, Wall;
            internal bool Fire;
            internal double J0, J1, Move, DMin, DMax;
            internal Vector3D Toward, U;
            internal List<int> Facing = new List<int>();
            internal List<Point3D> Chain = new List<Point3D>();
        }

        /// <summary>Evaluates every cap/wall pair on the post-grow geometry, then applies each cap's single closure.</summary>
        internal static List<Closure> Apply(List<SnappedPanel> panels, ResidualContactLineage lineage, double margin, double overshoot, double tolerance)
        {
            List<Closure> result = new List<Closure>();
            if (panels == null || lineage == null || margin <= tolerance)
            {
                return result;
            }

            List<Decision> fired = new List<Decision>();
            for (int c = 0; c < panels.Count; c++)
            {
                if (!IsFlatCap(panels[c], lineage.AngleTolerance))
                {
                    continue;
                }

                BoundingBox3D capBox = panels[c].GetBoundingBox();
                double zc = capBox.Min.Z;
                double reach = margin + overshoot + ContactTolerance;
                for (int w = 0; w < panels.Count; w++)
                {
                    if (w == c || !panels[w].IsVertical(lineage.AngleTolerance))
                    {
                        continue;
                    }

                    BoundingBox3D wallBox = panels[w].GetBoundingBox();
                    if (wallBox.Min.Z > zc + SectionDelta || wallBox.Max.Z < zc - SectionDelta
                        || capBox.Min.X - reach > wallBox.Max.X || capBox.Max.X + reach < wallBox.Min.X
                        || capBox.Min.Y - reach > wallBox.Max.Y || capBox.Max.Y + reach < wallBox.Min.Y)
                    {
                        continue;
                    }

                    Decision decision = Evaluate(panels, lineage, c, w, margin, overshoot, tolerance);
                    if (decision.Fire)
                    {
                        fired.Add(decision);
                    }
                }
            }

            // One closure per cap: a cap with several qualifying walls is ambiguous and stays as it is.
            foreach (IGrouping<int, Decision> group in fired.GroupBy(x => x.Cap).OrderBy(x => x.Key))
            {
                if (group.Count() != 1)
                {
                    continue;
                }

                Decision d = group.First();
                SnappedPanel cap = panels[d.Cap];
                double areaBefore = cap.GetArea();
                if (!MoveChain(cap, d, lineage.AngleTolerance, tolerance))
                {
                    continue;
                }

                Point3D from = Lerp(d.Chain[0], d.Chain[d.Chain.Count - 1], 0.5), to = Shift(from, d.Toward, d.Move);
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Residual cap-edge closure: cap #{0} edge authored on wall #{1} moved {2:0.###} m onto the wall plane plus {3:0.###} m overshoot over {4:0.###} m (shortfall {5:0.###}..{6:0.###} m); area {7:0.###} -> {8:0.###} m2.",
                    d.Cap, d.Wall, d.DMax, overshoot, d.J1 - d.J0, d.DMin, d.DMax, areaBefore, cap.GetArea());
                lineage.Messages.Add(message);
                result.Add(new Closure
                {
                    CapIndex = d.Cap, WallIndex = d.Wall, AreaBefore = areaBefore, AreaAfter = cap.GetArea(), Move = d.Move,
                    J0 = d.J0, J1 = d.J1, ShortfallMin = d.DMin, ShortfallMax = d.DMax, From = from, To = to, Message = message
                });
            }

            return result;
        }

        // ------------------------------------------------------------------------------------------------ eligibility
        private static Decision Evaluate(List<SnappedPanel> panels, ResidualContactLineage lineage, int c, int w, double margin, double overshoot, double tolerance)
        {
            Decision d = new Decision { Cap = c, Wall = w };
            Face3D cap = panels[c].Face3D, wall = panels[w].Face3D;
            double zc = cap.GetBoundingBox().Min.Z;
            (Vector3D n, Vector3D u, Point3D o) = WallFrame(wall);

            // The wall's actual section at cap height.
            List<(Point3D, Point3D)> secW = SectionSegments(wall, zc);
            if (secW.Count == 0)
            {
                return d;
            }

            List<(double, double)> secWIntervals = Merge(secW.Select(s => Ordered(Along(s.Item1, u), Along(s.Item2, u))).ToList(), tolerance);

            // Source contact: a source cap edge of the cap's lineage lay on a source wall of the wall's lineage, and
            // only a Stage-A move (<= MaxCapture) can have separated them.
            List<Face3D> wallSources = lineage.ContactSources(panels[w]);
            List<Contact> contacts = new List<Contact>();
            foreach (Face3D capSource in lineage.ContactSources(panels[c]))
            {
                foreach (Face3D wallSource in wallSources)
                {
                    contacts.AddRange(ContactsBetween(capSource, wallSource, lineage.AngleTolerance));
                }
            }

            contacts = contacts.Where(x => System.Math.Abs(Signed(x.A, n, o)) <= lineage.MaxCapture + ContactTolerance && System.Math.Abs(Signed(x.B, n, o)) <= lineage.MaxCapture + ContactTolerance).ToList();
            if (contacts.Count == 0)
            {
                return d; // never authored together: an intentional void is kept
            }

            List<(double, double)> intervals = Merge(contacts.Select(x => Ordered(Along(x.A, u), Along(x.B, u))).ToList(), tolerance);
            Dictionary<int, List<Point3D>> partners = Partners(panels, lineage, c, w, contacts);

            List<Point3D> ring = SnappedPanel.BoundaryPoints(cap);
            double area2 = SignedArea2(ring);
            double imageBand = margin + overshoot + ContactTolerance;
            var candidates = new List<(double J0, double J1, List<int> Facing, Vector3D Toward, List<Point3D> Chain, List<double> Dist)>();
            foreach ((double a0, double a1) in intervals)
            {
                List<Contact> here = contacts.Where(x => System.Math.Max(Along(x.A, u), Along(x.B, u)) >= a0 - tolerance && System.Math.Min(Along(x.A, u), Along(x.B, u)) <= a1 + tolerance).ToList();
                bool NearImage(Point3D p) => here.Any(x => LineDistance(p, x.A, x.B) <= imageBand);
                double b0 = a0, b1 = a1;
                foreach (Point3D corner in partners.Values.SelectMany(x => x))
                {
                    double ac = Along(corner, u);
                    if (System.Math.Abs(ac - a0) <= ContactTolerance) b0 = System.Math.Min(b0, a0 - overshoot);
                    if (System.Math.Abs(ac - a1) <= ContactTolerance) b1 = System.Math.Max(b1, a1 + overshoot);
                }

                // The current cap's facing chain that is the conditioned image of the authored edge.
                double s0 = 0;
                List<int> facing = new List<int>();
                for (int k = 0; k < ring.Count; k++)
                {
                    Point3D p = ring[k], q = ring[(k + 1) % ring.Count];
                    double ex = q.X - p.X, ey = q.Y - p.Y, el = System.Math.Sqrt((ex * ex) + (ey * ey));
                    if (el <= tolerance) continue;
                    double ox = (area2 > 0 ? ey : -ey) / el, oy = (area2 > 0 ? -ex : ex) / el; // outward normal
                    double pa = Along(p, u), qa = Along(q, u);
                    if (System.Math.Max(pa, qa) <= b0 + tolerance || System.Math.Min(pa, qa) >= b1 - tolerance) continue;
                    if (!NearImage(p) || !NearImage(q)) continue;
                    double sm = Signed(new Point3D((p.X + q.X) / 2, (p.Y + q.Y) / 2, zc), n, o);
                    double tx = sm > 0 ? -n.X : n.X, ty = sm > 0 ? -n.Y : n.Y;
                    if ((ox * tx) + (oy * ty) >= System.Math.Cos(FacingAngle)) { facing.Add(k); s0 = sm; }
                }

                if (facing.Count == 0) continue; // the cap reaches or passes the wall there
                facing.Sort();
                HashSet<int> facingSet = new HashSet<int>(facing);
                if (facing.Count(k => !facingSet.Contains((k + 1) % ring.Count)) != 1) return d; // not one cyclic run: fail closed
                double chainMin = facing.SelectMany(k => new[] { Along(ring[k], u), Along(ring[(k + 1) % ring.Count], u) }).Min();
                double chainMax = facing.SelectMany(k => new[] { Along(ring[k], u), Along(ring[(k + 1) % ring.Count], u) }).Max();
                double j0c = System.Math.Max(b0, chainMin), j1c = System.Math.Min(b1, chainMax);
                if (j1c - j0c <= ContactTolerance) continue;
                Vector3D toward = s0 > 0 ? new Vector3D(-n.X, -n.Y, 0) : new Vector3D(n.X, n.Y, 0);
                List<Point3D> chain = ChainPoints(ring, facing, u, j0c, j1c, tolerance);
                if (chain.Count < 2) continue;
                List<double> dist = chain.Select(p => ((o.X - p.X) * toward.X) + ((o.Y - p.Y) * toward.Y)).ToList();
                if (dist.Min() < -tolerance) return d; // the chain crosses the wall plane: fail closed
                if (dist.Max() <= tolerance || dist.Max() > margin + tolerance) continue; // already closed / out of reach

                // The wall must stand continuously at cap height over the contact interval.
                (double, double)? host = secWIntervals.Cast<(double, double)?>().FirstOrDefault(s => s.Value.Item1 <= System.Math.Max(a0, j0c) + ContactTolerance && s.Value.Item2 >= System.Math.Min(a1, j1c) - ContactTolerance);
                if (host == null) return d;
                double jj0 = System.Math.Max(j0c, host.Value.Item1), jj1 = System.Math.Min(j1c, host.Value.Item2);
                chain = ChainPoints(ring, facing, u, jj0, jj1, tolerance);
                if (chain.Count < 2) continue;
                dist = chain.Select(p => ((o.X - p.X) * toward.X) + ((o.Y - p.Y) * toward.Y)).ToList();

                // Correspondence AT THE SOURCE-CONTACT EDGE: the source cap's own band along its contact segment lies
                // inside the current cap, and the source is on the cap's level group.
                if (!here.Any(x => EdgeCorresponds(x, cap, zc, u, jj0, jj1, lineage.LevelReach))) return d;
                candidates.Add((jj0, jj1, facing, toward, chain, dist));
            }

            if (candidates.Count != 1)
            {
                return d; // none, or several short intervals: fail closed
            }

            var cand = candidates[0];
            d.J0 = cand.J0; d.J1 = cand.J1; d.Toward = cand.Toward; d.U = u; d.Facing = cand.Facing; d.Chain = cand.Chain;
            d.DMin = cand.Dist.Min(); d.DMax = cand.Dist.Max(); d.Move = d.DMax + overshoot;

            // Strip S (cap edge -> wall plane) and swept region R (cap edge -> moved edge, overshoot included).
            List<NetTopologySuite.Geometries.Geometry> sParts = new List<NetTopologySuite.Geometries.Geometry>(), rParts = new List<NetTopologySuite.Geometries.Geometry>();
            for (int i = 0; i + 1 < cand.Chain.Count; i++)
            {
                Point3D a = cand.Chain[i], b = cand.Chain[i + 1];
                sParts.Add(Quad(a, b, Shift(b, d.Toward, cand.Dist[i + 1]), Shift(a, d.Toward, cand.Dist[i])));
                rParts.Add(Quad(a, b, Shift(b, d.Toward, d.Move), Shift(a, d.Toward, d.Move)));
            }

            NetTopologySuite.Geometries.Geometry S = UnaryUnionOp.Union(sParts.Where(x => !x.IsEmpty).ToList());
            NetTopologySuite.Geometries.Geometry R = UnaryUnionOp.Union(rParts.Where(x => !x.IsEmpty).ToList());
            NetTopologySuite.Geometries.Geometry rInterior = R.Buffer(-InteriorShrink), sInterior = S.Buffer(-InteriorShrink);
            double span = d.J1 - d.J0;

            // The move must not cross the cap's own geometry.
            if (rInterior.Intersection(PlanPolygon(cap)).Area > tolerance * span)
            {
                return d;
            }

            // Coverage by every coplanar cap (true polygons with holes), the cap itself included.
            List<NetTopologySuite.Geometries.Geometry> covers = panels.Where(p => IsFlatCap(p, lineage.AngleTolerance) && System.Math.Abs(p.GetBoundingBox().Min.Z - zc) <= SectionDelta).Select(p => PlanPolygon(p.Face3D)).ToList();
            if (S.Difference(UnaryUnionOp.Union(covers)).Area <= tolerance * span)
            {
                return d; // already closed by another cap
            }

            // Blockers: EVERY non-horizontal panel (vertical, tilted, sloped) whose section at cap height enters R.
            for (int k = 0; k < panels.Count; k++)
            {
                if (k == w || k == c || IsFlatCap(panels[k], lineage.AngleTolerance))
                {
                    continue;
                }

                Face3D v = panels[k].Face3D;
                if (panels[k].IsVertical(lineage.AngleTolerance) && IsCoplanarWall(v, n, o))
                {
                    continue; // the target surface itself (coplanar continuation)
                }

                foreach ((Point3D a, Point3D b) in SectionSegments(v, zc))
                {
                    LineString line = factory.CreateLineString(new[] { new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y) });
                    if (!line.Intersects(rInterior)) continue;
                    if (partners.ContainsKey(k) && NearPartnerCorner(line.Intersection(rInterior), partners[k], u, overshoot)) continue;
                    return d; // blocked inside the strip or inside the overshoot zone
                }
            }

            // Authored uniqueness: no other wall's SOURCE foot inside the strip (every attributed source counts).
            HashSet<Face3D> targetSources = new HashSet<Face3D>(lineage.AllSources(panels[w]));
            for (int k = 0; k < panels.Count; k++)
            {
                if (k == w || k == c || !panels[k].IsVertical(lineage.AngleTolerance) || partners.ContainsKey(k)) continue;
                foreach (Face3D sv in lineage.AllSources(panels[k]).Where(x => !targetSources.Contains(x) && IsVertical(x, lineage.AngleTolerance)))
                {
                    foreach (Contact ct in contacts)
                    {
                        foreach ((Point3D a, Point3D b) in SectionSegments(sv, ct.CapSource.GetBoundingBox().Min.Z))
                        {
                            if (factory.CreateLineString(new[] { new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y) }).Intersects(sInterior)) return d;
                        }
                    }
                }
            }

            d.Fire = true;
            return d;
        }

        private static bool EdgeCorresponds(Contact x, Face3D cap, double zc, Vector3D u, double j0, double j1, double levelReach)
        {
            if (System.Math.Abs(x.CapSource.GetBoundingBox().Min.Z - zc) > levelReach + FlatTolerance) return false;
            double pa = Along(x.A, u), pb = Along(x.B, u);
            double lo = System.Math.Max(System.Math.Min(pa, pb), j0), hi = System.Math.Min(System.Math.Max(pa, pb), j1);
            if (hi - lo <= ContactTolerance) return false;
            Point3D p = Lerp(x.A, x.B, (lo - pa) / (pb - pa)), q = Lerp(x.A, x.B, (hi - pa) / (pb - pa));
            double length = PlanDistance(p, q);
            Vector3D normal = new Vector3D(-(q.Y - p.Y) / length, (q.X - p.X) / length, 0);
            NetTopologySuite.Geometries.Geometry source = PlanPolygon(x.CapSource);
            Point3D m = Lerp(p, q, 0.5);
            Vector3D side = source.Contains(factory.CreatePoint(new Coordinate(m.X + (normal.X * 1e-3), m.Y + (normal.Y * 1e-3)))) ? normal
                : source.Contains(factory.CreatePoint(new Coordinate(m.X - (normal.X * 1e-3), m.Y - (normal.Y * 1e-3)))) ? new Vector3D(-normal.X, -normal.Y, 0) : null;
            if (side == null) return false;
            NetTopologySuite.Geometries.Geometry band = Quad(p, q, Shift(q, side, ContactTolerance), Shift(p, side, ContactTolerance)).Intersection(source);
            if (band.Area < 0.5 * ContactTolerance * length) return false;
            return band.Difference(PlanPolygon(cap)).Area <= (1e-6 * length) + 1e-12;
        }

        private static bool NearPartnerCorner(NetTopologySuite.Geometries.Geometry intersection, List<Point3D> corners, Vector3D u, double overshoot)
        {
            foreach (Coordinate co in intersection.Coordinates)
            {
                double a = (co.X * u.X) + (co.Y * u.Y);
                if (!corners.Any(cn => System.Math.Abs(a - Along(cn, u)) <= ContactTolerance + overshoot + 1e-9)) return false;
            }

            return true;
        }

        private static List<Contact> ContactsBetween(Face3D capSource, Face3D wallSource, double angleTolerance)
        {
            List<Contact> result = new List<Contact>();
            if (!IsFlat(capSource, angleTolerance) || !IsVertical(wallSource, angleTolerance)) return result;
            double zs = capSource.GetBoundingBox().Min.Z;
            BoundingBox3D wb = wallSource.GetBoundingBox();
            if (zs < wb.Min.Z - ContactTolerance || zs > wb.Max.Z + ContactTolerance) return result;
            (Vector3D nw, Vector3D uw, Point3D ow) = WallFrame(wallSource);
            double zSection = System.Math.Min(System.Math.Max(zs, wb.Min.Z + (SectionDelta * 2)), wb.Max.Z - (SectionDelta * 2));
            List<(double, double)> sec = Merge(SectionSegments(wallSource, zSection).Select(s => Ordered(Along(s.Item1, uw), Along(s.Item2, uw))).ToList(), 1e-9);
            List<Point3D> ring = SnappedPanel.BoundaryPoints(capSource);
            for (int k = 0; k < ring.Count; k++)
            {
                Point3D p = ring[k], q = ring[(k + 1) % ring.Count];
                double length = PlanDistance(p, q);
                if (length <= ContactTolerance) continue;
                if (System.Math.Abs(Signed(p, nw, ow)) > ContactTolerance || System.Math.Abs(Signed(q, nw, ow)) > ContactTolerance) continue;
                double pa = Along(p, uw), qa = Along(q, uw);
                if (System.Math.Abs(System.Math.Abs(qa - pa) - length) > length * (1 - System.Math.Cos(angleTolerance))) continue; // parallel
                foreach ((double s0, double s1) in sec)
                {
                    double lo = System.Math.Max(System.Math.Min(pa, qa), s0), hi = System.Math.Min(System.Math.Max(pa, qa), s1);
                    if (hi - lo <= ContactTolerance) continue;
                    result.Add(new Contact { CapSource = capSource, EdgeA = p, EdgeB = q, A = Lerp(p, q, (lo - pa) / (qa - pa)), B = Lerp(p, q, (hi - pa) / (qa - pa)) });
                }
            }

            return result;
        }

        /// <summary>Corner partners: other walls whose (contact) source also bounds the same source cap on an edge sharing
        /// an end point with a contact edge that the contact reaches.</summary>
        private static Dictionary<int, List<Point3D>> Partners(List<SnappedPanel> panels, ResidualContactLineage lineage, int c, int w, List<Contact> contacts)
        {
            Dictionary<int, List<Point3D>> result = new Dictionary<int, List<Point3D>>();
            for (int k = 0; k < panels.Count; k++)
            {
                if (k == w || k == c || !panels[k].IsVertical(lineage.AngleTolerance)) continue;
                foreach (Face3D sv in lineage.ContactSources(panels[k]))
                {
                    foreach (Contact ct in contacts)
                    {
                        foreach (Contact other in ContactsBetween(ct.CapSource, sv, lineage.AngleTolerance))
                        {
                            foreach (Point3D end in new[] { ct.EdgeA, ct.EdgeB })
                            {
                                bool shares = PlanDistance(end, other.EdgeA) <= ContactTolerance || PlanDistance(end, other.EdgeB) <= ContactTolerance;
                                bool sameEdge = PlanDistance(other.EdgeA, ct.EdgeA) <= ContactTolerance && PlanDistance(other.EdgeB, ct.EdgeB) <= ContactTolerance;
                                bool reachesEnd = PlanDistance(end, ct.A) <= ContactTolerance || PlanDistance(end, ct.B) <= ContactTolerance;
                                if (shares && !sameEdge && reachesEnd)
                                {
                                    if (!result.TryGetValue(k, out List<Point3D> list)) result[k] = list = new List<Point3D>();
                                    list.Add(end);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        // ------------------------------------------------------------------------------------------------ move
        /// <summary>Inserts the split points at J's ends on the facing chain, then moves the run between them through
        /// <see cref="SnappedPanel.MoveSplitBoundaryRun"/>; the result must be a simple polygon.</summary>
        private static bool MoveChain(SnappedPanel cap, Decision d, double angleTolerance, double tolerance)
        {
            List<Point3D> ring = SnappedPanel.BoundaryPoints(cap.Face3D);
            HashSet<int> facing = new HashSet<int>(d.Facing);
            List<Point3D> split = new List<Point3D>();
            List<bool> onChain = new List<bool>();
            for (int k = 0; k < ring.Count; k++)
            {
                Point3D p = ring[k], q = ring[(k + 1) % ring.Count];
                split.Add(p);
                onChain.Add(facing.Contains(k) || facing.Contains((k - 1 + ring.Count) % ring.Count));
                if (!facing.Contains(k)) continue;
                double pa = Along(p, d.U), qa = Along(q, d.U);
                if (System.Math.Abs(qa - pa) <= tolerance) continue;
                foreach (double j in new[] { d.J0, d.J1 }.OrderBy(x => (x - pa) / (qa - pa)))
                {
                    double t = (j - pa) / (qa - pa);
                    if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                    split.Add(Lerp(p, q, t));
                    onChain.Add(true);
                }
            }

            int count = split.Count;
            bool[] inRun = new bool[count];
            for (int k = 0; k < count; k++)
            {
                double a = Along(split[k], d.U);
                inRun[k] = onChain[k] && a >= d.J0 - 1e-7 && a <= d.J1 + 1e-7;
            }

            int first = Enumerable.Range(0, count).Where(k => inRun[k] && !inRun[(k - 1 + count) % count]).DefaultIfEmpty(-1).First();
            int runCount = inRun.Count(x => x);
            if (first < 0 || runCount < 2 || Enumerable.Range(0, runCount).Any(k => !inRun[(first + k) % count]))
            {
                return false;
            }

            int last = (first + runCount - 1) % count;
            return cap.MoveSplitBoundaryRun(split, first, last, d.Toward.X * d.Move, d.Toward.Y * d.Move, angleTolerance, tolerance, moved => RawPlanPolygon(moved).IsValid);
        }

        // ------------------------------------------------------------------------------------------------ geometry
        private static bool IsFlat(Face3D face3D, double angleTolerance)
        {
            Plane plane = face3D?.GetPlane();
            BoundingBox3D box = face3D?.GetBoundingBox();
            return plane != null && box != null && System.Math.Abs(plane.Normal.Unit.Z) >= System.Math.Cos(angleTolerance) && box.Max.Z - box.Min.Z <= FlatTolerance;
        }

        private static bool IsFlatCap(SnappedPanel panel, double angleTolerance) => panel?.Face3D != null && IsFlat(panel.Face3D, angleTolerance);

        private static bool IsVertical(Face3D face3D, double angleTolerance)
        {
            Plane plane = face3D?.GetPlane();
            return plane != null && System.Math.Abs(plane.Normal.Unit.Z) <= System.Math.Sin(angleTolerance);
        }

        private static (Vector3D n, Vector3D u, Point3D o) WallFrame(Face3D wall)
        {
            Vector3D n0 = wall.GetPlane().Normal.Unit;
            double l = System.Math.Sqrt((n0.X * n0.X) + (n0.Y * n0.Y));
            Vector3D n = new Vector3D(n0.X / l, n0.Y / l, 0);
            return (n, new Vector3D(-n.Y, n.X, 0), wall.GetPlane().Origin);
        }

        private static bool IsCoplanarWall(Face3D v, Vector3D n, Point3D o)
        {
            (Vector3D nv, _, _) = WallFrame(v);
            if (System.Math.Abs((nv.X * n.Y) - (nv.Y * n.X)) > 1e-6) return false;
            return SnappedPanel.BoundaryPoints(v).All(p => System.Math.Abs(Signed(p, n, o)) <= 1e-5);
        }

        /// <summary>Exact section of any non-horizontal planar face (external loop and holes) with the horizontal planes
        /// z - delta and z + delta: even-odd pairing of the loop crossings along the face's horizontal direction.</summary>
        private static List<(Point3D, Point3D)> SectionSegments(Face3D face3D, double z)
        {
            List<(Point3D, Point3D)> result = new List<(Point3D, Point3D)>();
            Plane plane = face3D?.GetPlane();
            if (plane == null) return result;
            Vector3D nf = plane.Normal.Unit;
            double hl = System.Math.Sqrt((nf.X * nf.X) + (nf.Y * nf.Y));
            if (hl <= 1e-9) return result;
            Vector3D u = new Vector3D(-nf.Y / hl, nf.X / hl, 0);
            List<List<Point3D>> loops = new List<List<Point3D>> { SnappedPanel.BoundaryPoints(face3D) };
            loops.AddRange((face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>()).Select(h => (h as ISegmentable3D)?.GetPoints()).Where(x => x != null));
            foreach (double zz in new[] { z - SectionDelta, z + SectionDelta })
            {
                List<Point3D> hits = new List<Point3D>();
                foreach (List<Point3D> loop in loops)
                {
                    for (int i = 0; i < loop.Count; i++)
                    {
                        Point3D a = loop[i], b = loop[(i + 1) % loop.Count];
                        double da = a.Z - zz, db = b.Z - zz;
                        if ((da < 0 && db >= 0) || (da >= 0 && db < 0)) hits.Add(Lerp(a, b, da / (da - db)));
                    }
                }

                hits = hits.OrderBy(p => Along(p, u)).ToList();
                for (int i = 0; i + 1 < hits.Count; i += 2) result.Add((hits[i], hits[i + 1]));
            }

            return result;
        }

        private static List<Point3D> ChainPoints(List<Point3D> ring, List<int> facing, Vector3D u, double j0, double j1, double tolerance)
        {
            List<Point3D> points = new List<Point3D>();
            foreach (int k in facing)
            {
                Point3D p = ring[k], q = ring[(k + 1) % ring.Count];
                double pa = Along(p, u), qa = Along(q, u);
                double lo = System.Math.Max(System.Math.Min(pa, qa), j0), hi = System.Math.Min(System.Math.Max(pa, qa), j1);
                if (hi - lo <= tolerance) continue;
                points.Add(Lerp(p, q, (lo - pa) / (qa - pa)));
                points.Add(Lerp(p, q, (hi - pa) / (qa - pa)));
            }

            List<Point3D> result = new List<Point3D>();
            foreach (Point3D p in points.OrderBy(x => Along(x, u)))
            {
                if (result.Count == 0 || PlanDistance(result[result.Count - 1], p) > 1e-9) result.Add(p);
            }

            return result;
        }

        private static List<(double, double)> Merge(List<(double, double)> intervals, double tolerance)
        {
            List<(double, double)> result = new List<(double, double)>();
            foreach ((double a, double b) in intervals.OrderBy(x => x.Item1))
            {
                if (result.Count > 0 && a <= result[result.Count - 1].Item2 + tolerance)
                    result[result.Count - 1] = (result[result.Count - 1].Item1, System.Math.Max(result[result.Count - 1].Item2, b));
                else
                    result.Add((a, b));
            }

            return result;
        }

        /// <summary>Plan (XY) ring through SAM's own NTS conversion.</summary>
        private static LinearRing Ring(IEnumerable<Point3D> point3Ds) => point3Ds.Select(p => new Point2D(p.X, p.Y)).ToNTS_LinearRing();

        private static Polygon RawPlanPolygon(Face3D face3D)
        {
            LinearRing shell = Ring(SnappedPanel.BoundaryPoints(face3D));
            LinearRing[] holes = (face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>()).Select(h => (h as ISegmentable3D)?.GetPoints()).Where(x => x != null).Select(Ring).ToArray();
            return factory.CreatePolygon(shell, holes);
        }

        private static NetTopologySuite.Geometries.Geometry PlanPolygon(Face3D face3D)
        {
            Polygon polygon = RawPlanPolygon(face3D);
            return polygon.IsValid ? (NetTopologySuite.Geometries.Geometry)polygon : polygon.Buffer(0);
        }

        private static NetTopologySuite.Geometries.Geometry Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            Polygon polygon = factory.CreatePolygon(Ring(new[] { a, b, c, d }));
            return polygon.IsValid ? (NetTopologySuite.Geometries.Geometry)polygon : polygon.Buffer(0);
        }

        private static double SignedArea2(List<Point3D> r)
        {
            double s = 0;
            for (int i = 0; i < r.Count; i++) s += (r[i].X * r[(i + 1) % r.Count].Y) - (r[(i + 1) % r.Count].X * r[i].Y);
            return s;
        }

        private static double Along(Point3D p, Vector3D u) => (p.X * u.X) + (p.Y * u.Y);
        private static double Signed(Point3D p, Vector3D n, Point3D o) => ((p.X - o.X) * n.X) + ((p.Y - o.Y) * n.Y);
        private static (double, double) Ordered(double a, double b) => a <= b ? (a, b) : (b, a);
        private static Point3D Lerp(Point3D a, Point3D b, double t) => new Point3D(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));
        private static Point3D Shift(Point3D p, Vector3D v, double d) => new Point3D(p.X + (v.X * d), p.Y + (v.Y * d), p.Z);
        private static double PlanDistance(Point3D a, Point3D b) => System.Math.Sqrt(System.Math.Pow(a.X - b.X, 2) + System.Math.Pow(a.Y - b.Y, 2));

        private static double LineDistance(Point3D p, Point3D a, Point3D b)
        {
            double ex = b.X - a.X, ey = b.Y - a.Y, el = System.Math.Sqrt((ex * ex) + (ey * ey));
            return el <= 1e-12 ? PlanDistance(p, a) : System.Math.Abs(((p.X - a.X) * ey) - ((p.Y - a.Y) * ex)) / el;
        }
    }
}
