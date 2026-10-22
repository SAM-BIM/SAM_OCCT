// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;
using SolverParameter = SAM.Analytical.Solver.SolverParameter;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Nine-space wall-capture acceptance on the controlled fixture (owner intent, 2026-10-22: nine spaces; nearby
    /// faces of one wall become one shared boundary once the capture range exceeds their separation, without
    /// erasing real internal walls or merging rooms). Record: documentation/Nine-Space-Wall-Capture-Q4.md.
    /// <para>
    /// The capture range c is applied as a <see cref="SolverParameter.BucketSize"/> stamp on every wall of a fresh
    /// clone of the input: the stamp is the wall's bucket half-width and also its consolidation range in
    /// <see cref="Panel3DSnapSolver.ConsolidateWallStacks"/>. Caps stay unstamped. Everything else is the existing
    /// controlled chain (Clean3D bucketBetweenLevels 0.21 -&gt; Extend3D inputAlreadyClean, directionalCapGrow -&gt;
    /// Create.AdjacencyCluster) with the reviewed sew guard on (ValidateSewCoverage). Weights, fill, level grouping,
    /// align offset, build tolerances and rebuild options are fixed.
    /// </para>
    /// <para>
    /// Expectations come from the seed spaces, the authored geometry and the owner's reference images, never from
    /// the output. Above every intended gap (0.55 and 0.65 m) the hard target is not met at the tested commit:
    /// North0 is lost once its west wall is consolidated onto West3's (see the record). These tests keep that
    /// failure reproducible; they are not to be skipped or relaxed.
    /// </para>
    /// </summary>
    public class NineSpaceWallCaptureIntegrationTests
    {
        private const string PanelsSha256 = "0CB729D4C6D2DC94AF8E76B99D557F4206B6549E98FD6EAD873A5EF3D278BFE7";
        private const string SpacesSha256 = "A0E5A690EE21E6DE6654EFF15166A327221A7BD5640CEE72F1FE268DE0374B79";

        /// <summary>Capture ranges above every intended gap (largest 0.474 m), below the 1.0 m unrelated-wall bound.</summary>
        public const double CaptureAboveAll = 0.55;

        /// <summary>The second capture range above every intended gap.</summary>
        public const double CaptureAboveAllSecond = 0.65;

        /// <summary>Between the East1|South1 band (0.428 m authored, 0.380 m after the earlier passes) and West3|North0 (0.474 m).</summary>
        public const double CaptureBetweenBandAndWestNorth = 0.45;

        /// <summary>No facing, unrelated wall pair lies closer than this (asserted on the authored geometry).</summary>
        private const double UnrelatedWallBound = 1.0;

        private const double AlignColinearOffset = 0.3;

        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        private static readonly string[] SpaceNames = { "East1", "North0", "North1", "North2", "South1", "South2", "West1", "West2", "West3" };

        /// <summary>
        /// The intended one-wall pairs, measured on the authored geometry (plane of A to the centroid of B, the
        /// solver's own <see cref="SnappedPanel.PerpendicularSeparation"/>). West3|North0 and East1|South1 are the
        /// two owner-marked relationships; the rest are the room-to-room double lines of the owner plan.
        /// </summary>
        private static readonly WallPair[] IntendedPairs =
        {
            // Owner arrow. Anti-parallel 0.474 m: beyond the 0.3 m void guard, so only wall-stack consolidation can close it.
            new WallPair("West3|North0", "edb39f15-e5a1-4bae-8481-3aec45ed2b1e", "ca160254-72b3-4f3e-987e-a15f5ce15db9", 0.4738, 0.0, 0.912, PairStage.Consolidation),
            // West block east wall against South2's west wall; footprint 0.227 of the larger, closed by the align pass.
            new WallPair("South2|West1", "edb39f15-e5a1-4bae-8481-3aec45ed2b1e", "0664c970-4c46-4cc4-acf8-9bdcc894cb8c", 0.1633, 0.0, 0.853, PairStage.Align),
            // North0 east wall is 0.49 degrees off: it snaps only when it lies wholly in the bucket.
            new WallPair("North0|North1", "d5c1f0bc-9c8e-494c-b99d-c360dd9c2fe0", "61ba52da-3d29-4729-b7d5-309c4b41f191", 0.0981, 0.486, 0.960, PairStage.Bucket),
            new WallPair("North1|North2", "9701dd76-6719-4a30-a3e6-505156e98f3e", "1b2ef1d0-0032-4cac-a54f-4d60ffb8ad3d", 0.1354, 0.0, 0.961, PairStage.Align),
            // East1 west wall against South2's east wall (partial run, 0.622 of the smaller).
            new WallPair("East1|South2", "aa6f86f4-e03a-4b24-b1e5-0d5a24de0f5b", "5d97f223-fa1d-4669-8c23-9bc9f6f15cec", 0.2242, 0.468, 0.622, PairStage.Bucket),
            new WallPair("South1|South2", "3b032875-ce32-4d6a-a53f-b37028c55837", "5d97f223-fa1d-4669-8c23-9bc9f6f15cec", 0.1189, 0.468, 0.967, PairStage.Bucket),
            // Owner arrow. Outer skins of the four-skin band (East1 south wall, South1 north wall).
            new WallPair("East1|South1", "20fe83aa-ad65-4551-935a-97459edc959f", "5b9dbfd6-3772-475a-ab80-26c830bd2a39", 0.4278, 0.0, 0.983, PairStage.Consolidation),
        };

        /// <summary>The inner panels of the East1|South1 band: two skins and the 0.111 m box they bound.</summary>
        private static readonly HashSet<Guid> BandMembers = new HashSet<Guid>(new[]
        {
            "20fe83aa-ad65-4551-935a-97459edc959f", "31f97c71-855b-4a67-aed2-a0a364b1a728", "763f6aa3-e1f1-4de0-9ac3-fce16205de34",
            "5b9dbfd6-3772-475a-ab80-26c830bd2a39", "ae9d38d0-0aec-4878-bb80-a09e9429fbce", "5ec136d2-e15e-47e6-bf9e-ff2fb10fa27d",
        }.Select(x => new Guid(x)));

        /// <summary>Doubled exterior skins (no room on the outer side); consolidating them merges no room.</summary>
        private static readonly string[][] ExteriorSkinPairs =
        {
            new[] { "b7b8011e-a360-43f3-ac8a-67b6ed164d2e", "67adbfb7-97e4-4afa-9dbb-2e095cc05093" },
            new[] { "51a74459-441d-4584-a45e-4b3599ac7871", "1be9bfbd-adb4-4e48-a1af-f0d290142f09" },
            new[] { "51a74459-441d-4584-a45e-4b3599ac7871", "631c4c4a-c1a9-4fd7-a7e9-93e25315ff66" },
        };

        /// <summary>Room adjacencies intended by the authored model: the seven pairs above plus the West block's own
        /// separators (the y = -13.62 walls and the 15.21/15.29 slab between West1 and West2).</summary>
        private static readonly string[] IntendedAdjacencies =
        {
            "East1|South1", "East1|South2", "North0|North1", "North0|West3", "North1|North2", "South1|South2",
            "South2|West1", "West1|West2", "West1|West3", "West2|West3",
        };

        private readonly ITestOutputHelper output;

        public NineSpaceWallCaptureIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static IEnumerable<object[]> BelowGapCases()
        {
            // (pair, capture): every capture is safely below the gap the acting stage sees.
            yield return new object[] { "North0|North1", 0.05 };
            yield return new object[] { "South1|South2", 0.05 };
            yield return new object[] { "East1|South2", 0.20 };
            yield return new object[] { "East1|South1", 0.20 };
            yield return new object[] { "East1|South1", 0.35 };
            yield return new object[] { "West3|North0", CaptureBetweenBandAndWestNorth };
        }

        public static IEnumerable<object[]> AboveAllCaptures()
        {
            yield return new object[] { CaptureAboveAll };
            yield return new object[] { CaptureAboveAllSecond };
        }

        public static IEnumerable<object[]> MotionCases()
        {
            foreach (double capture in new[] { CaptureBetweenBandAndWestNorth, CaptureAboveAll, CaptureAboveAllSecond })
            {
                yield return new object[] { capture, "translate" };
                yield return new object[] { capture, "rotate Z 45" };
            }
        }

        public static IEnumerable<object[]> ExportCases()
        {
            yield return new object[] { "baseline", 0.0 };
            yield return new object[] { "below-band", 0.20 };
            yield return new object[] { "between-band-and-west-north", CaptureBetweenBandAndWestNorth };
            yield return new object[] { "above-all", CaptureAboveAll };
            yield return new object[] { "above-all", CaptureAboveAllSecond };
        }

        [SkippableFact]
        public void IntendedPairs_AuthoredGeometry_MatchRecordedSeparationsAndNoUnrelatedWallNearby()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> panels = LoadPanels();

            // Act + Assert: the committed inputs are the owner's original files.
            Assert.Equal(PanelsSha256, Sha256(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")));
            Assert.Equal(SpacesSha256, Sha256(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")));
            Assert.Equal(66, panels.Count);
            Assert.Equal(SpaceNames, LoadSpaces().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            // Two GUIDs repeat in the fixture (74eeefd7, f2142a9f); every pair panel used here is unique.
            foreach (Guid guid in IntendedPairs.SelectMany(x => new[] { x.A, x.B }).Concat(BandMembers))
            {
                Assert.Single(panels, x => x.Guid == guid);
            }

            foreach (WallPair pair in IntendedPairs)
            {
                SnappedPanel a = Snapped(panels.Single(x => x.Guid == pair.A));
                SnappedPanel b = Snapped(panels.Single(x => x.Guid == pair.B));
                double separation = a.PerpendicularSeparation(b);
                double angle = System.Math.Acos(System.Math.Min(1.0, System.Math.Abs(a.Plane.Normal.Unit.DotProduct(b.Plane.Normal.Unit)))) * 180.0 / System.Math.PI;
                double ratio = a.InPlaneOverlapRatioVsSmaller(b);
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "PAIR {0}: separation {1:0.0000} m, angle {2:0.000} deg, overlap/smaller {3:0.000}, overlap/larger {4:0.000}, align-eligible {5}, stage {6}",
                    pair.Name, separation, angle, ratio, a.InPlaneOverlapRatio(b), a.AbutsColinearWithin(b, AlignColinearOffset, Tolerance.Distance), pair.Stage));

                Assert.InRange(separation, pair.Separation - 5e-4, pair.Separation + 5e-4);
                Assert.InRange(angle, pair.AngleDegrees - 0.01, pair.AngleDegrees + 0.01);
                Assert.InRange(ratio, pair.OverlapRatio - 0.01, pair.OverlapRatio + 0.01);
                Assert.True(a.OverlapsInPlane(b, Tolerance.Distance), pair.Name + " does not overlap in plane.");
                Assert.True(pair.Separation < CaptureBetweenBandAndWestNorth || pair.Name == "West3|North0", pair.Name);
            }

            // Every other facing wall pair within the bound belongs to the band or is a doubled exterior skin.
            HashSet<string> declared = new HashSet<string>(IntendedPairs.Select(x => Key(x.A, x.B)).Concat(ExteriorSkinPairs.Select(x => Key(new Guid(x[0]), new Guid(x[1])))));
            List<Panel> walls = panels.Where(IsWall).ToList();
            double nearestUnrelated = double.MaxValue;
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = i + 1; j < walls.Count; j++)
                {
                    SnappedPanel a = Snapped(walls[i]);
                    SnappedPanel b = Snapped(walls[j]);
                    if (!a.IsParallelWith(b, 5 * System.Math.PI / 180) || !a.OverlapsInPlane(b, 0.05) || a.InPlaneOverlapRatioVsSmaller(b) < 0.5)
                    {
                        continue;
                    }

                    double separation = a.PerpendicularSeparation(b);
                    bool band = BandMembers.Contains(walls[i].Guid) && BandMembers.Contains(walls[j].Guid)
                        || (BandMembers.Contains(walls[i].Guid) || BandMembers.Contains(walls[j].Guid)) && separation < 0.3;
                    if (declared.Contains(Key(walls[i].Guid, walls[j].Guid)) || band)
                    {
                        continue;
                    }

                    nearestUnrelated = System.Math.Min(nearestUnrelated, separation);
                }
            }

            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Nearest facing unrelated wall pair: {0:0.000} m (bound {1} m).", nearestUnrelated, UnrelatedWallBound));
            Assert.True(nearestUnrelated > UnrelatedWallBound, "An unrelated facing wall pair lies within the safety bound.");
            Assert.True(CaptureAboveAllSecond < UnrelatedWallBound - 0.25);
        }

        [SkippableTheory]
        [MemberData(nameof(BelowGapCases))]
        public void CleanStage_CaptureBelowPairGap_PairStaysFartherApartThanCapture(string pairName, double capture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            WallPair pair = IntendedPairs.Single(x => x.Name == pairName);

            // Act
            ChainRun run = ChainRun.Clean(capture);

            // Assert: the stage with this reach did not capture the pair.
            double separation = PostCleanSeparation(run, pair);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} at capture {1}: post-clean separation {2:0.0000} m (authored {3:0.0000} m).", pair.Name, capture, separation, pair.Separation));
            Assert.True(separation > capture, string.Format(CultureInfo.InvariantCulture, "{0} closed to {1:0.0000} m at capture {2}.", pair.Name, separation, capture));
            Assert.All(run.CleanReport.CleanRecords.Where(x => x.Kind == CleanRecordKind.StackConsolidated), x => Assert.True(x.DistanceMoved <= capture + Tolerance.Distance));
        }

        [SkippableTheory]
        [InlineData("South2|West1")]
        [InlineData("North1|North2")]
        public void CleanStage_AlignEligiblePair_ClosedByAlignPassEvenAtSmallCapture(string pairName)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: 0.05 m is below both gaps; the align pass (alignColinearOffset 0.3, held fixed) does not use the bucket.
            const double capture = 0.05;
            WallPair pair = IntendedPairs.Single(x => x.Name == pairName);
            List<Panel> panels = LoadPanels();
            SnappedPanel a = Snapped(panels.Single(x => x.Guid == pair.A));
            SnappedPanel b = Snapped(panels.Single(x => x.Guid == pair.B));

            // Act
            ChainRun run = ChainRun.Clean(capture);

            // Assert: closed by a snap that moved a pair panel farther than the bucket could reach.
            Assert.True(a.AbutsColinearWithin(b, AlignColinearOffset, Tolerance.Distance), pair.Name + " is not align-eligible.");
            Assert.True(PostCleanSeparation(run, pair) <= 1e-3, pair.Name + " stayed open.");
            int indexA = run.SourceIndex(pair.A);
            int indexB = run.SourceIndex(pair.B);
            Assert.Contains(run.CleanReport.CleanRecords, x => x.Kind == CleanRecordKind.SnappedToBacker && x.DistanceMoved > capture
                && (x.SourceIndex == indexA && x.BackerSourceIndex == indexB || x.SourceIndex == indexB && x.BackerSourceIndex == indexA));
        }

        [SkippableTheory]
        [MemberData(nameof(AboveAllCaptures))]
        public void CleanStage_CaptureAboveEveryPairGap_ClosesEveryIntendedPairWithinRequestedRange(double capture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            ChainRun run = ChainRun.Clean(capture);

            // Assert: the requested range reached the solver for every wall, and no move exceeded it.
            Assert.Contains(run.CleanDiagnostics, x => x.StartsWith("SAM_OCCT_CLEAN3D_INPUT_OVERRIDDEN: 40 of 66 panel(s)", StringComparison.Ordinal));
            Assert.All(run.Cleaned.Where(IsWall), x => Assert.True(x.TryGetValue(SolverParameter.BucketSize, out double stamped) && System.Math.Abs(stamped - capture) <= 1e-9));
            Assert.All(run.CleanReport.CleanRecords.Where(x => x.Kind == CleanRecordKind.StackConsolidated), x => Assert.True(x.DistanceMoved <= capture + Tolerance.Distance));

            foreach (WallPair pair in IntendedPairs)
            {
                double separation = PostCleanSeparation(run, pair);
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: post-clean separation {1:0.0000} m.", pair.Name, separation));
                Assert.True(separation <= 1e-3, pair.Name + " stayed open.");
            }

            // The two pairs beyond the void guard are closed by wall-stack consolidation, not by the bucket snap.
            foreach (WallPair pair in IntendedPairs.Where(x => x.Stage == PairStage.Consolidation))
            {
                int indexA = run.SourceIndex(pair.A);
                int indexB = run.SourceIndex(pair.B);
                Assert.Contains(run.CleanReport.CleanRecords, x => x.Kind == CleanRecordKind.StackConsolidated && (x.SourceIndex == indexA || x.SourceIndex == indexB));
            }
        }

        [SkippableTheory]
        [MemberData(nameof(AboveAllCaptures))]
        public void AcceptanceChain_CaptureAboveEveryPairGap_NineSpacesWithIntendedSharedBoundaries(double capture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            ChainRun run = ChainRun.Full(capture, null, null);

            // Assert
            using (run.Result)
            {
                List<string> violations = ContractViolations(run, IntendedAdjacencies);
                WriteRun(run, violations);
                Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
            }
        }

        [SkippableFact]
        public void AcceptanceChain_CaptureBetweenBandAndWestNorth_NineSpacesWithOnlyWestNorthLeftApart()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act: 0.45 m is above the band and below West3|North0, so that one pair may stay apart (model-level
            // below-gap control); the rest of the contract is the full target.
            ChainRun run = ChainRun.Full(CaptureBetweenBandAndWestNorth, null, null);

            // Assert
            using (run.Result)
            {
                List<string> violations = ContractViolations(run, IntendedAdjacencies.Where(x => x != "North0|West3"));
                WriteRun(run, violations);
                Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
            }
        }

        [SkippableFact]
        public void AcceptanceChain_TwoCapturesAboveEveryPairGap_SameSpatialResult()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            ChainRun first = ChainRun.Full(CaptureAboveAll, null, null);
            ChainRun second = ChainRun.Full(CaptureAboveAllSecond, null, null);

            // Assert
            using (first.Result)
            using (second.Result)
            {
                List<string> differences = Differences(first, second);
                foreach (string difference in differences)
                {
                    output.WriteLine(difference);
                }

                Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
            }
        }

        [SkippableTheory]
        [MemberData(nameof(MotionCases))]
        public void AcceptanceChain_RigidMotion_SameSpatialResultAfterInverseMotion(double capture, string motion)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            Point3D pivot = new Point3D(1.0, -12.0, 0.0);
            Transform3D forward = motion == "translate"
                ? Transform3D.GetTranslation(123.4, -56.7, 7.8)
                : Transform3D.GetRotation(pivot, Vector3D.WorldZ, System.Math.PI / 4);
            Transform3D inverse = motion == "translate"
                ? Transform3D.GetTranslation(-123.4, 56.7, -7.8)
                : Transform3D.GetRotation(pivot, Vector3D.WorldZ, -System.Math.PI / 4);

            // Act
            ChainRun reference = ChainRun.Full(capture, null, null);
            ChainRun moved = ChainRun.Full(capture, forward, inverse);

            // Assert
            using (reference.Result)
            using (moved.Result)
            {
                output.WriteLine("REFERENCE");
                WriteRun(reference, new List<string>());
                output.WriteLine("MOVED " + motion);
                WriteRun(moved, new List<string>());
                List<string> differences = Differences(reference, moved);
                foreach (string difference in differences)
                {
                    output.WriteLine(difference);
                }

                Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
            }
        }

        [SkippableTheory]
        [MemberData(nameof(ExportCases))]
        public void Export_PrimarySetting_WritesReadableSamFilesForOwnerReview(string label, double capture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            string directory = Environment.GetEnvironmentVariable("SAM_OCCT_NINE_SPACE_EXPORT_DIR");
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(Path.GetTempPath(), "SAM_OCCT_NineSpaceWallCapture");
            }

            string sha = Environment.GetEnvironmentVariable("SAM_OCCT_TEST_CODE_SHA");
            string stem = string.Format(CultureInfo.InvariantCulture, "{0}_capture-{1}_occt-{2}",
                label, capture > 0 ? capture.ToString("0.00", CultureInfo.InvariantCulture) : "none", string.IsNullOrWhiteSpace(sha) ? "unlabelled" : sha.Substring(0, System.Math.Min(7, sha.Length)));
            Directory.CreateDirectory(directory);

            // Act
            ChainRun run = ChainRun.Full(capture > 0 ? (double?)capture : null, null, null);

            // Assert: written whatever the outcome, then read back.
            using (run.Result)
            {
                List<Shell> shells = run.Result?.Cells?.Select(x => x.Shell).Where(x => x != null).ToList() ?? new List<Shell>();
                string panelsPath = Path.Combine(directory, stem + "_01-processed-panels.sam");
                string clusterPath = Path.Combine(directory, stem + "_02-adjacency-cluster.sam");
                string cellsPath = Path.Combine(directory, stem + "_03-cells.sam");
                Assert.True(SAM.Core.Convert.ToFile(run.Extended.Cast<IJSAMObject>(), panelsPath));
                Assert.NotNull(run.Cluster);
                Assert.True(SAM.Core.Convert.ToFile(new IJSAMObject[] { run.Cluster }, clusterPath));
                Assert.True(SAM.Core.Convert.ToFile(shells.Cast<IJSAMObject>(), cellsPath));

                List<string> summary = ContractViolations(run, IntendedAdjacencies);
                summary.Insert(0, string.Format(CultureInfo.InvariantCulture, "{0}: {1} processed panels, {2} cells, {3} cluster spaces, {4} cluster panels; contract violations against the full target follow.",
                    stem, run.Extended.Count, shells.Count, run.Cluster.GetSpaces()?.Count ?? 0, run.Cluster.GetPanels()?.Count ?? 0));
                summary.AddRange(run.Report.ToLines());
                File.WriteAllLines(Path.Combine(directory, stem + "_summary.txt"), summary);
                WriteRun(run, summary);

                List<IJSAMObject> panelsBack = SAM.Core.Convert.ToSAM(panelsPath);
                AdjacencyCluster clusterBack = SAM.Core.Convert.ToSAM(clusterPath).OfType<AdjacencyCluster>().Single();
                List<IJSAMObject> cellsBack = SAM.Core.Convert.ToSAM(cellsPath);
                Assert.Equal(run.Extended.Count, panelsBack.OfType<Panel>().Count());
                Assert.Equal(run.Cluster.GetPanels().Count, clusterBack.GetPanels().Count);
                Assert.Equal(run.Cluster.GetSpaces().Count, clusterBack.GetSpaces().Count);
                Assert.Equal(shells.Count, cellsBack.OfType<Shell>().Count());
                Assert.Equal(PanelsSha256, Sha256(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")));
            }
        }

        /// <summary>Every way <paramref name="run"/> misses the nine-space contract; empty when it meets it.</summary>
        private static List<string> ContractViolations(ChainRun run, IEnumerable<string> expectedAdjacencies)
        {
            List<string> violations = new List<string>();
            if (run.OriginalJson != run.CurrentOriginalJson())
            {
                violations.Add("original input panels were modified");
            }

            if (run.Result == null || run.Cluster == null || !run.Result.Success)
            {
                violations.Add("no adjacency cluster / unsuccessful build");
                return violations;
            }

            SpaceMatchReport report = run.Report;
            int cellCount = run.Result.Cells.Count;
            if (cellCount != 9)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "{0} cells, expected 9", cellCount));
            }

            if ((run.Cluster.GetSpaces()?.Count ?? 0) != 9)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "{0} cluster spaces, expected 9", run.Cluster.GetSpaces()?.Count ?? 0));
            }

            foreach (SpaceMatchRecord record in report.SpaceMatches.Where(x => x.Outcome != SpaceMatchOutcome.Matched))
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} ({2})", record.Name, record.Outcome, record.Detail));
            }

            foreach (CellMatchRecord record in report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra))
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "Extra cell {0} at {1} ({2:0.###} m3)", record.CellIndex, Format(run.Back(record.Center)), record.Volume));
            }

            List<SpaceMatchRecord> matched = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Matched).ToList();
            if (matched.Select(x => x.CellIndices.Single()).Distinct().Count() != matched.Count)
            {
                violations.Add("matched spaces share a cell");
            }

            if (!(report.DoubleHeightOk.TryGetValue(West3Guid, out bool west3) && west3))
            {
                violations.Add("West3 is not one double-height cell");
            }

            SpaceMatchRecord west1 = matched.FirstOrDefault(x => x.Name == "West1");
            SpaceMatchRecord west2 = matched.FirstOrDefault(x => x.Name == "West2");
            if (west1 == null || west2 == null || west1.ActualSpan[1] > west2.ActualSpan[0] + 0.21)
            {
                violations.Add("West1/West2 are not two stacked cells");
            }

            List<string> adjacencies = Adjacencies(run);
            List<string> expected = expectedAdjacencies.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (string missing in expected.Except(adjacencies))
            {
                violations.Add("missing shared boundary " + missing);
            }

            foreach (string extra in adjacencies.Except(expected))
            {
                violations.Add("unexpected shared boundary " + extra);
            }

            foreach (OcctCell cell in run.Result.Cells.Where(x => double.IsNaN(x.Volume) || double.IsInfinity(x.Volume) || x.Volume <= 0))
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "non-positive cell volume {0}", cell.Volume));
            }

            OcctBuildOptions options = run.Options;
            foreach (string failure in OcctUnderSplitGuard.ValidateCells(run.Result.Cells, options, System.Math.Max(options.Tolerance, options.FuzzyTolerance)))
            {
                violations.Add("cell check: " + failure);
            }

            FaceCoverageReport coverage = SAM.Geometry.OCCT.Query.FaceCoverage(run.Extended.Select(x => x.GetFace3D()), run.Result, options);
            violations.AddRange(OcctUnderSplitGuard.CheckCoverageComplete(coverage));

            foreach (Guid orphan in report.OrphanClusterPanelGuids)
            {
                violations.Add("orphan cluster panel " + orphan);
            }

            string[] unreliable = { "SAM_OCCT_SEW_BUILD_INVALID", "SAM_OCCT_SEW_VOLUME_CONFLICT", "SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA", "SAM_OCCT_COVERAGE_UNAVAILABLE" };
            foreach (OcctDiagnostic diagnostic in run.Result.Diagnostics.Where(x => x.Severity == OcctDiagnosticSeverity.Error || unreliable.Contains(x.Code)))
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture, "diagnostic {0} {1}", diagnostic.Severity, diagnostic.Code));
            }

            foreach (string line in run.CleanDiagnostics.Concat(run.ExtendDiagnostics).Where(x => x.Contains("/Error]")))
            {
                violations.Add("conditioning error: " + line);
            }

            return violations;
        }

        /// <summary>Cell-to-cell shared faces as room-name pairs, the names taken from the independent matcher.</summary>
        private static List<string> Adjacencies(ChainRun run)
        {
            Dictionary<int, string> names = new Dictionary<int, string>();
            foreach (CellMatchRecord record in run.Report.CellMatches)
            {
                names[record.CellIndex] = record.Outcome == SpaceMatchOutcome.Extra
                    ? "Extra" + record.CellIndex.ToString(CultureInfo.InvariantCulture)
                    : string.Join("+", record.SpaceGuids.Select(x => run.Expected.TryGetSpace(x)?.Name).OrderBy(x => x, StringComparer.Ordinal));
            }

            return (run.Result.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                .Where(x => x.CellIndex1 != x.CellIndex2 && names.ContainsKey(x.CellIndex1) && names.ContainsKey(x.CellIndex2))
                .Select(x => string.Join("|", new[] { names[x.CellIndex1], names[x.CellIndex2] }.OrderBy(y => y, StringComparer.Ordinal)))
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Spatial differences between two runs, matched by expected room (never by GUID or list order);
        /// <paramref name="other"/>'s geometry is taken back through its inverse motion.</summary>
        private static List<string> Differences(ChainRun reference, ChainRun other)
        {
            List<string> differences = new List<string>();
            if (reference.Result == null || other.Result == null)
            {
                differences.Add("a run produced no result");
                return differences;
            }

            if (reference.Result.Cells.Count != other.Result.Cells.Count)
            {
                differences.Add(string.Format(CultureInfo.InvariantCulture, "{0} cells vs {1}", reference.Result.Cells.Count, other.Result.Cells.Count));
            }

            foreach (string name in SpaceNames)
            {
                SpaceMatchRecord a = reference.Report.SpaceMatches.Single(x => x.Name == name);
                SpaceMatchRecord b = other.Report.SpaceMatches.Single(x => x.Name == name);
                if (a.Outcome != b.Outcome)
                {
                    differences.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} vs {2}", name, a.Outcome, b.Outcome));
                    continue;
                }

                if (a.Outcome != SpaceMatchOutcome.Matched)
                {
                    continue;
                }

                OcctCell cellA = reference.Result.Cells[a.CellIndices.Single()];
                OcctCell cellB = other.Result.Cells[b.CellIndices.Single()];
                BoundingBox3D boxA = Box(reference, cellA.Shell);
                BoundingBox3D boxB = Box(other, cellB.Shell);
                // The kernel glues geometry within the fuzzy band b, so a boundary may legitimately sit up to b off:
                // volume within b times the surface area, bounding box within 2b.
                double band = reference.Options.FuzzyTolerance;
                double volumeTolerance = band * cellA.Shell.Face3Ds.Sum(x => x.GetArea());
                if (System.Math.Abs(cellA.Volume - cellB.Volume) > volumeTolerance || boxA.Min.Distance(boxB.Min) > 2 * band || boxA.Max.Distance(boxB.Max) > 2 * band)
                {
                    differences.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.###} m3 in {2}-{3} vs {4:0.###} m3 in {5}-{6}", name, cellA.Volume, Format(boxA.Min), Format(boxA.Max), cellB.Volume, Format(boxB.Min), Format(boxB.Max)));
                }
            }

            List<string> extrasA = reference.Report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra).Select(x => Format(reference.Back(x.Center))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> extrasB = other.Report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra).Select(x => Format(other.Back(x.Center))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!extrasA.SequenceEqual(extrasB))
            {
                differences.Add(string.Format(CultureInfo.InvariantCulture, "extra cells [{0}] vs [{1}]", string.Join(" ", extrasA), string.Join(" ", extrasB)));
            }

            List<string> adjacenciesA = Adjacencies(reference);
            List<string> adjacenciesB = Adjacencies(other);
            if (!adjacenciesA.SequenceEqual(adjacenciesB))
            {
                differences.Add(string.Format(CultureInfo.InvariantCulture, "shared boundaries [{0}] vs [{1}]", string.Join(", ", adjacenciesA), string.Join(", ", adjacenciesB)));
            }

            return differences;
        }

        private void WriteRun(ChainRun run, IEnumerable<string> violations)
        {
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "capture {0}: {1} processed panels, {2} cells", run.Capture?.ToString("0.00", CultureInfo.InvariantCulture) ?? "none", run.Extended.Count, run.Result?.Cells?.Count ?? 0));
            foreach (string line in run.Report?.ToLines() ?? new List<string>())
            {
                output.WriteLine(line);
            }

            if (run.Result != null && run.Report != null)
            {
                output.WriteLine("SHARED " + string.Join(", ", Adjacencies(run)));
            }

            foreach (CleanRecord record in run.CleanReport.CleanRecords.Where(x => x.Kind == CleanRecordKind.StackConsolidated || x.Kind == CleanRecordKind.OpposedCollapsed))
            {
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "CLEAN {0} {1} -> {2} {3:0.###} m", record.Kind, run.Input[record.SourceIndex].Guid, record.BackerSourceIndex >= 0 ? run.Input[record.BackerSourceIndex].Guid.ToString() : "-", record.DistanceMoved));
            }

            foreach (string line in run.ExtendDiagnostics.Where(x => x.StartsWith("SAM_OCCT_EXTEND3D_OPEN_ENDS", StringComparison.Ordinal)))
            {
                output.WriteLine(line);
            }

            foreach (string violation in violations)
            {
                output.WriteLine("VIOLATION " + violation);
            }
        }

        /// <summary>The smallest plane-to-centroid distance between the clean faces the two pair panels map to.</summary>
        private static double PostCleanSeparation(ChainRun run, WallPair pair)
        {
            IReadOnlyList<FaceKey> facesA = run.CleanReport.SourceMap.FacesOf(run.SourceIndex(pair.A));
            IReadOnlyList<FaceKey> facesB = run.CleanReport.SourceMap.FacesOf(run.SourceIndex(pair.B));
            Assert.NotEmpty(facesA);
            Assert.NotEmpty(facesB);

            double result = double.MaxValue;
            foreach (FaceKey keyA in facesA)
            {
                Plane plane = run.CleanReport.CleanFace3Ds[keyA.Value].GetPlane();
                foreach (FaceKey keyB in facesB)
                {
                    result = System.Math.Min(result, System.Math.Abs(plane.Distance(run.CleanReport.CleanFace3Ds[keyB.Value].GetBoundingBox().GetCentroid())));
                }
            }

            return result;
        }

        /// <summary>The shell's bounding box in the reference frame (vertices taken back through the run's inverse motion).</summary>
        private static BoundingBox3D Box(ChainRun run, Shell shell)
        {
            return new BoundingBox3D(shell.Face3Ds.SelectMany(x => ((ISegmentable3D)x.GetExternalEdge3D()).GetPoints()).Select(run.Back));
        }

        private static SnappedPanel Snapped(Panel panel)
        {
            return new SnappedPanel(0, panel.GetFace3D(), 1.0, 0.4, 0.4);
        }

        private static bool IsWall(Panel panel)
        {
            return System.Math.Abs(panel.GetFace3D().GetPlane().Normal.Unit.Z) < System.Math.Sin(20 * System.Math.PI / 180);
        }

        private static string Key(Guid a, Guid b)
        {
            return a.CompareTo(b) < 0 ? a + "|" + b : b + "|" + a;
        }

        private static string Format(Point3D point)
        {
            return point == null ? "-" : string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})", point.X, point.Y, point.Z);
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return System.Convert.ToHexString(sha.ComputeHash(stream));
            }
        }

        private static List<Panel> LoadPanels()
        {
            return SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
        }

        private static List<Space> LoadSpaces()
        {
            return SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();
        }

        private enum PairStage
        {
            /// <summary>Bucket snap: needs the capture half-width to reach the partner.</summary>
            Bucket,

            /// <summary>Snap's colinear-align test (alignColinearOffset 0.3 m), independent of the bucket.</summary>
            Align,

            /// <summary>Beyond the 0.3 m void guard: only wall-stack consolidation (stamp or doubleWallGap).</summary>
            Consolidation,
        }

        private sealed class WallPair
        {
            public WallPair(string name, string a, string b, double separation, double angleDegrees, double overlapRatio, PairStage stage)
            {
                Name = name;
                A = new Guid(a);
                B = new Guid(b);
                Separation = separation;
                AngleDegrees = angleDegrees;
                OverlapRatio = overlapRatio;
                Stage = stage;
            }

            public string Name { get; }

            public Guid A { get; }

            public Guid B { get; }

            public double Separation { get; }

            public double AngleDegrees { get; }

            public double OverlapRatio { get; }

            public PairStage Stage { get; }
        }

        /// <summary>One run of the controlled chain on fresh clones of the fixture.</summary>
        private sealed class ChainRun
        {
            public double? Capture { get; private set; }

            public List<Panel> Original { get; private set; }

            public string OriginalJson { get; private set; }

            public List<Panel> Input { get; private set; }

            public List<Panel> Cleaned { get; private set; }

            public List<string> CleanDiagnostics { get; private set; }

            public Solve3DReport CleanReport { get; private set; }

            public List<Panel> Extended { get; private set; }

            public List<string> ExtendDiagnostics { get; private set; } = new List<string>();

            public ExpectedSpaceSet Expected { get; private set; }

            public OcctBuildOptions Options { get; private set; }

            public AdjacencyCluster Cluster { get; private set; }

            public OcctCellComplexResult Result { get; private set; }

            public SpaceMatchReport Report { get; private set; }

            private Transform3D Inverse { get; set; }

            /// <summary>Clean3D only, at <paramref name="capture"/> (null = no stamps, the existing chain).</summary>
            public static ChainRun Clean(double? capture)
            {
                ChainRun run = new ChainRun { Capture = capture, Original = LoadPanels() };
                run.OriginalJson = run.CurrentOriginalJson();
                run.Input = run.Original.Select(x => global::SAM.Analytical.Create.Panel(x)).ToList();
                if (capture.HasValue)
                {
                    foreach (Panel panel in run.Input.Where(IsWall))
                    {
                        panel.SetValue(SolverParameter.BucketSize, capture.Value);
                    }
                }

                run.Cleaned = run.Input.Clean3D(out List<string> cleanDiagnostics, out Solve3DReport cleanReport, bucketBetweenLevels: 0.21);
                run.CleanDiagnostics = cleanDiagnostics;
                run.CleanReport = cleanReport;
                return run;
            }

            /// <summary>The full chain on clones moved by <paramref name="forward"/> (null = in place).</summary>
            public static ChainRun Full(double? capture, Transform3D forward, Transform3D inverse)
            {
                ChainRun run = new ChainRun { Capture = capture, Original = LoadPanels(), Inverse = inverse };
                run.OriginalJson = run.CurrentOriginalJson();
                run.Input = run.Original.Select(x => global::SAM.Analytical.Create.Panel(x)).ToList();
                List<Space> spaces = LoadSpaces();
                foreach (Panel panel in run.Input)
                {
                    if (capture.HasValue && IsWall(panel))
                    {
                        panel.SetValue(SolverParameter.BucketSize, capture.Value);
                    }

                    if (forward != null)
                    {
                        panel.Transform(forward);
                    }
                }

                if (forward != null)
                {
                    spaces.ForEach(x => x.Transform(forward));
                }

                run.Cleaned = run.Input.Clean3D(out List<string> cleanDiagnostics, out Solve3DReport cleanReport, bucketBetweenLevels: 0.21);
                run.CleanDiagnostics = cleanDiagnostics;
                run.CleanReport = cleanReport;
                run.Extended = run.Cleaned.Extend3D(out List<string> extendDiagnostics, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);
                run.ExtendDiagnostics = extendDiagnostics;

                // Matcher datums from the cleaned panels, as in ControlledWorkflowAcceptanceIntegrationTests.
                SpaceMatchOptions matchOptions = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
                run.Expected = ExpectedSpaceSet.Create(spaces, run.Cleaned, matchOptions, new[] { West3Guid });
                run.Options = new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, MergeCoplanarBeforeBuild = true, ValidateSewCoverage = true };
                run.Cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(run.Expected.ToSeedSpaces(), run.Extended, out OcctCellComplexResult result, new Log(), run.Options);
                run.Result = result;
                List<Panel> sourcePanels = forward == null ? run.Original : run.Input;
                run.Report = SpaceMatcher.Match(run.Expected, CellGeometry.FromComplex(result), new[] { West3Guid }, run.Cluster, sourcePanels);
                return run;
            }

            public string CurrentOriginalJson()
            {
                return string.Join("\n", Original.Select(x => x.ToJsonObject()?.ToJsonString()));
            }

            public int SourceIndex(Guid guid)
            {
                return Input.FindIndex(x => x.Guid == guid);
            }

            public Point3D Back(Point3D point)
            {
                return point == null || Inverse == null ? point : point.Transform(Inverse);
            }
        }
    }
}
