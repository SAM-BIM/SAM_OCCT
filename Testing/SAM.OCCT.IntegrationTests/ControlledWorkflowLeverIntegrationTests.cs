// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end evidence that the two Grasshopper-facing controlled-workflow inputs actually move geometry on
    /// the real 9-space model, so a user can trust that turning a knob does something (docs/CONTROLLED_WORKFLOW_PLAN.md
    /// §5, "each input's effect must be visible"):
    /// <list type="bullet">
    /// <item><b>Clean3D bucket</b> changes the panel COUNT - a wider capture slab merges more near-coplanar
    /// skins (fewer output panels).</item>
    /// <item><b>Extend3D</b> changes the panel SIZE - walls grow to reach caps, so total area rises while the
    /// panel count is preserved (extend grows, it does not merge).</item>
    /// </list>
    /// The mechanism in isolation (vertical/horizontal/tilted squeeze, angled extend, the collapse gates) is in
    /// <c>SAM.OCCT.UnitTests.SqueezeAndAngledExtendTests</c>; this pins the same effects on the shipped fixture.
    /// </summary>
    public class ControlledWorkflowLeverIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private readonly ITestOutputHelper output;

        public ControlledWorkflowLeverIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels()
        {
            return SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
        }

        private static double TotalArea(IEnumerable<Panel> panels)
        {
            return panels.Sum(x => x.GetFace3D()?.GetArea() ?? 0.0);
        }

        [SkippableFact]
        public void Clean3D_WiderBucket_MergesMorePanels()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // A narrow capture slab leaves near-coplanar slab skins distinct; a wider one merges them.
            List<Panel> narrow = LoadPanels().Clean3D(out _, out _, minBucketSize: 0.1, bucketBetweenLevels: 0.21);
            List<Panel> wide = LoadPanels().Clean3D(out _, out _, minBucketSize: 0.4, bucketBetweenLevels: 0.21);

            output.WriteLine($"Clean3D minBucketSize=0.1 -> {narrow.Count} panels; minBucketSize=0.4 -> {wide.Count} panels");

            Assert.True(narrow.Count > wide.Count,
                $"A wider bucket must merge more panels (narrow={narrow.Count}, wide={wide.Count}).");
        }

        [SkippableFact]
        public void Extend3D_GrowsPanelSize_CountPreserved()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> cleaned = LoadPanels().Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);

            double cleanedArea = TotalArea(cleaned);
            double extendedArea = TotalArea(extended);
            output.WriteLine($"cleaned: {cleaned.Count} panels, {cleanedArea:0.0} m2 -> extended: {extended.Count} panels, {extendedArea:0.0} m2 (+{extendedArea - cleanedArea:0.0} m2)");

            // Extend grows walls to their caps: total area rises materially, and the panel count is unchanged
            // (extend enlarges panels, it does not merge them - that is the bucket's job).
            Assert.True(extendedArea > cleanedArea * 1.05,
                $"Extend3D must grow panel area (cleaned={cleanedArea:0.0}, extended={extendedArea:0.0}).");
            Assert.Equal(cleaned.Count, extended.Count);
        }
    }
}
