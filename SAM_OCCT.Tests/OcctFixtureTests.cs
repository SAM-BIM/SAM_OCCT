// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;
using OcctCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.Tests
{
    public class OcctFixtureTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public OcctFixtureTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Coded_box_builds_one_occt_cell()
        {
            List<Face3D> face3Ds = BoxFace3Ds();

            OcctCellComplexResult result;
            List<Shell> shells = OcctCreate.Shells(face3Ds, out result, DefaultOptions());

            WriteDiagnostics("coded box", face3Ds, result);

            Assert.NotNull(result);
            Assert.True(result.Success, FailureMessage("coded box", result));
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Single(result.Cells);
        }

        [Fact]
        public void Uploaded_sam_fixtures_build_occt_cells()
        {
            List<string> paths = Directory.Exists(FixturesDirectory)
                ? Directory.GetFiles(FixturesDirectory, "*.sam", SearchOption.TopDirectoryOnly).OrderBy(x => x).ToList()
                : new List<string>();

            if (paths.Count == 0)
            {
                output.WriteLine("No .sam fixtures found in {0}. Add exported SAM geometry there to exercise real examples.", FixturesDirectory);
                return;
            }

            foreach (string path in paths)
            {
                string fixtureName = Path.GetFileName(path);
                List<Face3D> face3Ds = LoadFace3Ds(path);

                Assert.True(face3Ds.Count != 0, string.Format("{0} did not contain extractable Face3D geometry.", fixtureName));

                OcctCellComplexResult result;
                List<Shell> shells = OcctCreate.Shells(face3Ds, out result, DefaultOptions());

                WriteDiagnostics(fixtureName, face3Ds, result);

                Assert.NotNull(result);
                Assert.True(result.Success, FailureMessage(fixtureName, result));
                Assert.NotNull(shells);
                Assert.NotEmpty(shells);
                Assert.Equal(result.Cells.Count, shells.Count);
            }
        }

        private static OcctBuildOptions DefaultOptions()
        {
            return new OcctBuildOptions
            {
                Tolerance = Tolerance.Distance,
                FuzzyTolerance = Tolerance.MacroDistance,
                RunParallel = true,
                AvoidInternalShapes = true
            };
        }

        private static List<Face3D> LoadFace3Ds(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            Assert.NotNull(objects);

            List<Face3D> result = new List<Face3D>();
            foreach (IJSAMObject sAMObject in objects)
            {
                AddFace3Ds(sAMObject, result);
            }

            return result.Where(x => x != null).ToList();
        }

        private static void AddFace3Ds(IJSAMObject sAMObject, List<Face3D> face3Ds)
        {
            if (sAMObject == null || face3Ds == null)
            {
                return;
            }

            if (sAMObject is AnalyticalModel analyticalModel)
            {
                int count = face3Ds.Count;
                AddPanels(analyticalModel.GetPanels(), face3Ds);
                if (face3Ds.Count == count)
                {
                    AddShells(analyticalModel.GetShells(), face3Ds);
                }

                return;
            }

            if (sAMObject is AdjacencyCluster adjacencyCluster)
            {
                int count = face3Ds.Count;
                AddPanels(adjacencyCluster.GetPanels(), face3Ds);
                if (face3Ds.Count == count)
                {
                    AddShells(adjacencyCluster.GetShells(), face3Ds);
                }

                return;
            }

            if (sAMObject is Panel panel)
            {
                AddFace3D(panel.GetFace3D(), face3Ds);
                return;
            }

            if (sAMObject is Face3D face3D)
            {
                AddFace3D(face3D, face3Ds);
                return;
            }

            if (sAMObject is Shell shell)
            {
                AddShell(shell, face3Ds);
                return;
            }

            if (sAMObject is IEnumerable<ISAMGeometry3DObject> geometry3DObjects)
            {
                foreach (ISAMGeometry3DObject geometry3DObject in geometry3DObjects)
                {
                    AddFace3Ds(geometry3DObject, face3Ds);
                }

                return;
            }

            if (sAMObject is ISAMGeometry3DObject geometry3DObjectSingle)
            {
                AddGeometry3D(SAM.Geometry.Object.Spatial.Query.ISAMGeometry3D(geometry3DObjectSingle), face3Ds);
                return;
            }

            if (sAMObject is ISAMGeometry3D geometry3D)
            {
                AddGeometry3D(geometry3D, face3Ds);
            }
        }

        private static void AddPanels(IEnumerable<Panel> panels, List<Face3D> face3Ds)
        {
            if (panels == null)
            {
                return;
            }

            foreach (Panel panel in panels)
            {
                AddFace3D(panel?.GetFace3D(), face3Ds);
            }
        }

        private static void AddShells(IEnumerable<Shell> shells, List<Face3D> face3Ds)
        {
            if (shells == null)
            {
                return;
            }

            foreach (Shell shell in shells)
            {
                AddShell(shell, face3Ds);
            }
        }

        private static void AddShell(Shell shell, List<Face3D> face3Ds)
        {
            if (shell?.Face3Ds == null)
            {
                return;
            }

            foreach (Face3D face3D in shell.Face3Ds)
            {
                AddFace3D(face3D, face3Ds);
            }
        }

        private static void AddGeometry3D(ISAMGeometry3D geometry3D, List<Face3D> face3Ds)
        {
            if (geometry3D == null)
            {
                return;
            }

            List<Face3D> extracted = new List<ISAMGeometry3D> { geometry3D }.Face3Ds();
            if (extracted == null)
            {
                return;
            }

            foreach (Face3D face3D in extracted)
            {
                AddFace3D(face3D, face3Ds);
            }
        }

        private static void AddFace3D(Face3D face3D, List<Face3D> face3Ds)
        {
            if (face3D != null)
            {
                face3Ds.Add(new Face3D(face3D));
            }
        }

        private static List<Face3D> BoxFace3Ds()
        {
            Point3D p000 = new Point3D(0, 0, 0);
            Point3D p100 = new Point3D(1, 0, 0);
            Point3D p110 = new Point3D(1, 1, 0);
            Point3D p010 = new Point3D(0, 1, 0);
            Point3D p001 = new Point3D(0, 0, 1);
            Point3D p101 = new Point3D(1, 0, 1);
            Point3D p111 = new Point3D(1, 1, 1);
            Point3D p011 = new Point3D(0, 1, 1);

            return new List<Face3D>
            {
                Face(p000, p010, p110, p100),
                Face(p001, p101, p111, p011),
                Face(p000, p100, p101, p001),
                Face(p100, p110, p111, p101),
                Face(p110, p010, p011, p111),
                Face(p010, p000, p001, p011)
            };
        }

        private static Face3D Face(params Point3D[] points)
        {
            return new Polygon3D(points).ToFace3D();
        }

        private void WriteDiagnostics(string name, IReadOnlyCollection<Face3D> face3Ds, OcctCellComplexResult result)
        {
            output.WriteLine("{0}: {1} input face(s), {2} decoded cell(s), {3} shared face relation(s).",
                name,
                face3Ds?.Count ?? 0,
                result?.Cells?.Count ?? 0,
                result?.FaceAdjacencies?.Count ?? 0);

            output.WriteLine(Diagnostics(result));
        }

        private static string FailureMessage(string name, OcctCellComplexResult result)
        {
            return string.Format("{0} failed OCCT build.{1}{2}", name, Environment.NewLine, Diagnostics(result));
        }

        private static string Diagnostics(OcctCellComplexResult result)
        {
            if (result?.Diagnostics == null || result.Diagnostics.Count == 0)
            {
                return "(no OCCT diagnostics)";
            }

            return string.Join(Environment.NewLine, result.Diagnostics.Select(x => x.ToString()));
        }
    }
}
