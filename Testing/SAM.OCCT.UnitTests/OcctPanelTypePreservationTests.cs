// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed tests for the narrowed panel reclassification in
    /// Create.AdjacencyCluster(IEnumerable{Panel}, ResolvedCellComplex, ...).
    ///
    /// A decoded cell face's raw plane normal has arbitrary orientation relative to its owning cell, so a panel
    /// whose type was DEFAULTED from that normal can be left calling a floor a Roof. Those panels are therefore
    /// reclassified through SAM's own UpdatePanelTypes / SetDefaultConstructionByPanelType once UpdateNormals has
    /// made the normals space-relative. The reclassification is deliberately NARROW - it must never reach a
    /// panel that carries real metadata:
    ///
    /// 1. a defaulted panel IS corrected once normals are space-relative;
    /// 2. an explicitly assigned panel type is NOT overwritten;
    /// 3. an inherited source panel's type, construction and Guid are preserved;
    /// 4. only Guids collected during the current creation call can be reclassified;
    /// 5. an existing Air panel stays Air - it is accepted for floor area, but must remain a virtual boundary.
    /// </summary>
    public class OcctPanelTypePreservationTests
    {
        private const double Length = 4;
        private const double Width = 3;
        private const double Height = 2;

        /// <summary>
        /// (1) The box's bottom face is wound counter-clockwise seen from above, so its raw plane normal points
        /// UP and the pre-reclassification guess is Roof. After reclassification it must be a floor.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_DefaultedPanel_ReclassifiedOnceNormalsAreSpaceRelative()
        {
            List<Face3D> face3Ds = BoxFaces();

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel>());

            Panel panel_Lower = LowerBoundaryPanel(adjacencyCluster);
            Assert.Equal(PanelGroup.Floor, panel_Lower.PanelType.PanelGroup());
            Assert.NotEqual(PanelType.Roof, panel_Lower.PanelType);

            // The upper boundary stays a Roof, so the correction is orientation-aware rather than blanket.
            Panel panel_Upper = UpperBoundaryPanel(adjacencyCluster);
            Assert.Equal(PanelGroup.Roof, panel_Upper.PanelType.PanelGroup());
        }

        /// <summary>
        /// (2) A supplied panel explicitly typed Wall sits on the bottom face. Reclassification would have made
        /// it SlabOnGrade; the explicit type must survive instead.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_ExplicitlyAssignedPanelType_NotOverwritten()
        {
            List<Face3D> face3Ds = BoxFaces();
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(new Construction("Explicit Wall"), PanelType.Wall, face3Ds[0]);

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel> { sourcePanel });

            Panel panel_Lower = LowerBoundaryPanel(adjacencyCluster);
            Assert.Equal(PanelType.Wall, panel_Lower.PanelType);
            Assert.NotEqual(PanelType.SlabOnGrade, panel_Lower.PanelType);
        }

        /// <summary>(3) Inherited type, construction and Guid all survive.</summary>
        [Fact]
        public void AdjacencyCluster_InheritedPanel_TypeConstructionAndGuidPreserved()
        {
            List<Face3D> face3Ds = BoxFaces();
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(new Construction("Source Raised Floor"), PanelType.FloorRaised, face3Ds[0]);

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel> { sourcePanel });

            Panel panel_Lower = LowerBoundaryPanel(adjacencyCluster);
            Assert.Equal(sourcePanel.Guid, panel_Lower.Guid);
            Assert.Equal(PanelType.FloorRaised, panel_Lower.PanelType);
            Assert.Equal("Source Raised Floor", panel_Lower.Construction?.Name);
        }

        /// <summary>
        /// (4) Within one call, the inherited panel is untouched while the defaulted panels ARE corrected - the
        /// discriminator being membership of the Guid set collected during that call. The supplied source Panel
        /// object is also left unmodified, so nothing outside the operation is affected.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_Reclassification_RestrictedToGuidsCollectedInThisCall()
        {
            List<Face3D> face3Ds = BoxFaces();
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(new Construction("Explicit Wall"), PanelType.Wall, face3Ds[0]);
            PanelType sourceTypeBefore = sourcePanel.PanelType;

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel> { sourcePanel });

            // Inherited: still the raw supplied type.
            Assert.Equal(PanelType.Wall, LowerBoundaryPanel(adjacencyCluster).PanelType);

            // Defaulted: the vertical faces were guessed as Wall from their raw normals and have been corrected
            // to WallExternal, which proves the pass ran in the same call that left the inherited panel alone.
            List<Panel> panels_Vertical = adjacencyCluster.GetPanels().FindAll(x => Vertical(x.GetFace3D()));
            Assert.NotEmpty(panels_Vertical);
            Assert.All(panels_Vertical, x => Assert.Equal(PanelType.WallExternal, x.PanelType));

            // The caller's own object is not mutated.
            Assert.Equal(sourceTypeBefore, sourcePanel.PanelType);
            Assert.Equal("Explicit Wall", sourcePanel.Construction?.Name);
        }

        /// <summary>
        /// (5) An existing Air panel stays Air. This matters precisely because Air is now accepted for
        /// floor-area calculation: it must contribute its area while remaining a virtual boundary, never being
        /// converted to Floor, SlabOnGrade, FloorExposed or Roof.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_ExistingAirPanel_RemainsAirAndIsNotConvertedToPhysicalType()
        {
            List<Face3D> face3Ds = BoxFaces();
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(global::SAM.Analytical.Query.DefaultConstruction(PanelType.Air), PanelType.Air, face3Ds[0]);

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel> { sourcePanel });

            Panel panel_Lower = LowerBoundaryPanel(adjacencyCluster);
            Assert.Equal(PanelType.Air, panel_Lower.PanelType);
            Assert.DoesNotContain(panel_Lower.PanelType, new[] { PanelType.Floor, PanelType.SlabOnGrade, PanelType.FloorExposed, PanelType.FloorInternal, PanelType.Roof });

            // ...and it still counts as the space's floor.
            Space space = Assert.Single(adjacencyCluster.GetSpaces());
            double area = adjacencyCluster.FloorArea(space, out FloorAreaCalculationMethod method, out List<Panel> panels);
            Assert.Equal(FloorAreaCalculationMethod.GeometricalFloorPanels, method);
            Assert.Equal(Length * Width, area, 3);
            Assert.Equal(PanelType.Air, Assert.Single(panels).PanelType);
        }

        /// <summary>
        /// A defaulted Air panel (the fallback when a decoded face has no usable plane normal) is likewise never
        /// reclassified into a physical type.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_AirPanelOnUpperBoundary_RemainsAir()
        {
            List<Face3D> face3Ds = BoxFaces();
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(global::SAM.Analytical.Query.DefaultConstruction(PanelType.Air), PanelType.Air, face3Ds[1]);

            AdjacencyCluster adjacencyCluster = Build(face3Ds, new List<Panel> { sourcePanel });

            Assert.Equal(PanelType.Air, UpperBoundaryPanel(adjacencyCluster).PanelType);

            // The lower boundary was still defaulted and corrected, so narrowing has not disabled the fix.
            Assert.Equal(PanelGroup.Floor, LowerBoundaryPanel(adjacencyCluster).PanelType.PanelGroup());
        }

        // ---------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------

        private static AdjacencyCluster Build(List<Face3D> face3Ds, List<Panel> sourcePanels)
        {
            List<ResolvedCellFace> faces = new List<ResolvedCellFace>();
            for (int i = 0; i < face3Ds.Count; i++)
            {
                faces.Add(new ResolvedCellFace(face3Ds[i], i + 1, new List<int> { 0 }, new List<int> { i }));
            }

            List<ResolvedCell> cells = new List<ResolvedCell> { new ResolvedCell(0, Length * Width * Height, new Point3D(Length / 2, Width / 2, Height / 2)) };
            ResolvedCellComplex complex = new ResolvedCellComplex(Guid.NewGuid(), cells, faces, null, null, 0);

            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                sourcePanels, complex, out List<string> diagnostics,
                tolerance: Tolerance.Distance, fuzzyTolerance: Tolerance.MacroDistance);

            Assert.NotNull(adjacencyCluster);
            return adjacencyCluster;
        }

        private static Panel LowerBoundaryPanel(AdjacencyCluster adjacencyCluster)
        {
            List<Panel> panels = adjacencyCluster.GetPanels().FindAll(x => !Vertical(x.GetFace3D()));
            Assert.Equal(2, panels.Count);
            return panels.OrderBy(x => x.GetFace3D().GetBoundingBox().Min.Z).First();
        }

        private static Panel UpperBoundaryPanel(AdjacencyCluster adjacencyCluster)
        {
            List<Panel> panels = adjacencyCluster.GetPanels().FindAll(x => !Vertical(x.GetFace3D()));
            Assert.Equal(2, panels.Count);
            return panels.OrderBy(x => x.GetFace3D().GetBoundingBox().Min.Z).Last();
        }

        private static bool Vertical(Face3D face3D)
        {
            return Math.Abs(global::SAM.Geometry.Spatial.Query.Tilt(face3D.GetPlane().Normal) - 90) <= 45;
        }

        /// <summary>Index 0 is the bottom face (z = 0), index 1 the top face; the rest are vertical.</summary>
        private static List<Face3D> BoxFaces()
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(Length, 0, 0), new Point3D(Length, Width, 0), new Point3D(0, Width, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, Height), new Point3D(Length, 0, Height), new Point3D(Length, Width, Height), new Point3D(0, Width, Height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(Length, 0, 0), new Point3D(Length, 0, Height), new Point3D(0, 0, Height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, Width, 0), new Point3D(Length, Width, 0), new Point3D(Length, Width, Height), new Point3D(0, Width, Height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, Width, 0), new Point3D(0, Width, Height), new Point3D(0, 0, Height)),
                TestGeometry.CreatePlanarFace(new Point3D(Length, 0, 0), new Point3D(Length, Width, 0), new Point3D(Length, Width, Height), new Point3D(Length, 0, Height))
            };
        }
    }
}
