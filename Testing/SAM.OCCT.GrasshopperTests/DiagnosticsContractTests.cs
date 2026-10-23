// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.OCCT;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.Grasshopper.OCCT;
using SAM.Core.OCCT;
using SAM.Geometry.Grasshopper.OCCT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// Unified Diagnostics Phase 3: the three OCCT components that report diagnostics keep their GUIDs, existing
    /// parameters (names, order, access, defaults), the <c>Diagnostics</c> output and saved-definition behaviour; the
    /// optional <c>diagnosticDetail_</c> is appended last; and severity maps to the Grasshopper message level.
    /// Contract level only (no Rhino document); the behaviour with a real canvas is the Windows acceptance.
    /// </summary>
    public class DiagnosticsContractTests
    {
        private static GH_SAMParam[] GetInputs(GH_Component component)
        {
            return (GH_SAMParam[])component.GetType().GetProperty("Inputs", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).GetValue(component);
        }

        private static GH_SAMParam[] GetOutputs(GH_Component component)
        {
            return (GH_SAMParam[])component.GetType().GetProperty("Outputs", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).GetValue(component);
        }

        // ── GUIDs and versions ───────────────────────────────────────────

        [Fact]
        public void ComponentGuids_AreUnchanged()
        {
            Assert.Equal(new Guid("3d6b9e02-4a17-4c8d-b5e3-1f9a2c7d4e8b"), new SAMOCCTSolve3D().ComponentGuid);
            Assert.Equal(new Guid("c104f272-9b10-454f-9825-16e2d41adfa6"), new SAMOCCTCreateAdjacencyCluster().ComponentGuid);
            Assert.Equal(new Guid("89f9c336-e4dc-4908-bb87-b7b10ee84245"), new SAMOCCTCreateShells().ComponentGuid);
        }

        [Fact]
        public void LatestComponentVersions_AreBumpedForTheChange()
        {
            Assert.Equal("0.7.1", new SAMOCCTSolve3D().LatestComponentVersion);
            Assert.Equal("0.2.1", new SAMOCCTCreateAdjacencyCluster().LatestComponentVersion);
            // 0.3.1 -> 0.4.0: the sew guard changes the computed result of a sewing build (GH_COMPONENT_STANDARDS GH-F2/H2).
            Assert.Equal("0.4.0", new SAMOCCTCreateShells().LatestComponentVersion);
        }

        // ── Existing inputs and outputs are untouched; the new input is appended last ──

        [Fact]
        public void Solve3D_Inputs_ExistingOrderKept_DiagnosticDetailAppendedLast()
        {
            string[] expected = { "_panels", "bucket_", "thicknessFactor_", "alignColinearOffset_", "normalizeCapOffset_", "bucketBetweenLevels_", "doubleWallGap_", "slitMinGap_", "slitMaxGap_", "slitMaxOverlap_", "classifyCells_", "minCellVolume_", "_run", "diagnosticDetail_" };

            GH_SAMParam[] inputs = GetInputs(new SAMOCCTSolve3D());

            Assert.Equal(expected, inputs.Select(x => x.Param.Name).ToArray());
            AssertDiagnosticDetailInput(inputs.Last());
        }

        [Fact]
        public void CreateAdjacencyCluster_Inputs_ExistingOrderKept_DiagnosticDetailAppendedLast()
        {
            string[] expected = { "_panels", "spaces_", "tolerance_", "fuzzyTolerance_", "cellComplex_", "_run", "mergeCoplanarBeforeBuild_", "diagnosticDetail_" };

            GH_SAMParam[] inputs = GetInputs(new SAMOCCTCreateAdjacencyCluster());

            Assert.Equal(expected, inputs.Select(x => x.Param.Name).ToArray());
            AssertDiagnosticDetailInput(inputs.Last());
        }

        [Fact]
        public void CreateShells_Inputs_Unchanged_NoDiagnosticDetail()
        {
            string[] expected = { "_face3Ds", "tolerance_", "fuzzyTolerance_", "sewBeforeBuild_", "sewingTolerance_", "glueMode_", "_run" };

            Assert.Equal(expected, GetInputs(new SAMOCCTCreateShells()).Select(x => x.Param.Name).ToArray());
        }

        private static void AssertDiagnosticDetailInput(GH_SAMParam input)
        {
            Assert.IsType<global::Grasshopper.Kernel.Parameters.Param_Integer>(input.Param);
            Assert.True(input.Param.Optional);
            Assert.Equal(GH_ParamAccess.item, input.Param.Access);
            Assert.Equal(ParamVisibility.Voluntary, input.ParamVisibility);

            global::Grasshopper.Kernel.Parameters.Param_Integer param = (global::Grasshopper.Kernel.Parameters.Param_Integer)input.Param;
            Assert.Equal(0, ((GH_Integer)param.PersistentData.AllData(true).First()).Value);
        }

        [Fact]
        public void Solve3D_Outputs_NamesAndOrderUnchanged()
        {
            string[] expected =
            {
                "Panels", "NakedPoints", "Slits", "SlitPanels", "Diagnostics", "Successful", "CleanFaces", "GapFillPanels", "Cells", "CellCentres", "CellVolumes", "CellClassification", "NakedWires",
                "SourceMap", "LevelFrames", "LevelGroups", "ClosureReport", "CellComplex", "ComplexFaces", "ComplexFaceOwners", "ComplexAdjacencies", "ComplexAdjacencyFaces", "ComplexSummary"
            };

            Assert.Equal(expected, GetOutputs(new SAMOCCTSolve3D()).Select(x => x.Param.Name).ToArray());
        }

        [Fact]
        public void DiagnosticsOutput_NameTypeAndAccessUnchanged_OnAllThreeComponents()
        {
            foreach (GH_Component component in new GH_Component[] { new SAMOCCTSolve3D(), new SAMOCCTCreateAdjacencyCluster(), new SAMOCCTCreateShells() })
            {
                GH_SAMParam output = GetOutputs(component).Single(x => x.Param.Name == "Diagnostics");

                Assert.IsType<global::Grasshopper.Kernel.Parameters.Param_String>(output.Param);
                Assert.Equal(GH_ParamAccess.list, output.Param.Access);
            }
        }

        [Fact]
        public void CreateAdjacencyCluster_Outputs_Unchanged()
        {
            Assert.Equal(new[] { "AdjacencyCluster", "Diagnostics", "Successful" }, GetOutputs(new SAMOCCTCreateAdjacencyCluster()).Select(x => x.Param.Name).ToArray());
            Assert.Equal(new[] { "Shells", "Diagnostics", "Successful" }, GetOutputs(new SAMOCCTCreateShells()).Select(x => x.Param.Name).ToArray());
        }

        // ── Saved definitions: an older document has no diagnosticDetail_ ──

        [Fact]
        public void ReadDetail_ComponentWithoutTheInput_IsSummaryAndNeverTouchesTheDataAccess()
        {
            // Arrange: no Params registered here, exactly as for an older saved component that lacks the input.
            SAMOCCTCreateAdjacencyCluster component = new SAMOCCTCreateAdjacencyCluster();

            // Act
            DiagnosticDetail detail = OcctDiagnosticReporter.ReadDetail(component, null, out DiagnosticRecord invalid);

            // Assert
            Assert.Equal(DiagnosticDetail.Summary, detail);
            Assert.Null(invalid);
        }

        // ── Severity to Grasshopper level ────────────────────────────────

        [Fact]
        public void ToRuntimeMessageLevel_MapsInfoToLogOnly_WarningToWarning_ErrorToError()
        {
            Assert.Null(OcctDiagnosticReporter.ToRuntimeMessageLevel(DiagnosticSeverity.Info));
            Assert.Equal(GH_RuntimeMessageLevel.Warning, OcctDiagnosticReporter.ToRuntimeMessageLevel(DiagnosticSeverity.Warning));
            Assert.Equal(GH_RuntimeMessageLevel.Error, OcctDiagnosticReporter.ToRuntimeMessageLevel(DiagnosticSeverity.Error));
        }

        [Fact]
        public void Report_MixedRecords_RaisesOnlyWarningsAndErrors_AndFiltersTheLines()
        {
            // Arrange
            SAMOCCTCreateAdjacencyCluster component = new SAMOCCTCreateAdjacencyCluster();
            List<DiagnosticRecord> records = new List<DiagnosticRecord>
            {
                new OcctDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_TOTAL", "t").ToDiagnosticRecord(),
                new OcctDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_UNRELIABLE", "w").ToDiagnosticRecord(),
                new OcctDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_FAILED", "e").ToDiagnosticRecord(),
            };

            // Act
            List<string> summary = OcctDiagnosticReporter.Report(component, records, DiagnosticDetail.Summary);

            // Assert: the Info timing line is filtered out of the list at Summary; Warning and Error are kept in full.
            Assert.Equal(new[] { "SAM_OCCT_BUILD_UNRELIABLE: w", "SAM_OCCT_NATIVE_FAILED: e" }, summary.ToArray());
            Assert.Equal(1, component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count);
            Assert.Equal(1, component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count);
            Assert.Empty(component.RuntimeMessages(GH_RuntimeMessageLevel.Remark));
        }
    }
}
