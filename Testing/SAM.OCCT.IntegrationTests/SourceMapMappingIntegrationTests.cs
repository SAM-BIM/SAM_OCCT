// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 2 acceptance: every solved output face carries at least one source (or a fabricated provenance
    /// tag) - no mapping orphans (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 2). Exercises the
    /// coarse managed-only <see cref="SourceMap"/> the solver now builds over its output on both the raw-first
    /// and forced-managed paths. Native-gated (the resolve is native), but the invariant asserted is purely
    /// the managed mapping.
    /// </summary>
    public class SourceMapMappingIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Face3D> LoadFace3Ds(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> panels = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        panels.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;
                    case AdjacencyCluster adjacencyCluster:
                        panels.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;
                    case Panel panel:
                        panels.Add(panel);
                        break;
                }
            }

            return panels
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .Select(x => x.GetFace3D())
                .ToList();
        }

        [SkippableTheory]
        [InlineData(false)] // raw-first path
        [InlineData(true)]  // forced-managed path
        public void Execute_WholeLevelFlat_EveryOutputFaceHasASourceOrFabricatedTag(bool forceManaged)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Face3D> face3Ds = LoadFace3Ds(path);
            Assert.NotEmpty(face3Ds);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds) { ForceManagedPipeline = forceManaged };
            solver.Execute(new OcctBuildOptions());

            Assert.True(solver.NativeResolved, "Expected the native resolve to run");
            Assert.NotEmpty(solver.ResolvedFace3Ds);

            // Every output face is addressed in the SourceMap - either attributed to >= 1 input source or,
            // failing that, explicitly tagged fabricated (never silently orphaned).
            for (int k = 0; k < solver.ResolvedFace3Ds.Count; k++)
            {
                FaceKey key = new FaceKey(k);
                bool addressed = solver.SourceMap.SourcesOf(key).Count > 0;
                Assert.True(addressed, $"Output face {k} has no SourceMap entry (mapping orphan)");
            }
        }
    }
}
