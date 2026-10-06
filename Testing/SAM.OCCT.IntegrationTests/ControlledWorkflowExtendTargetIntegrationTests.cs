// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P3 fixture coverage (docs/CONTROLLED_WORKFLOW_PLAN.md §5.2-§5.3) on the 9-space controlled fixture,
    /// through the exact chained handoff (<c>Clean3D(bucketBetweenLevels: 0.21)</c> -&gt;
    /// <c>Extend3D(inputAlreadyClean: true, bucketBetweenLevels: 0.21)</c>). Clean3D/Extend3D are the MANAGED
    /// (native-free) pre-resolve passes, so these run everywhere (no native gate).
    /// </summary>
    public class ControlledWorkflowExtendTargetIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        /// <summary>The three group datums the fixture's 5 raw frames merge onto at band 0.21 (plan §1) - wall
        /// tops/bottoms must land on exactly these, never the un-merged raw datums 12.436/15.473.</summary>
        private static readonly double[] GroupDatums = new double[] { 12.24, 15.29, 18.34 };

        private static readonly double[] UnmergedRawDatums = new double[] { 12.436, 15.473 };

        /// <summary>West3's plan location (plan §0.1) - the double-height space's XY, used to test whether any
        /// cap face's plan footprint newly covers it at the intermediate datum (the D4 false-floor signature).</summary>
        private static readonly Point3D West3PlanPoint = new Point3D(-4.14, -6.56, 0);

        private const double VerticalNormalZ = 0.342; // sin(20 deg) - a wall's |normal.Z| stays below this

        private readonly ITestOutputHelper output;

        public ControlledWorkflowExtendTargetIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> FixturePanels()
        {
            string panelsPath = Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam");
            Assert.True(File.Exists(panelsPath), "Missing fixture: " + panelsPath);
            List<Panel> panels = SAM.Core.Convert.ToSAM(panelsPath).OfType<Panel>().ToList();
            Assert.NotEmpty(panels);
            return panels;
        }

        private static List<Panel> ChainedExtend(bool directionalCapGrow)
        {
            List<Panel> panels = FixturePanels();
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            Assert.NotNull(cleaned);
            Assert.NotEmpty(cleaned);

            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: directionalCapGrow);
            Assert.NotNull(extended);
            Assert.NotEmpty(extended);
            return extended;
        }

        private static bool IsWall(Panel panel)
        {
            Face3D face3D = panel?.GetFace3D();
            Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
            return normal != null && System.Math.Abs(normal.Z) <= VerticalNormalZ;
        }

        [Fact]
        public void ExtendTargets_FixtureChainedRun_CapsNormalizeToGroupDatumsAndWallsReachThem()
        {
            // Act - the exact controlled-fixture chain (plan §9): Clean3D(0.21) -> Extend3D(inputAlreadyClean: true, 0.21).
            List<Panel> extended = ChainedExtend(directionalCapGrow: false);

            // D3 fix, the merged-plane targeting guarantee (plan §5.2): every floor/roof CAP normalizes onto one
            // of the three group datums 12.24/15.29/18.34 and NONE stays on an un-merged raw slab-skin datum
            // (12.436/15.473). The caps are what wall-to-cap extension targets, so proving the caps are merged
            // proves walls can only extend to a group datum, never to a raw skin.
            //
            // (Walls are deliberately NOT asserted against the raw datums here: a wall whose ORIGINAL modelled top
            // sat at a raw skin - e.g. North0's 15.473 roof, now normalized to the 15.29 cap - legitimately
            // OVERSHOOTS its lowered cap in this pre-resolve view, because Extend3D only grows walls and never
            // shortens them; the native MakerVolume split in Solve3D trims that overshoot back to the cap. So the
            // guarantee lives on the caps, not on overshooting wall tops.)
            List<Panel> caps = extended.Where(x => !IsWall(x)).ToList();
            Assert.NotEmpty(caps);

            HashSet<double> datumsReached = new HashSet<double>();
            foreach (Panel cap in caps)
            {
                BoundingBox3D box = cap.GetFace3D()?.GetBoundingBox();
                if (box == null)
                {
                    continue;
                }

                double elevation = 0.5 * (box.Min.Z + box.Max.Z);

                foreach (double raw in UnmergedRawDatums)
                {
                    Assert.True(System.Math.Abs(elevation - raw) > 0.02,
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "Cap elevation {0:0.###} stayed on the un-merged raw datum {1:0.###} - cap normalization did not merge it onto a group datum (D3).", elevation, raw));
                }

                double nearestDatum = GroupDatums.OrderBy(d => System.Math.Abs(d - elevation)).First();
                Assert.True(System.Math.Abs(nearestDatum - elevation) <= 0.02,
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Cap elevation {0:0.###} is not on any group datum 12.24/15.29/18.34.", elevation));
                datumsReached.Add(nearestDatum);
            }

            // All three group datums are actually present as cap planes (the fixture's 5 raw frames -> 3 groups).
            output.WriteLine("Group datums reached by caps: {0}", string.Join(", ", datumsReached.OrderBy(x => x)));
            Assert.Equal(3, datumsReached.Count);

            // ...and walls do reach the merged planes: some wall top/bottom lands on a group datum within the
            // deliberate wall-extend overshoot (0.05 m past the cap so the native split trims cleanly), e.g. a
            // wall extended DOWN to the 12.24 floor cap sits at 12.19. The overshoot is why this is not an exact
            // match - the target plane is the group datum, the wall stops just past it.
            const double extendOvershootTolerance = 0.06;
            int wallExtremesOnGroupDatum = extended.Where(IsWall)
                .Select(w => w.GetFace3D()?.GetBoundingBox())
                .Where(b => b != null)
                .SelectMany(b => new[] { b.Min.Z, b.Max.Z })
                .Count(z => GroupDatums.Any(d => System.Math.Abs(z - d) <= extendOvershootTolerance));
            Assert.True(wallExtremesOnGroupDatum > 0, "No wall extreme reached any group datum (within the extend overshoot) - the wall-to-cap extension evidence is missing.");
        }

        [Fact]
        public void InputEffect_ChainedRunInputAlreadyClean_CleanStageInputsInertButRunSaysSo()
        {
            // The user-reported symptom: on the chained handoff (inputAlreadyClean=true) Stage A is skipped, so
            // changing the clean-stage inputs (minBucketSize/thicknessFactor/alignColinearOffset/
            // normalizeCapOffset) and bucketBetweenLevels produces IDENTICAL geometry - they had no effect. That
            // is by design (P2 §2: no second clean), but it must be VISIBLE, not silent (plan §5 "generic
            // solution - each input's effect must be visible"). This pins both halves: the inertness is real,
            // and the run reports it.
            List<Panel> panels = FixturePanels();
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            Assert.NotNull(cleaned);

            List<Panel> baseline = cleaned.Extend3D(out List<string> baselineDiagnostics, out _,
                minBucketSize: 0.4, thicknessFactor: 0.6, fillMargin: 0.5,
                alignColinearOffset: 0.3, normalizeCapOffset: 0.3,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: false);

            // Same chain, WILDLY different clean-stage inputs (all skipped on the inputAlreadyClean path).
            List<Panel> cleanStageVaried = cleaned.Extend3D(out _, out _,
                minBucketSize: 1.5, thicknessFactor: 2.0, fillMargin: 0.5,
                alignColinearOffset: 0.9, normalizeCapOffset: 0.9,
                bucketBetweenLevels: 0.05, inputAlreadyClean: true, directionalCapGrow: false);

            string baselineSignature = GeometrySignature(baseline);
            string variedSignature = GeometrySignature(cleanStageVaried);
            output.WriteLine("baseline (clean-stage defaults):   {0}", baselineSignature);
            output.WriteLine("clean-stage inputs varied wildly:  {0}", variedSignature);

            // Inert: identical geometry despite the wildly different clean-stage inputs.
            Assert.Equal(baselineSignature, variedSignature);

            // ...and the run says so, honestly (never a silent no-op).
            Assert.Contains(baselineDiagnostics, d => d.StartsWith("SAM_OCCT_EXTEND3D_INPUT_INERT:"));

            // Positive control: fillMargin is a LIVE knob on this path - growing caps further changes geometry.
            List<Panel> fillVaried = cleaned.Extend3D(out _, out _,
                minBucketSize: 0.4, thicknessFactor: 0.6, fillMargin: 3.0,
                alignColinearOffset: 0.3, normalizeCapOffset: 0.3,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: false);
            string fillSignature = GeometrySignature(fillVaried);
            output.WriteLine("fillMargin 0.5 -> 3.0 (live knob):  {0}", fillSignature);
            Assert.NotEqual(baselineSignature, fillSignature);
        }

        /// <summary>A stable, order-independent geometry fingerprint: panel count plus the summed face area and
        /// summed bounding-box extents (rounded), so any cap grow / wall move that actually changed the geometry
        /// changes the string, and a run that changed nothing leaves it identical.</summary>
        private static string GeometrySignature(IEnumerable<Panel> panels)
        {
            int count = 0;
            double area = 0, extent = 0;
            foreach (Panel panel in panels ?? Enumerable.Empty<Panel>())
            {
                Face3D face3D = panel?.GetFace3D();
                if (face3D == null)
                {
                    continue;
                }

                count++;
                area += face3D.GetArea();
                BoundingBox3D box = face3D.GetBoundingBox();
                if (box != null)
                {
                    extent += (box.Max.X - box.Min.X) + (box.Max.Y - box.Min.Y) + (box.Max.Z - box.Min.Z);
                }
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "count={0} area={1:0.000000} extent={2:0.000000}", count, area, extent);
        }

        [Fact]
        public void ExtendTargets_FixtureChainedRunDirectionalCapGrowOn_NoFalseFloorCoversDoubleHeightColumn()
        {
            // Act - the acceptance chain per plan §9: 0.21 / inputAlreadyClean=true / directionalCapGrow=true.
            List<Panel> extended = ChainedExtend(directionalCapGrow: true);

            List<Panel> falseFloors = FindCapsAtIntermediateDatumOverPoint(extended, 15.29, West3PlanPoint);
            foreach (Panel panel in falseFloors)
            {
                output.WriteLine("SUSPECT FALSE FLOOR: panel {0} box={1}", panel.Guid, panel.GetFace3D()?.GetBoundingBox());
            }

            Assert.Empty(falseFloors);
        }

        /// <summary>The (near-horizontal, cap-like) panels among <paramref name="panels"/> whose elevation sits
        /// within 0.21 m of <paramref name="intermediateDatum"/> AND whose plan bounding box covers
        /// <paramref name="planPoint"/> - the D4 false-floor signature (a mid-level cap pushed into a
        /// double-height void).</summary>
        private static List<Panel> FindCapsAtIntermediateDatumOverPoint(IEnumerable<Panel> panels, double intermediateDatum, Point3D planPoint)
        {
            List<Panel> result = new List<Panel>();
            foreach (Panel panel in panels ?? Enumerable.Empty<Panel>())
            {
                Face3D face3D = panel?.GetFace3D();
                BoundingBox3D box = face3D?.GetBoundingBox();
                Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
                if (box == null || normal == null || System.Math.Abs(normal.Z) <= VerticalNormalZ)
                {
                    continue; // not cap-like (a wall, or degenerate)
                }

                double elevation = 0.5 * (box.Min.Z + box.Max.Z);
                if (System.Math.Abs(elevation - intermediateDatum) > 0.21)
                {
                    continue;
                }

                if (planPoint.X < box.Min.X || planPoint.X > box.Max.X || planPoint.Y < box.Min.Y || planPoint.Y > box.Max.Y)
                {
                    continue;
                }

                result.Add(panel);
            }

            return result;
        }
    }
}
