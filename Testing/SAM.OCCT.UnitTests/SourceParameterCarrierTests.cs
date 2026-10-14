// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// <see cref="SourceParameterCarrier"/>: which supplied panels a rebuilt face takes its instance parameters from, and
    /// how several are merged. Pure managed - no OCCT build involved.
    /// </summary>
    public class SourceParameterCarrierTests
    {
        private const string SetName = "Rhino Source";

        // A wall panel in the plane y = 0 from x0 to x1, 3 m high, carrying a source set (name, thickness).
        private static Panel Wall(double x0, double x1, string name, double thickness, double y = 0)
        {
            Panel panel = Analytical.Create.Panel(Analytical.Query.DefaultConstruction(PanelType.WallExternal), PanelType.WallExternal, Face(x0, x1, y));
            ParameterSet parameterSet = new ParameterSet(SetName);
            parameterSet.Add("Source Name", name);
            parameterSet.Add("Physical Thickness", thickness);
            panel.Add(parameterSet);
            return panel;
        }

        private static Face3D Face(double x0, double x1, double y = 0)
        {
            return new Face3D(new Polygon3D(new List<Point3D> { new Point3D(x0, y, 0), new Point3D(x1, y, 0), new Point3D(x1, y, 3), new Point3D(x0, y, 3) }));
        }

        private static Panel Rebuilt(Face3D face3D)
        {
            return Analytical.Create.Panel(Analytical.Query.DefaultConstruction(PanelType.Wall), PanelType.Wall, face3D);
        }

        private static ParameterSet Set(Panel panel)
        {
            return panel.GetParameterSets()?.Find(x => x.Name == SetName);
        }

        [Fact]
        public void Carry_FaceIsAPieceOfOneSource_CopiesItsParameters()
        {
            // Arrange
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { Wall(0, 6, "GF South wall", 0.3) }, 1e-6);
            Panel panel = Rebuilt(Face(1, 3));

            // Act
            int count = carrier.Carry(panel, Face(1, 3));

            // Assert
            Assert.Equal(1, count);
            Assert.Equal("GF South wall", Set(panel).ToString("Source Name"));
            Assert.Equal(0.3, Set(panel).ToDouble("Physical Thickness"), 9);
        }

        [Fact]
        public void Carry_FaceSpansTwoAgreeingSources_KeepsTheValueAndJoinsTheNames()
        {
            // Arrange: two in-line walls of the same thickness; the face spans both.
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { Wall(0, 3, "West part", 0.3), Wall(3, 6, "East part", 0.3) }, 1e-6);
            Panel panel = Rebuilt(Face(0, 6));

            // Act
            int count = carrier.Carry(panel, Face(0, 6));

            // Assert
            Assert.Equal(2, count);
            Assert.Equal("West part; East part", Set(panel).ToString("Source Name"));
            Assert.Equal(0.3, Set(panel).ToDouble("Physical Thickness"), 9);
            Assert.DoesNotContain(carrier.Diagnostics(), x => x.Code == "SAM_OCCT_ANALYTICAL_SOURCE_PARAMETER_CONFLICT");
        }

        [Fact]
        public void Carry_FaceSpansSourcesOfDifferentThickness_DropsTheThicknessAndWarns()
        {
            // Arrange
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { Wall(0, 3, "Thick", 0.3), Wall(3, 6, "Thin", 0.2) }, 1e-6);
            Panel panel = Rebuilt(Face(0, 6));

            // Act
            carrier.Carry(panel, Face(0, 6));

            // Assert
            Assert.False(Set(panel).Contains("Physical Thickness"), "never picked arbitrarily");
            Assert.Equal("Thick; Thin", Set(panel).ToString("Source Name"));
            (OcctDiagnosticSeverity severity, string _, string message) = carrier.Diagnostics().Single(x => x.Code == "SAM_OCCT_ANALYTICAL_SOURCE_PARAMETER_CONFLICT");
            Assert.Equal(OcctDiagnosticSeverity.Warning, severity);
            Assert.Contains("Physical Thickness", message);
        }

        [Fact]
        public void Carry_SourceTouchingOnlyAlongAnEdgeOrOnAnotherPlane_IsNotASource()
        {
            // Arrange: one wall ends where the face starts; another is parallel 0.3 m away.
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { Wall(0, 3, "Neighbour", 0.3), Wall(3, 6, "Parallel", 0.3, 0.3) }, 1e-6);
            Panel panel = Rebuilt(Face(3, 6));

            // Act
            int count = carrier.Carry(panel, Face(3, 6));

            // Assert
            Assert.Equal(0, count);
            Assert.Null(Set(panel));
        }

        [Fact]
        public void Carry_PanelParameters_AreLeftToTheRebuild()
        {
            // Arrange: the source is adiabatic and coloured; those belong to the rebuild's classification.
            Panel source = Wall(0, 6, "GF South wall", 0.3);
            source.SetValue(PanelParameter.Adiabatic, true);
            source.SetValue(PanelParameter.Color, System.Drawing.Color.Red);
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { source }, 1e-6);
            Panel panel = Rebuilt(Face(0, 6));

            // Act
            carrier.Carry(panel, Face(0, 6));

            // Assert
            Assert.False(panel.HasValue(PanelParameter.Adiabatic));
            Assert.False(panel.HasValue(PanelParameter.Color));
            Assert.Equal("GF South wall", Set(panel).ToString("Source Name"));
        }

        [Fact]
        public void Carry_AirSource_IsNeverASource()
        {
            // Arrange: Air panels pass their identity on wholesale elsewhere; they never feed a physical panel.
            Panel air = Analytical.Create.Panel(null, PanelType.Air, Face(0, 6));
            ParameterSet parameterSet = new ParameterSet(SetName);
            parameterSet.Add("Source Name", "Stair void");
            air.Add(parameterSet);
            SourceParameterCarrier carrier = new SourceParameterCarrier(new[] { air }, 1e-6);
            Panel panel = Rebuilt(Face(0, 6));

            // Act
            int count = carrier.Carry(panel, Face(0, 6));

            // Assert
            Assert.Equal(0, count);
            Assert.Empty(carrier.Diagnostics());
        }
    }
}
