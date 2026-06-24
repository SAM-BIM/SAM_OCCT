// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Clean3D is the managed (native-free) Step 1 of the panel solver, so these run anywhere.
    /// They lock in the diagnostics added for the OCCT solver tooling: the clean panels carry the
    /// <see cref="SolverParameter.BucketSize"/>/<see cref="SolverParameter.Weight"/> stamps that
    /// SAMAnalytical.Visualize reads to draw the capture slab, and the shared Slits detection is wired.
    /// </summary>
    public class Clean3DDiagnosticsIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            if (objects == null)
            {
                return result;
            }

            foreach (IJSAMObject sAMObject in objects)
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;
                    case AdjacencyCluster adjacencyCluster:
                        result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;
                    case Panel panel:
                        result.Add(panel);
                        break;
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
        }

        [SkippableFact]
        public void Clean3D_HomeFixture_StampsBucketSizeAndWeight_AndDetectsSlits()
        {
            string path = Path.Combine(FixturesDirectory, "AdjacencyCluster-home.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path)
                .Where(x => { Face3D face3D = x.GetFace3D(); return face3D != null && face3D.IsValid(); })
                .ToList();
            Assert.NotEmpty(panels);

            // GH component path: no caller weights, so Clean3D derives length-based defaults internally.
            List<Panel> clean = panels.Clean3D(out List<string> diagnostics, weights: null, minBucketSize: 0.4, thicknessFactor: 0.6);

            Assert.NotNull(clean);
            Assert.NotEmpty(clean);
            Assert.Contains(diagnostics, d => d.Contains("SAM_OCCT_CLEAN3D_RESULT"));

            // Every clean panel carries the bucket/weight stamps SAMAnalytical.Visualize draws from.
            foreach (Panel panel in clean)
            {
                Assert.True(panel.TryGetValue(SolverParameter.BucketSize, out double bucketSize), "Clean panel is missing the BucketSize stamp");
                Assert.True(bucketSize >= 0.4, "Bucket size should be at least the minimum capture half-width");
                Assert.True(panel.TryGetValue(SolverParameter.Weight, out double _), "Clean panel is missing the Weight stamp");
            }

            // The shared slit detector is wired and returns a consistent (non-null) result.
            List<Segment3D> slits = clean.Slits(out List<Panel> slitPanels, null, 0.2, 0.02, 0.5, 2.0);
            Assert.NotNull(slits);
            Assert.NotNull(slitPanels);
            Assert.All(slits, x => Assert.NotNull(x));
        }
    }
}
