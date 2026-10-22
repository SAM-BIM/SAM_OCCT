// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using GeometryOcctCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// The sew guard against invalid sew builds on two FIXED Revit-home Solve3D outputs (2026-10-22 attribution).
    /// <para>
    /// On these panel sets the direct MakerVolume build is translation-invariant, but the sew build is chaotic under
    /// rigid translations: merged rooms, inverted envelope cells (-216, -192, -85.7 m3) and a duplicated envelope
    /// (431.985 m3 = 2 x 215.993), all reported Success. The coverage-only guard missed two of them silently because
    /// an envelope cell covers every input face.
    /// </para>
    /// <para>
    /// The guarded build must give the same cells at every offset, matching the direct build after the inverse
    /// translation. The 14-cell decomposition is what the direct build observably produces, not verified design intent
    /// (five cells are 1.2 m3 or smaller), so the tests compare against the direct build rather than a room count.
    /// </para>
    /// </summary>
    public class SewGuardInvalidCellIntegrationTests
    {
        private const string CurrentPanels = "Revit-home-solved-current-131panels.sam";
        private const string JulyPanels = "Revit-home-solved-july-105panels.sam";

        // Observed total of the direct decomposition on both panel sets (m3); not design intent.
        private const double ObservedTotalVolume = 215.993;

        // The eight rigid offsets recorded in the attribution (exactly representable, plus 1e-7).
        private static readonly double[][] Offsets =
        {
            new[] { 0.0, 0.0, 0.0 }, new[] { 0.5, 0.0, 0.0 }, new[] { 0.0, 0.25, 0.0 }, new[] { 1.0, 1.0, 0.0 },
            new[] { 8.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 1.0 }, new[] { 1e-7, 0.0, 0.0 }, new[] { 100.0, -50.0, 0.0 },
        };

        public static IEnumerable<object[]> Cases
        {
            get
            {
                foreach (string fixture in new[] { CurrentPanels, JulyPanels })
                {
                    for (int i = 0; i < Offsets.Length; i++)
                    {
                        yield return new object[] { fixture, i };
                    }
                }
            }
        }

        private static OcctBuildOptions GrasshopperRecipe()
        {
            // Exactly the options SAMOCCTCreateAdjacencyCluster passes to the rebuild path.
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, MergeCoplanarBeforeBuild = false, ValidateSewCoverage = true };
        }

        private static OcctBuildOptions SewRecipe()
        {
            OcctBuildOptions options = GrasshopperRecipe();
            options.ValidateSewCoverage = false;
            return options;
        }

        private static OcctBuildOptions DirectRecipe()
        {
            OcctBuildOptions options = GrasshopperRecipe();
            options.SewBeforeBuild = false;
            options.ValidateSewCoverage = false;
            return options;
        }

        private static Vector3D Offset(int index)
        {
            return new Vector3D(Offsets[index][0], Offsets[index][1], Offsets[index][2]);
        }

        // Non-Air panels with valid faces, rigidly moved (fresh copies from the file every call).
        private static List<Panel> LoadPanels(string fixture, Vector3D offset, bool includeAir = false)
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Revit-Reconstruction", fixture);
            List<Panel> panels = new List<Panel>();
            foreach (IJSAMObject jSAMObject in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (jSAMObject is AnalyticalModel analyticalModel)
                {
                    panels.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                }
            }

            panels = panels.Where(x => x != null && (includeAir || x.PanelType != PanelType.Air) && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
            foreach (Panel panel in panels)
            {
                panel.Move(offset);
            }

            return panels;
        }

        private static List<Face3D> Faces(List<Panel> panels)
        {
            return panels.Select(x => x.GetFace3D()).ToList();
        }

        // Signed volume plus the inverse-translated bounding box, sorted.
        private static List<double[]> Signature(OcctCellComplexResult result, Vector3D offset)
        {
            return result.Cells.Select(x =>
            {
                BoundingBox3D box = x.Shell.GetBoundingBox();
                return new[] { x.Volume, box.Min.X - offset.X, box.Min.Y - offset.Y, box.Min.Z - offset.Z, box.Max.X - offset.X, box.Max.Y - offset.Y, box.Max.Z - offset.Z };
            }).OrderBy(x => System.Math.Round(x[0], 3)).ThenBy(x => System.Math.Round(x[1], 3)).ThenBy(x => System.Math.Round(x[2], 3)).ThenBy(x => System.Math.Round(x[3], 3)).ToList();
        }

        private static void AssertSameCells(List<double[]> expected, List<double[]> actual, string context)
        {
            Assert.True(expected.Count == actual.Count, string.Format("{0}: {1} cells expected, {2} built.", context, expected.Count, actual.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                for (int k = 0; k < expected[i].Length; k++)
                {
                    Assert.True(System.Math.Abs(expected[i][k] - actual[i][k]) <= 1e-3, string.Format("{0}: cell {1} component {2} differs ({3} vs {4}).", context, i, k, expected[i][k], actual[i][k]));
                }
            }
        }

        [SkippableTheory]
        [MemberData(nameof(Cases))]
        public void Shells_TranslatedRevitPanelsWithGuard_MatchInverseTranslatedDirectBuild(string fixture, int offsetIndex)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            Vector3D offset = Offset(offsetIndex);
            List<Face3D> faces = Faces(LoadPanels(fixture, offset));
            GeometryOcctCreate.Shells(Faces(LoadPanels(fixture, Offset(0))), out OcctCellComplexResult reference, DirectRecipe());

            // Act
            List<Shell> shells = GeometryOcctCreate.Shells(faces, out OcctCellComplexResult result, GrasshopperRecipe());

            // Assert
            using (reference)
            using (result)
            {
                string context = string.Format("{0} at ({1}, {2}, {3})", fixture, offset.X, offset.Y, offset.Z);
                Assert.NotNull(shells);
                Assert.True(result.Success, context);
                Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
                Assert.All(result.Cells, x => Assert.True(x.Volume > 0, string.Format("{0}: non-positive cell {1}.", context, x.Volume)));
                Assert.InRange(result.Cells.Sum(x => x.Volume), ObservedTotalVolume - 0.01, ObservedTotalVolume + 0.01);
                AssertSameCells(Signature(reference, Offset(0)), Signature(result, offset), context);

                FaceCoverageReport coverage = SAM.Geometry.OCCT.Query.FaceCoverage(faces, result, GrasshopperRecipe());
                Assert.True(coverage.IsClean, string.Format("{0}: {1}", context, coverage.Summary()));

                // An invalid sew build is never kept; replacing it always leaves a Warning.
                if (result.Diagnostics.Any(x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID"))
                {
                    Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" && x.Severity == OcctDiagnosticSeverity.Warning);
                }
            }
        }

        [SkippableTheory]
        [MemberData(nameof(Cases))]
        public void AdjacencyCluster_TranslatedRevitPanelsWithAir_NeverBuildsFromInvalidCells(string fixture, int offsetIndex)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: the Grasshopper path, Air gap-fill panels included.
            Vector3D offset = Offset(offsetIndex);
            List<Panel> panels = LoadPanels(fixture, offset, includeAir: true);

            // Act
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(null, panels, out OcctCellComplexResult result, null, GrasshopperRecipe());

            // Assert: either a cluster from positive, valid cells, or no cluster and an Error - never a cluster built
            // on an invalid sew build kept with a Warning.
            using (result)
            {
                string context = string.Format("{0} at ({1}, {2}, {3})", fixture, offset.X, offset.Y, offset.Z);
                if (adjacencyCluster == null)
                {
                    Assert.False(result.Success, context);
                    Assert.Contains(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
                    return;
                }

                Assert.True(result.Success, context);
                Assert.All(result.Cells, x => Assert.True(x.Volume > 0, string.Format("{0}: non-positive cell {1}.", context, x.Volume)));
                Assert.Equal(result.Cells.Count, adjacencyCluster.GetSpaces().Count);
                Assert.InRange(result.Cells.Sum(x => x.Volume), 0, ObservedTotalVolume + 0.01);
                if (result.Diagnostics.Any(x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID"))
                {
                    Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" || x.Code == "SAM_OCCT_ANALYTICAL_AIR_DIRECT_BUILD");
                }
            }
        }

        [SkippableFact]
        public void Shells_CurrentPanelsInvalidSewWithRetainTopology_FailsClosedWithoutCluster()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: find an offset whose plain sew build has a non-positive cell on this machine (+0.5 m in x
            // and +1e-7 m in x did in the attribution; floating-point trajectories may differ elsewhere).
            int invalidOffset = -1;
            for (int i = 0; i < Offsets.Length && invalidOffset < 0; i++)
            {
                GeometryOcctCreate.Shells(Faces(LoadPanels(CurrentPanels, Offset(i))), out OcctCellComplexResult sew, SewRecipe());
                using (sew)
                {
                    if (sew.Cells.Any(x => x.Volume <= 0))
                    {
                        invalidOffset = i;
                    }
                }
            }

            Skip.If(invalidOffset < 0, "No recorded offset produces an inverted sew cell with this native build; nothing to fail closed on.");
            OcctBuildOptions options = GrasshopperRecipe();
            options.RetainTopology = true;
            List<Panel> panels = LoadPanels(CurrentPanels, Offset(invalidOffset));

            // Act
            List<Shell> shells = GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, options);
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(null, panels, out OcctCellComplexResult clusterResult, null, options);

            // Assert: RetainTopology cannot take the direct build (its handle is freed), so the invalid sew build
            // must be rejected outright - never returned with its inverted cell.
            using (result)
            using (clusterResult)
            {
                Assert.Null(shells);
                Assert.False(result.Success);
                Assert.Empty(result.Cells);
                Assert.Null(result.Topology);
                Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Severity == OcctDiagnosticSeverity.Warning);
                Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Severity == OcctDiagnosticSeverity.Error);

                Assert.Null(adjacencyCluster);
                Assert.False(clusterResult.Success);
                Assert.Contains(clusterResult.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Severity == OcctDiagnosticSeverity.Error);
            }
        }

        // Unit-height L-shaped shell: x 0..2 by y 0..1 plus x 0..1 by y 1..2 (3 m3).
        private static Shell LShape()
        {
            Point3D[] footprint = { new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 2, 0), new Point3D(0, 2, 0) };
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(footprint),
                TestGeometry.CreatePlanarFace(footprint.Select(x => new Point3D(x.X, x.Y, 1)).ToArray()),
            };
            for (int i = 0; i < footprint.Length; i++)
            {
                Point3D a = footprint[i];
                Point3D b = footprint[(i + 1) % footprint.Length];
                faces.Add(TestGeometry.CreatePlanarFace(a, b, new Point3D(b.X, b.Y, 1), new Point3D(a.X, a.Y, 1)));
            }

            return new Shell(faces);
        }

        public static IEnumerable<object[]> CommonVolumeCases()
        {
            // Unit-depth/height box: x origin, x size, expected common volume with the box x 0..2 (2 m3).
            yield return new object[] { "partial overlap, centres outside each other", 1.5, 2.0, 0.5 };
            yield return new object[] { "duplicate", 0.0, 2.0, 2.0 };
            yield return new object[] { "containment", 0.5, 1.0, 1.0 };
            yield return new object[] { "face-touching", 2.0, 1.0, 0.0 };
            yield return new object[] { "seam inside the glue band", 1.9995, 1.0, 0.0 };
            yield return new object[] { "overlap just thicker than the glue band", 1.99, 1.0, 0.01 };
            yield return new object[] { "disjoint", 3.0, 1.0, 0.0 };
        }

        [SkippableTheory]
        [MemberData(nameof(CommonVolumeCases))]
        public void CommonVolume_BoxPairs_MatchesExactOverlap(string name, double x0, double sizeX, double expected)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            Shell first = TestGeometry.CreateBox(0, 0, 0, 2, 1, 1);
            Shell second = TestGeometry.CreateBox(x0, 0, 0, sizeX, 1, 1);

            // Act
            double? common = SAM.Geometry.OCCT.Native.OcctCellComplexBuilder.CommonVolume(first, second, GrasshopperRecipe());

            // Assert
            Assert.True(common.HasValue, name);
            Assert.True(System.Math.Abs(common.Value - expected) <= 1e-6, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: common volume {1}, expected {2}.", name, common, expected));
        }

        [SkippableFact]
        public void CheckDisjoint_NonSeparablePairsWithNativeExactTest_DecidedByKernel()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: a box in the notch of an L (disjoint, touching on two faces, no separating face plane: only
            // the kernel can show it disjoint), and a box intruding into the L by 0.44 m3.
            OcctBuildOptions options = GrasshopperRecipe();
            Shell lShape = LShape();
            Shell notchBox = TestGeometry.CreateBox(1, 1, 0, 1, 1, 1);
            Shell intrudingBox = TestGeometry.CreateBox(0.8, 0.8, 0, 1.2, 1.2, 1);
            List<SAM.Geometry.OCCT.OcctCell> notch = new List<SAM.Geometry.OCCT.OcctCell> { new SAM.Geometry.OCCT.OcctCell(lShape, 3), new SAM.Geometry.OCCT.OcctCell(notchBox, 1) };
            List<SAM.Geometry.OCCT.OcctCell> intruding = new List<SAM.Geometry.OCCT.OcctCell> { new SAM.Geometry.OCCT.OcctCell(lShape, 3), new SAM.Geometry.OCCT.OcctCell(intrudingBox, 1.44) };

            // Act
            List<string> notchFailures = SAM.Geometry.OCCT.Native.OcctUnderSplitGuard.CheckOverlap(notch, null, options, "cells");
            List<string> notchUndecided = SAM.Geometry.OCCT.Native.OcctUnderSplitGuard.CheckDisjoint(notch, null, options, "cells", SAM.Geometry.OCCT.Native.OcctUnderSplitGuard.NoExactTest);
            List<string> intrudingFailures = SAM.Geometry.OCCT.Native.OcctUnderSplitGuard.CheckOverlap(intruding, null, options, "cells");
            double? notchCommon = SAM.Geometry.OCCT.Native.OcctCellComplexBuilder.CommonVolume(lShape, notchBox, options);
            double? intrudingCommon = SAM.Geometry.OCCT.Native.OcctCellComplexBuilder.CommonVolume(lShape, intrudingBox, options);

            // Assert
            Assert.Empty(notchFailures);
            Assert.Contains("could not be established", Assert.Single(notchUndecided));
            Assert.Equal(0, notchCommon);
            Assert.StartsWith("cells 0 and 1 overlap", Assert.Single(intrudingFailures));
            Assert.True(intrudingCommon.HasValue && System.Math.Abs(intrudingCommon.Value - 0.44) <= 1e-6, string.Format(System.Globalization.CultureInfo.InvariantCulture, "common volume {0}", intrudingCommon));
        }
        // R3: the native exact test under rigid motions, measured by the guard's one glue-band policy (band 1 mm).
        public static IEnumerable<object[]> RigidMotions()
        {
            yield return new object[] { "identity" };
            yield return new object[] { "rotate Z 45" };
            yield return new object[] { "rotate oblique" };
            yield return new object[] { "rotate oblique, translate" };
        }

        private static Shell Moved(Shell shell, string motion)
        {
            // Oblique: about Z, then X, then Y (each elementary rotation is orthonormal).
            Transform3D[] oblique = { Transform3D.GetRotationZ(0.6), Transform3D.GetRotationX(0.5), Transform3D.GetRotationY(0.4) };
            List<Transform3D> transforms = motion == "identity" ? new List<Transform3D>()
                : motion == "rotate Z 45" ? new List<Transform3D> { Transform3D.GetRotationZ(System.Math.PI / 4) }
                : motion == "rotate oblique" ? oblique.ToList()
                : oblique.Append(Transform3D.GetTranslation(100, -50, 7)).ToList();
            foreach (Transform3D transform3D in transforms)
            {
                shell = (Shell)shell.GetTransformed(transform3D);
            }

            return shell;
        }

        private static Shell[] GlueBandPair(string name)
        {
            Shell cube = TestGeometry.CreateBox(0, 0, 0, 1, 1, 1);
            switch (name)
            {
                case "slab seam 0.5 mm": return new[] { cube, TestGeometry.CreateBox(0.9995, 0, 0, 1.0005, 1, 1) };
                case "slab overlap 1.5 mm": return new[] { cube, TestGeometry.CreateBox(0.9985, 0, 0, 1.0015, 1, 1) };
                case "corner cube 0.6 mm": return new[] { cube, TestGeometry.CreateBox(0.9994, 0.9994, 0.9994, 1, 1, 1) };
                case "corner cube 2 mm": return new[] { cube, TestGeometry.CreateBox(0.998, 0.998, 0.998, 1, 1, 1) };
                case "corner cube 9 mm": return new[] { cube, TestGeometry.CreateBox(0.991, 0.991, 0.991, 1, 1, 1) };
                case "L-notch seam 0.5 mm": return new[] { LShape(), TestGeometry.CreateBox(0.9995, 0.9995, 0, 1.0005, 1.0005, 1) };
                case "L-notch overlap 1.5 mm": return new[] { LShape(), TestGeometry.CreateBox(0.9985, 0.9985, 0, 1.0015, 1.0015, 1) };
                case "face-touching": return new[] { cube, TestGeometry.CreateBox(1, 0, 0, 1, 1, 1) };
                case "duplicate": return new[] { cube, TestGeometry.CreateBox(0, 0, 0, 1, 1, 1) };
                default: throw new System.ArgumentException(name);
            }
        }

        public static IEnumerable<object[]> GlueBandCommonCases()
        {
            // Expected common volume beyond the band (m3): 0 for seams no thicker than the band.
            object[][] cases =
            {
                new object[] { "slab seam 0.5 mm", 0.0 },
                new object[] { "slab overlap 1.5 mm", 0.0015 },
                new object[] { "corner cube 0.6 mm", 0.0 },
                new object[] { "corner cube 2 mm", 8e-9 },
                new object[] { "corner cube 9 mm", 7.29e-7 },
                new object[] { "L-notch seam 0.5 mm", 0.0 },
                new object[] { "L-notch overlap 1.5 mm", 0.00300225 },
                new object[] { "face-touching", 0.0 },
                new object[] { "duplicate", 1.0 },
            };
            foreach (object[] motion in RigidMotions())
            {
                foreach (object[] item in cases)
                {
                    yield return new object[] { motion[0], item[0], item[1] };
                }
            }
        }

        [SkippableTheory]
        [MemberData(nameof(GlueBandCommonCases))]
        public void CommonVolume_UnderRigidMotion_CountsOnlyOverlapBeyondGlueBand(string motion, string name, double expected)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: before R3 the kernel ran at the band and dropped solids under 1e-6 m3, so the 2 mm and 9 mm
            // corner cubes reported 0.
            Shell[] pair = GlueBandPair(name);

            // Act
            double? common = SAM.Geometry.OCCT.Native.OcctCellComplexBuilder.CommonVolume(Moved(pair[0], motion), Moved(pair[1], motion), GrasshopperRecipe());

            // Assert
            string message = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1}): common volume {2}, expected {3}.", name, motion, common, expected);
            Assert.True(common.HasValue, message);
            Assert.True(expected == 0 ? common.Value == 0 : System.Math.Abs(common.Value - expected) <= 1e-4 * expected, message);
        }

        public static IEnumerable<object[]> GlueBandPairCases()
        {
            foreach (object[] motion in RigidMotions())
            {
                yield return new object[] { motion[0], "slab seam 0.5 mm", false };
                yield return new object[] { motion[0], "L-notch seam 0.5 mm", false };
                yield return new object[] { motion[0], "corner cube 0.6 mm", false };
                yield return new object[] { motion[0], "slab overlap 1.5 mm", true };
                yield return new object[] { motion[0], "L-notch overlap 1.5 mm", true };
                yield return new object[] { motion[0], "corner cube 2 mm", true };
                yield return new object[] { motion[0], "duplicate", true };
            }
        }

        [SkippableTheory]
        [MemberData(nameof(GlueBandPairCases))]
        public void CheckOverlap_PairsUnderRigidMotionWithNativeExactTest_SameDecisionInEveryPose(string motion, string name, bool overlaps)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            Shell[] pair = GlueBandPair(name);
            List<OcctCell> cells = pair.Select(x => Moved(x, motion)).Select(x => new OcctCell(x, x.Volume())).ToList();

            // Act
            List<string> failures = SAM.Geometry.OCCT.Native.OcctUnderSplitGuard.CheckOverlap(cells, null, GrasshopperRecipe(), "cells");

            // Assert
            string message = name + " (" + motion + "): " + string.Join("; ", failures);
            if (overlaps)
            {
                Assert.True(failures.Count == 1 && failures[0].StartsWith("cells 0 and 1 overlap", System.StringComparison.Ordinal), message);
            }
            else
            {
                Assert.True(failures.Count == 0, message);
            }
        }


        [SkippableFact]
        public void Shells_JulyPanelsValidSewWithRetainTopology_KeepsTopologyContract()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: the untranslated July panels build a valid, fully covered sew complex.
            OcctBuildOptions options = GrasshopperRecipe();
            options.RetainTopology = true;
            List<Panel> panels = LoadPanels(JulyPanels, Offset(0));

            // Act
            List<Shell> shells = GeometryOcctCreate.CellComplexByPanels(panels, out OcctCellComplexResult result, options);

            // Assert
            using (result)
            {
                Assert.NotNull(shells);
                Assert.True(result.Success);
                Assert.NotNull(result.Topology);
                Assert.False(result.Topology.IsInvalid);
                Assert.All(result.Cells, x => Assert.True(x.Volume > 0));
                Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" || x.Code == "SAM_OCCT_BUILD_REJECTED");
            }
        }
    }
}
