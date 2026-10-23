// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.Grasshopper.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// SAMOCCT.CreateShells with <c>sewBeforeBuild_</c> builds through the sew guard (SAM_OCCT#81), as
    /// SAMOCCT.CreateAdjacencyCluster does. Without it the forced-managed towers outputs lost the west room at
    /// bucketBetweenLevels 0 (20 instead of 24 cells) and five cells at 0.21 (22 instead of 27), reported as success
    /// (record: documentation/Towers-Residual-Closure-Q4.md).
    /// <para>
    /// The real component runs its own SolveInstance on Grasshopper's own data access; only what needs a running Rhino
    /// is replaced: inputs get their values as volatile data (what CollectData produces for typed-in values, minus its
    /// Rhino-referenced-geometry step), and output writes are recorded as the component passed them (Grasshopper's
    /// output cast resolves geometry through Rhino). Voluntary inputs are inserted from the component's own declaration.
    /// The reference is the library build with the component's own options (every voluntary input at its default)
    /// plus the guard, so the comparison is option for option.
    /// </para>
    /// </summary>
    public class CreateShellsSewGuardComponentTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private static readonly Point3D WestRoom = new Point3D(14.0, -5.0, 13.8);
        private static readonly Point3D MiddleRoom = new Point3D(19.5, -5.0, 13.8);
        private static readonly Dictionary<double, List<Face3D>> towersFaces = new Dictionary<double, List<Face3D>>();
        private static readonly string[] DeclaredInputNames = { "_face3Ds", "tolerance_", "fuzzyTolerance_", "sewBeforeBuild_", "sewingTolerance_", "glueMode_", "_run" };

        private readonly ITestOutputHelper output;

        public CreateShellsSewGuardComponentTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Inputs_DeclaredNamesAccessVisibilityAndDefaults_Unchanged()
        {
            // Arrange
            GH_SAMParam[] inputs = DeclaredInputs(new SAMOCCTCreateShells());

            // Act
            List<IGH_Goo> defaults = new List<IGH_Goo>
            {
                ((GH_PersistentParam<GH_Number>)inputs[1].Param).PersistentData.AllData(true).Single(),
                ((GH_PersistentParam<GH_Number>)inputs[2].Param).PersistentData.AllData(true).Single(),
                ((GH_PersistentParam<GH_Boolean>)inputs[3].Param).PersistentData.AllData(true).Single(),
                ((GH_PersistentParam<GH_Number>)inputs[4].Param).PersistentData.AllData(true).Single(),
                ((GH_PersistentParam<GH_Integer>)inputs[5].Param).PersistentData.AllData(true).Single(),
                ((GH_PersistentParam<GH_Boolean>)inputs[6].Param).PersistentData.AllData(true).Single(),
            };

            // Assert: the guard adds no input and changes no default; sewing stays off unless the user turns it on.
            Assert.Equal(DeclaredInputNames, inputs.Select(x => x.Param.Name).ToArray());
            Assert.Equal(new[] { GH_ParamAccess.list, GH_ParamAccess.item, GH_ParamAccess.item, GH_ParamAccess.item, GH_ParamAccess.item, GH_ParamAccess.item, GH_ParamAccess.item }, inputs.Select(x => x.Param.Access).ToArray());
            Assert.Equal(new[] { ParamVisibility.Binding, ParamVisibility.Voluntary, ParamVisibility.Voluntary, ParamVisibility.Voluntary, ParamVisibility.Voluntary, ParamVisibility.Voluntary, ParamVisibility.Binding }, inputs.Select(x => x.ParamVisibility).ToArray());
            Assert.Equal(Tolerance.Distance, ((GH_Number)defaults[0]).Value);
            Assert.Equal(Tolerance.MacroDistance, ((GH_Number)defaults[1]).Value);
            Assert.False(((GH_Boolean)defaults[2]).Value);
            Assert.Equal(0.0, ((GH_Number)defaults[3]).Value);
            Assert.Equal(0, ((GH_Integer)defaults[4]).Value);
            Assert.False(((GH_Boolean)defaults[5]).Value);
        }

        [SkippableTheory]
        [InlineData(0.0, 24, 159.479, 113.717)]
        [InlineData(0.21, 27, 174.145, 119.364)]
        public void Run_SewBeforeBuildOnForcedManagedTowers_KeepsWestAndMiddleRoomsAsGuardedReference(double bucketBetweenLevels, int expectedShellCount, double westVolume, double middleVolume)
        {
            Skip.IfNot(NativeRuntime.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Face3D> face3Ds = TowersFaces(bucketBetweenLevels);
            List<OcctCell> reference = Build(face3Ds, ComponentOptions(sewBeforeBuild: true, validateSewCoverage: true), out bool referenceSuccess);

            // Act
            ComponentRun run = Run(face3Ds, sewBeforeBuild: true);
            Log(run, bucketBetweenLevels);

            // Assert: the two rooms first, so a lost room is reported by name.
            int west = AssertSingleShell(run.Shells, WestRoom, "west");
            int middle = AssertSingleShell(run.Shells, MiddleRoom, "middle");
            Assert.NotEqual(west, middle);
            Assert.InRange(ReferenceVolume(run.Shells[west], reference, "west"), westVolume - 0.05, westVolume + 0.05);
            Assert.InRange(ReferenceVolume(run.Shells[middle], reference, "middle"), middleVolume - 0.05, middleVolume + 0.05);

            // The whole result is the guarded reference's, boundary for boundary, and its cells are valid.
            Assert.True(referenceSuccess);
            Assert.Equal(expectedShellCount, reference.Count);
            Assert.Equal(expectedShellCount, run.Shells.Count);
            Assert.Equal(Keys(reference.Select(x => x.Shell)), Keys(run.Shells));
            Assert.Empty(ValidateCells(run.Shells.Select(x => new OcctCell(x, VolumeOf(x, reference))).ToList()));

            // Success, and the guard's decision is reported rather than silent.
            Assert.True(run.Successful);
            Assert.Empty(run.Errors);
            Assert.Contains(run.Warnings, x => x.Contains("SAM_OCCT_BUILD_FALLBACK_DIRECT"));
        }

        [SkippableTheory]
        [InlineData(null)]
        [InlineData(false)]
        public void Run_SewingOffOnForcedManagedTowers_MatchesTheDirectBuild(bool? sewBeforeBuild)
        {
            Skip.IfNot(NativeRuntime.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: null leaves the voluntary input off the component, as a newly placed component has it.
            List<Face3D> face3Ds = TowersFaces(0.0);
            List<OcctCell> reference = Build(face3Ds, ComponentOptions(sewBeforeBuild: false, validateSewCoverage: false), out bool referenceSuccess);

            // Act
            ComponentRun run = Run(face3Ds, sewBeforeBuild);
            Log(run, 0.0);

            // Assert
            Assert.True(referenceSuccess);
            Assert.True(run.Successful);
            Assert.Empty(run.Errors);
            Assert.Empty(run.Warnings);
            Assert.Equal(24, run.Shells.Count);
            Assert.Equal(Keys(reference.Select(x => x.Shell)), Keys(run.Shells));
            Assert.DoesNotContain(run.Diagnostics, x => x.Contains("SAM_OCCT_SEW"));
        }

        [SkippableFact]
        public void Run_SewBeforeBuildOnTwoBoxesSharingAWall_KeepsTheValidSewBuild()
        {
            Skip.IfNot(NativeRuntime.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Face3D> face3Ds = TwoBoxes(includeTop: true);
            List<OcctCell> unguarded = Build(face3Ds, ComponentOptions(sewBeforeBuild: true, validateSewCoverage: false), out bool unguardedSuccess);

            // Act
            ComponentRun run = Run(face3Ds, sewBeforeBuild: true);
            Log(run, double.NaN);

            // Assert: a valid sew build is adopted unchanged, without a warning.
            Assert.True(unguardedSuccess);
            Assert.True(run.Successful);
            Assert.Empty(run.Errors);
            Assert.Empty(run.Warnings);
            Assert.Equal(2, run.Shells.Count);
            Assert.Equal(Keys(unguarded.Select(x => x.Shell)), Keys(run.Shells));
            Assert.Contains(run.Diagnostics, x => x.StartsWith("SAM_OCCT_SEW_SUCCESS", StringComparison.Ordinal));
            Assert.DoesNotContain(run.Diagnostics, x => x.Contains("SAM_OCCT_BUILD_FALLBACK_DIRECT") || x.Contains("SAM_OCCT_BUILD_REJECTED"));
            Assert.Empty(ValidateCells(run.Shells.Select(x => new OcctCell(x, VolumeOf(x, unguarded))).ToList()));
        }

        [SkippableFact]
        public void Run_SewBeforeBuildOnBoxesWithoutTops_ReportsFailureWithoutShells()
        {
            Skip.IfNot(NativeRuntime.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: no closed volume can be built.
            List<Face3D> face3Ds = TwoBoxes(includeTop: false);

            // Act
            ComponentRun run = Run(face3Ds, sewBeforeBuild: true);
            Log(run, double.NaN);

            // Assert: the failure is an Error and Successful is false; no shells are returned.
            Assert.False(run.Successful);
            Assert.NotEmpty(run.Errors);
            Assert.Empty(run.Shells);
        }

        // ── The component ───────────────────────────────────────────────────────

        private sealed class ComponentRun
        {
            public List<Shell> Shells { get; set; } = new List<Shell>();
            public bool? Successful { get; set; }
            public List<string> Diagnostics { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> Errors { get; set; } = new List<string>();
            public List<string> Inputs { get; set; } = new List<string>();
        }

        private static ComponentRun Run(List<Face3D> face3Ds, bool? sewBeforeBuild)
        {
            SAMOCCTCreateShells component = new SAMOCCTCreateShells();
            if (sewBeforeBuild.HasValue)
            {
                // Inserted from the component's own declaration, at its declared place after _face3Ds.
                global::Grasshopper.Kernel.Parameters.Param_Boolean param = (global::Grasshopper.Kernel.Parameters.Param_Boolean)DeclaredInputs(component).Single(x => x.Param.Name == "sewBeforeBuild_").Param;
                param.PersistentData.Clear();
                param.PersistentData.Append(new GH_Boolean(sewBeforeBuild.Value));
                component.Params.RegisterInputParam(param, component.Params.IndexOfInputParam("_face3Ds") + 1);
            }

            SetInput(component, "_face3Ds", face3Ds.Select(x => new GH_ObjectWrapper(x)).ToList());
            SetInput(component, "_run", new List<GH_Boolean> { new GH_Boolean(true) });
            if (sewBeforeBuild.HasValue)
            {
                SetInput(component, "sewBeforeBuild_", new List<GH_Boolean> { new GH_Boolean(sewBeforeBuild.Value) });
            }

            Type structureIterator = typeof(GH_Component).GetNestedType("GH_StructureIterator", BindingFlags.NonPublic | BindingFlags.Public);
            IGH_DataAccess ghDataAccess = (IGH_DataAccess)Activator.CreateInstance(structureIterator, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new object[] { component }, null);
            IGH_DataAccess dataAccess = DispatchProxy.Create<IGH_DataAccess, RecordingDataAccess>();
            ((RecordingDataAccess)(object)dataAccess).Inner = ghDataAccess;
            typeof(GH_Component).GetMethod("SolveInstance", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, new object[] { dataAccess });

            Dictionary<int, object> outputs = ((RecordingDataAccess)(object)dataAccess).Outputs;

            return new ComponentRun
            {
                Shells = (Output(component, outputs, "Shells") as IEnumerable)?.Cast<object>().OfType<Shell>().ToList() ?? new List<Shell>(),
                Successful = Output(component, outputs, "Successful") as bool?,
                Diagnostics = (Output(component, outputs, "Diagnostics") as IEnumerable)?.Cast<object>().Select(x => x?.ToString()).ToList() ?? new List<string>(),
                Warnings = component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList(),
                Errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToList(),
                Inputs = component.Params.Input.Select(x => x.Name).ToList(),
            };
        }

        /// <summary>The component's declared inputs (a fresh set on every call), as the contract tests read them.</summary>
        private static GH_SAMParam[] DeclaredInputs(SAMOCCTCreateShells component)
        {
            return (GH_SAMParam[])typeof(SAMOCCTCreateShells).GetProperty("Inputs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component);
        }

        private static object Output(GH_Component component,Dictionary<int, object> outputs, string name)
        {
            return outputs.TryGetValue(component.Params.IndexOfOutputParam(name), out object value) ? value : null;
        }

        // Not generic: a generic constraint on a Grasshopper type is resolved when this class loads, and xUnit discovery
        // loads it before the Grasshopper assembly resolver is registered (the whole assembly is then skipped).
        private static void SetInput(GH_Component component, string name, IEnumerable values)
        {
            IGH_Param param = component.Params.Input[component.Params.IndexOfInputParam(name)];
            param.ClearData();
            param.AddVolatileDataList(new GH_Path(0), values);
        }

        /// <summary>Forwards every data-access call to Grasshopper's own iterator; records SetData / SetDataList.</summary>
        public class RecordingDataAccess : DispatchProxy
        {
            public IGH_DataAccess Inner { get; set; }

            public Dictionary<int, object> Outputs { get; } = new Dictionary<int, object>();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if ((targetMethod.Name == "SetData" || targetMethod.Name == "SetDataList") && args.Length >= 2 && args[0] is int index)
                {
                    Outputs[index] = args[1];
                    return true;
                }

                return targetMethod.Invoke(Inner, args);
            }
        }

        private void Log(ComponentRun run, double bucketBetweenLevels)
        {
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "bucketBetweenLevels {0}; inputs {1}; Successful {2}; shells {3}", bucketBetweenLevels, string.Join(",", run.Inputs), run.Successful, run.Shells.Count));
            foreach (string line in run.Warnings.Select(x => "[warning] " + x).Concat(run.Errors.Select(x => "[error] " + x)).Concat(run.Diagnostics.Where(x => x != null && x.Contains("SAM_OCCT_SEW")).Select(x => "[diagnostic] " + x)))
            {
                output.WriteLine(line.Length > 400 ? string.Concat(line.AsSpan(0, 400), " ...") : line);
            }
        }

        // ── References ──────────────────────────────────────────────────────────

        /// <summary>SAMOCCTCreateShells' options with every voluntary input at its default.</summary>
        private static OcctBuildOptions ComponentOptions(bool sewBeforeBuild, bool validateSewCoverage)
        {
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, SewBeforeBuild = sewBeforeBuild, SewingTolerance = 0.0, GlueMode = OcctGlueMode.Off, ValidateSewCoverage = validateSewCoverage };
        }

        private static List<OcctCell> Build(List<Face3D> face3Ds, OcctBuildOptions options, out bool success)
        {
            SAM.Geometry.OCCT.Create.Shells(face3Ds, out OcctCellComplexResult result, options);
            using (result)
            {
                success = result.Success;
                return result.Cells.ToList();
            }
        }

        /// <summary>The non-air panel faces returned by the golden masters' forced-managed Solve3D call on towers.</summary>
        private static List<Face3D> TowersFaces(double bucketBetweenLevels)
        {
            lock (towersFaces)
            {
                if (!towersFaces.TryGetValue(bucketBetweenLevels, out List<Face3D> face3Ds))
                {
                    List<Panel> panels = new List<Panel>();
                    foreach (IJSAMObject jSAMObject in SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "whole-level-towers.sam")) ?? new List<IJSAMObject>())
                    {
                        if (jSAMObject is AnalyticalModel analyticalModel) panels.AddRange(analyticalModel.GetPanels());
                        else if (jSAMObject is AdjacencyCluster adjacencyCluster) panels.AddRange(adjacencyCluster.GetPanels());
                        else if (jSAMObject is Panel panel) panels.Add(panel);
                    }

                    List<Panel> solved = panels.Solve3D(out _, out _, out _, out _, forceManagedPipeline: true, bucketBetweenLevels: bucketBetweenLevels);
                    face3Ds = solved.Where(x => x != null && x.PanelType != PanelType.Air).Select(x => x.GetFace3D()).Where(x => x != null && x.IsValid()).ToList();
                    towersFaces[bucketBetweenLevels] = face3Ds;
                }

                return face3Ds;
            }
        }

        private static List<Face3D> TwoBoxes(bool includeTop)
        {
            List<Face3D> result = new List<Face3D>();
            foreach (double x0 in new[] { 0.0, 1.0 })
            {
                double x1 = x0 + 1;
                result.Add(NativeRuntime.Quad(x0, 0, 0, x1, 0, 0, x1, 1, 0, x0, 1, 0));
                if (includeTop)
                {
                    result.Add(NativeRuntime.Quad(x0, 0, 1, x1, 0, 1, x1, 1, 1, x0, 1, 1));
                }

                result.Add(NativeRuntime.Quad(x0, 0, 0, x1, 0, 0, x1, 0, 1, x0, 0, 1));
                result.Add(NativeRuntime.Quad(x0, 1, 0, x1, 1, 0, x1, 1, 1, x0, 1, 1));
            }

            foreach (double x in new[] { 0.0, 1.0, 2.0 })
            {
                result.Add(NativeRuntime.Quad(x, 0, 0, x, 1, 0, x, 1, 1, x, 0, 1));
            }

            return result;
        }

        // ── Cells ───────────────────────────────────────────────────────────────

        private static int AssertSingleShell(List<Shell> shells, Point3D point, string room)
        {
            List<int> indices = Enumerable.Range(0, shells.Count).Where(i => shells[i].Inside(point)).ToList();
            Assert.True(indices.Count == 1, string.Format(CultureInfo.InvariantCulture, "the {0} room point {1} lies in {2} of {3} shell(s), expected exactly one", room, point, indices.Count, shells.Count));
            return indices[0];
        }

        private static double ReferenceVolume(Shell shell, List<OcctCell> reference, string room)
        {
            double volume = VolumeOf(shell, reference);
            Assert.False(double.IsNaN(volume), string.Format(CultureInfo.InvariantCulture, "the {0} room's shell ({1} faces) is not a cell of the guarded reference", room, shell.Face3Ds.Count));
            return volume;
        }

        /// <summary>The volume of the reference cell with the same complete boundary (NaN when there is none).</summary>
        private static double VolumeOf(Shell shell, List<OcctCell> reference)
        {
            string key = ShellKey(shell);
            return reference.FirstOrDefault(x => ShellKey(x.Shell) == key)?.Volume ?? double.NaN;
        }

        /// <summary>The sew guard's own cell validation (closed, positive volume, no sliver, disjoint); internal to
        /// SAM.Geometry.OCCT, so it is reached by reflection.</summary>
        private static List<string> ValidateCells(List<OcctCell> cells)
        {
            MethodInfo validateCells = typeof(OcctCell).Assembly.GetType("SAM.Geometry.OCCT.Native.OcctUnderSplitGuard", true).GetMethod("ValidateCells", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(validateCells);
            return (List<string>)validateCells.Invoke(null, new object[] { cells, ComponentOptions(sewBeforeBuild: true, validateSewCoverage: true), Tolerance.MacroDistance, null });
        }

        private static List<string> Keys(IEnumerable<Shell> shells) => shells.Select(ShellKey).OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>A complete-boundary signature: every face's external loop and holes, cyclic order and winding
        /// canonicalised, coordinates rounded to 1e-6 m, faces sorted.</summary>
        private static string ShellKey(Shell shell) => string.Join("|", shell.Face3Ds.Select(FaceKey).OrderBy(x => x, StringComparer.Ordinal));

        private static string FaceKey(Face3D face3D)
        {
            string external = Cycle(((ISegmentable3D)face3D.GetExternalEdge3D()).GetPoints());
            IEnumerable<string> holes = (face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>()).Select(x => Cycle(((ISegmentable3D)x).GetPoints())).OrderBy(x => x, StringComparer.Ordinal);
            return external + "{" + string.Join(";", holes) + "}";
        }

        private static string Cycle(List<Point3D> points)
        {
            List<string> p = points.Select(x => string.Format(CultureInfo.InvariantCulture, "{0:0.000000},{1:0.000000},{2:0.000000}", Round(x.X), Round(x.Y), Round(x.Z))).ToList();
            string best = null;
            foreach (List<string> q in new[] { p, Enumerable.Reverse(p).ToList() })
            {
                for (int i = 0; i < q.Count; i++)
                {
                    string candidate = string.Join(" ", q.Skip(i).Concat(q.Take(i)));
                    if (best == null || string.CompareOrdinal(candidate, best) < 0) best = candidate;
                }
            }

            return best;
        }

        private static double Round(double value) => System.Math.Round(value, 6) + 0.0;
    }
}
