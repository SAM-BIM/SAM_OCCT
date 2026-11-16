// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using AnalyticalCreate = SAM.Analytical.Create;
using SolverModify = SAM.Analytical.OCCT.Solver.Modify;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static SAM.OCCT.UnitTests.ContactFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The analytical adapter: a panel's <c>SourceEvidence</c> reaches Stage A only through <c>Query.ValidSourceEvidence</c>, and then
    /// changes the outcome of <c>Modify.Clean3D</c> for a contacting anti-parallel pair (Softhouse D203) and for nothing else.
    /// </summary>
    public class SourceEvidenceWiringTests
    {
        private static readonly Construction Construction = new Construction("wall");

        /// <summary>A wall panel with a record whose bound footprint is the panel's own face, unless overridden.</summary>
        private static Panel Recorded(Face3D face3D, double g, Face3D boundFootprint = null, Vector3D moveRecordFrame = null)
        {
            Panel panel = AnalyticalCreate.Panel(Construction, PanelType.Wall, face3D);
            Plane plane = panel.Plane;
            Plane recordPlane = moveRecordFrame == null ? plane : (Plane)plane.GetMoved(moveRecordFrame);
            Face2D own = plane.Convert(panel.GetFace3D());
            Face2D bound = boundFootprint == null ? own : plane.Convert(boundFootprint);
            SourceEvidence evidence = new SourceEvidence(recordPlane, g, new[] { own }, new[] { own }, new[] { bound });

            JsonObject json = panel.ToJsonObject();
            json["SourceEvidence"] = evidence.ToJsonObject();
            return SAM.Core.Create.IJSAMObject<Panel>(json);
        }

        private static List<Panel> D203Panels(Panel thin, Panel thick)
        {
            return new List<Panel> { thin, thick };
        }

        private static (double min, double max) YExtent(IEnumerable<Panel> panels)
        {
            List<BoundingBox3D> boxes = panels.Select(x => x.GetFace3D().GetBoundingBox()).ToList();
            return (boxes.Min(x => x.Min.Y), boxes.Max(x => x.Max.Y));
        }

        [Fact]
        public void Clean3D_ContactingPairWithValidRecords_MovesBothToTheMidpoint()
        {
            // Arrange: D203 after junction snapping - skins 0.324 m apart, the records bound to the faces they are on.
            List<Panel> panels = D203Panels(
                Recorded(Box(YThin, false, 0, Run), GThin),
                Recorded(Box(YThick, true, 0, Run), GThick));
            Assert.NotNull(panels[0].ValidSourceEvidence());
            Assert.NotNull(panels[1].ValidSourceEvidence());

            // Act
            List<Panel> clean = SolverModify.Clean3D(panels, out List<string> diagnostics);

            // Assert
            Assert.NotEmpty(clean);
            (double min, double max) = YExtent(clean);
            Assert.Equal(YMid, min, 6);
            Assert.Equal(YMid, max, 6);
            Assert.DoesNotContain(diagnostics, x => x.Contains("real void"));
        }

        [Fact]
        public void Clean3D_ContactingPairWithoutRecords_StaysTwoWallsAndReportsAVoid()
        {
            // Arrange: the same two walls with no evidence.
            List<Panel> panels = D203Panels(
                AnalyticalCreate.Panel(Construction, PanelType.Wall, Box(YThin, false, 0, Run)),
                AnalyticalCreate.Panel(Construction, PanelType.Wall, Box(YThick, true, 0, Run)));

            // Act
            List<Panel> clean = SolverModify.Clean3D(panels, out List<string> diagnostics);

            // Assert
            (double min, double max) = YExtent(clean);
            Assert.Equal(YThin, min, 6);
            Assert.Equal(YThick, max, 6);
            Assert.Contains(diagnostics, x => x.Contains("real void"));
        }

        [Fact]
        public void Clean3D_RecordBoundToAnotherFootprint_IsIgnored()
        {
            // Arrange: the thick wall was edited after its record was made (the record is bound to a longer wall): not evidence.
            Face3D thickFace = Box(YThick, true, 0, Run);
            List<Panel> panels = D203Panels(
                Recorded(Box(YThin, false, 0, Run), GThin),
                Recorded(thickFace, GThick, boundFootprint: Box(YThick, true, 0, 4.0)));
            Assert.Null(panels[1].ValidSourceEvidence());

            // Act
            List<Panel> clean = SolverModify.Clean3D(panels, out List<string> _);

            // Assert
            (double min, double max) = YExtent(clean);
            Assert.Equal(YThin, min, 6);
            Assert.Equal(YThick, max, 6);
        }

        [Fact]
        public void Clean3D_RecordOnAMovedFrame_IsIgnored()
        {
            // Arrange: a record whose frame is 1 mm off the panel's (the panel was moved after extraction).
            List<Panel> panels = D203Panels(
                Recorded(Box(YThin, false, 0, Run), GThin),
                Recorded(Box(YThick, true, 0, Run), GThick, moveRecordFrame: new Vector3D(0, 0.001, 0)));
            Assert.Null(panels[1].ValidSourceEvidence());

            // Act
            List<Panel> clean = SolverModify.Clean3D(panels, out List<string> _);

            // Assert
            (double min, double max) = YExtent(clean);
            Assert.Equal(YThin, min, 6);
            Assert.Equal(YThick, max, 6);
        }

        [Fact]
        public void ResolveContactEvidences_ValidRecordAndNone_CarriesTheRecordOrNull()
        {
            // Arrange
            Panel recorded = Recorded(Box(YThin, false, 0, Run), GThin);
            Panel plain = AnalyticalCreate.Panel(Construction, PanelType.Wall, Box(YThick, true, 0, Run));

            // Act
            List<ContactEvidence> evidences = SolverModify.ResolveContactEvidences(new List<Panel> { recorded, plain });

            // Assert
            Assert.Equal(2, evidences.Count);
            Assert.Equal(GThin, evidences[0].GuaranteedThickness, 12);
            Assert.NotNull(evidences[0].RegionIntersection);
            Assert.Equal(Run * Height, evidences[0].Origin.Sum(x => x.GetArea()), 9);
            Assert.Null(evidences[1]);
        }
    }
}
