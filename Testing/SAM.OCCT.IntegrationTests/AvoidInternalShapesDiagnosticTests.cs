// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    public class AvoidInternalShapesDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public AvoidInternalShapesDiagnosticTests(ITestOutputHelper output) { this.output = output; }

        [SkippableFact]
        public void Towers_Extend3D_CreateAdjacencyCluster_AvoidInternalShapesComparison()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = new List<Panel>();
            foreach (var obj in SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "whole-level-towers.sam")) ?? new List<IJSAMObject>())
            {
                if (obj is AnalyticalModel am) panels.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (obj is AdjacencyCluster ac) panels.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (obj is Panel p) panels.Add(p);
            }
            Assert.NotEmpty(panels);

            // Shared params for Extend3D
            List<Panel> extended = panels.Extend3D(out _, out _,
                bucketBetweenLevels: 0.4, fillMargin: 0.4, directionalCapGrow: false);
            var nonAir = extended.Where(x => x?.GetFace3D() != null).ToList();

            output.WriteLine("Extended: {0} panels", nonAir.Count);

            // Test both modes
            foreach (bool avoid in new[] { false, true })
            {
                var opts = new OcctBuildOptions
                {
                    AvoidInternalShapes = avoid,
                    SewBeforeBuild = true,
                    SewingTolerance = 0.01,
                };

                var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                    null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

                int spaces = cluster?.GetSpaces()?.Count ?? 0;
                int panelsC = cluster?.GetPanels()?.Count ?? 0;

                // Count shared walls: panels that bound 2+ spaces
                int shared = 0, single = 0, zero = 0;
                int totalRelations = 0;
                foreach (var space in cluster?.GetSpaces() ?? new List<Space>())
                {
                    foreach (var panel in cluster?.GetPanels(space) ?? new List<Panel>())
                    {
                        totalRelations++;
                    }
                }
                foreach (var panel in cluster?.GetPanels() ?? new List<Panel>())
                {
                    var boundSpaces = cluster?.GetSpaces(panel)?.Count ?? 0;
                    if (boundSpaces >= 2) shared++;
                    else if (boundSpaces == 1) single++;
                    else zero++;
                }

                int faceAdjacencies = cr?.FaceAdjacencies?.Count ?? 0;
                var parityDiag = cr?.Diagnostics?.FirstOrDefault(d => d.Code == "SAM_OCCT_ANALYTICAL_PARITY");

                output.WriteLine("AvoidInternalShapes={0,-5} → spaces={1,3}  panels={2,4}  shared={3,3}  single={4,3}  zero={5,3}  adjacencies={6,3}  parity={7}",
                    avoid, spaces, panelsC, shared, single, zero, faceAdjacencies,
                    parityDiag?.Severity == OcctDiagnosticSeverity.Info ? "clean" : "WARN");

                cr?.Dispose();
            }
        }
    }
}
