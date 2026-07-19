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
    public class TowersDiscoveryPathDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public TowersDiscoveryPathDiagnosticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string relativePath)
        {
            string path = Path.Combine(FixturesDirectory, relativePath);
            List<Panel> result = new List<Panel>();
            foreach (var obj in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (obj is AnalyticalModel am) result.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (obj is AdjacencyCluster ac) result.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (obj is Panel p) result.Add(p);
            }
            return result;
        }

        [SkippableFact]
        public void Towers_CompareDiscoveryPathVsGHWorkflow()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = LoadPanels("whole-level-towers.sam");
            Assert.NotEmpty(panels);
            List<Face3D> faces = panels.Select(p => p.GetFace3D()).Where(f => f != null).ToList();

            // --- Path A: Discovery solver (StopAfterExtend + Create.Shells) ---
            output.WriteLine("=== PATH A: Discovery solver (StopAfterExtend + Create.Shells) ===");
            for (double fm = 0.3; fm <= 0.5; fm += 0.1)
            {
                var solver = new Panel3DSnapSolver(
                    faces,
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, 0.4),
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, Panel3DSnapSolver.DEFAULT_Weight),
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, Panel3DSnapSolver.DEFAULT_MaxExtension))
                {
                    StopAfterExtend = true,
                    FillMargin = fm,
                    DirectionalCapGrow = true,
                    BucketBetweenLevels = 0.5,
                    ForceManagedPipeline = true,
                };
                solver.Execute(null);

                var buildOpts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                SAM.Geometry.OCCT.Create.Shells(solver.ResolvedFace3Ds, out OcctCellComplexResult cellResult, buildOpts);
                int cellsA = cellResult?.Cells?.Count ?? 0;
                cellResult?.Dispose();
                output.WriteLine("  fill={0:0.#} → {1} cells (StopAfterExtend + Create.Shells)", fm, cellsA);
            }

            // --- Path B: Extend3D panels → CreateAdjacencyCluster (actual GH workflow) ---
            output.WriteLine("");
            output.WriteLine("=== PATH B: Extend3D(panels) → CreateAdjacencyCluster (GH workflow) ===");
            for (double fm = 0.3; fm <= 0.5; fm += 0.1)
            {
                List<Panel> extended = panels.Extend3D(out _, out _,
                    bucketBetweenLevels: 0.5, fillMargin: fm, directionalCapGrow: true);

                var nonAir = extended.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null).ToList();

                output.WriteLine("  Extended: {0} total, {1} non-air panels", extended?.Count ?? 0, nonAir.Count);

                // Count distinct Face3Ds (checking if BuildPanels drops/filters any)
                int faceCount = nonAir.Select(x => x.GetFace3D()).Where(f => f != null).Distinct().Count();
                output.WriteLine("  Unique faces: {0}", faceCount);

                var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                    null, nonAir, out OcctCellComplexResult clusterResult, new SAM.Core.Log(),
                    new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });
                int spaces = cluster?.GetSpaces()?.Count ?? 0;
                int cellsB = clusterResult?.Cells?.Count ?? 0;
                clusterResult?.Dispose();
                output.WriteLine("  fill={0:0.#} → {1} spaces, {2} cells (Extend3D → CreateAdjacencyCluster)", fm, spaces, cellsB);
            }

            // --- Path D: Direct solver face count vs Extend3D face count ---
            output.WriteLine("");
            output.WriteLine("=== PATH D: Compare solver.ResolvedFace3Ds vs Extend3D output faces ===");
            for (double fm = 0.3; fm <= 0.5; fm += 0.1)
            {
                // Direct solver
                var solver = new Panel3DSnapSolver(
                    faces,
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, 0.4),
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, Panel3DSnapSolver.DEFAULT_Weight),
                    Panel3DSnapSolver.AdjustListLength(null, faces.Count, Panel3DSnapSolver.DEFAULT_MaxExtension))
                {
                    StopAfterExtend = true,
                    FillMargin = fm,
                    DirectionalCapGrow = true,
                    BucketBetweenLevels = 0.5,
                    ForceManagedPipeline = true,
                };
                solver.Execute(null);
                int solverFaceCount = solver.ResolvedFace3Ds?.Count ?? 0;
                double solverArea = solver.ResolvedFace3Ds?.Sum(f => f?.GetArea() ?? 0) ?? 0;

                // Extend3D
                List<Panel> extended = panels.Extend3D(out _, out _,
                    bucketBetweenLevels: 0.5, fillMargin: fm, directionalCapGrow: true);
                var extendFaces = extended.Where(x => x != null && x.GetFace3D() != null)
                    .Select(x => x.GetFace3D()).Distinct().ToList();
                int extendFaceCount = extendFaces.Count;
                double extendArea = extendFaces.Sum(f => f?.GetArea() ?? 0);

                // Also check the INTERNAL solver's faces (from the report)
                output.WriteLine("  fill={0:0.#}: solver faces={1} area={2:0.#}  Extend3D faces={3} area={4:0.#}  MATCH={5}",
                    fm, solverFaceCount, solverArea, extendFaceCount, extendArea,
                    solverFaceCount == extendFaceCount ? "YES" : "NO (diff=" + (solverFaceCount - extendFaceCount) + ")");
            }
            output.WriteLine("");
            output.WriteLine("=== PATH C: Extend3D output faces → Create.Shells directly ===");
            for (double fm = 0.3; fm <= 0.5; fm += 0.1)
            {
                List<Panel> extended = panels.Extend3D(out _, out _,
                    bucketBetweenLevels: 0.5, fillMargin: fm, directionalCapGrow: true);
                var extendFaces = extended.Where(x => x != null && x.GetFace3D() != null)
                    .Select(x => x.GetFace3D()).ToList();

                var buildOpts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                SAM.Geometry.OCCT.Create.Shells(extendFaces, out OcctCellComplexResult cellResult, buildOpts);
                int cellsC = cellResult?.Cells?.Count ?? 0;
                cellResult?.Dispose();
                output.WriteLine("  fill={0:0.#} → {1} cells (Extend3D faces → Create.Shells)", fm, cellsC);
            }
        }
    }
}
