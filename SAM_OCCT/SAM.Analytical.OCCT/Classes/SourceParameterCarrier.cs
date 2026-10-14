// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT
{
    /// <summary>
    /// Carries the instance parameters of the supplied panels onto the panels an OCCT rebuild creates from cell faces.
    /// The rebuild classifies each face and gives it a default construction, so without this every rebuilt panel lost
    /// what it was made from - the source name, id and layer, the physical thickness, and any other parameter set.
    /// <list type="bullet">
    /// <item><b>Sources</b> - every supplied non-Air panel coplanar with the face whose face overlaps it by area. A face
    /// is usually one piece of one element; it can span two coplanar elements when a build merges them.</item>
    /// <item><b>One source</b> - its parameters are copied.</item>
    /// <item><b>Several</b> - a value they all agree on is copied; differing texts are joined with "; " (as aperture
    /// sources are); a differing non-text value (two physical thicknesses) is not carried and is reported, never
    /// picked arbitrarily.</item>
    /// <item><b>Never carried</b> - <see cref="PanelParameter"/> values (adiabatic, colour, U-value, ...): they belong
    /// to the classification and construction the rebuild decides, exactly as before.</item>
    /// </list>
    /// Air faces are not handled here: they inherit their Air source wholesale.
    /// </summary>
    internal sealed class SourceParameterCarrier
    {
        private readonly List<Panel> sources = new List<Panel>();
        private readonly List<BoundingBox3D> boundingBox3Ds = new List<BoundingBox3D>();
        private readonly double tolerance;
        private readonly System.Guid guid_Analytical = Core.Query.Guid(typeof(PanelParameter).Assembly);
        private readonly HashSet<string> excludedNames = new HashSet<string>();

        private int carried, merged, conflicts;
        private readonly HashSet<string> conflictNames = new HashSet<string>();

        public SourceParameterCarrier(IEnumerable<Panel> panels, double tolerance)
        {
            this.tolerance = tolerance;

            foreach (Panel panel in panels ?? Enumerable.Empty<Panel>())
            {
                if (panel == null || panel.PanelType == PanelType.Air)
                {
                    continue;
                }

                List<ParameterSet> parameterSets = panel.GetParameterSets();
                BoundingBox3D boundingBox3D = panel.GetFace3D()?.GetBoundingBox(tolerance);
                if (parameterSets == null || parameterSets.Count == 0 || boundingBox3D == null)
                {
                    continue;
                }

                sources.Add(panel);
                boundingBox3Ds.Add(boundingBox3D);
            }

            foreach (System.Enum @enum in System.Enum.GetValues(typeof(PanelParameter)))
            {
                string name = Core.Attributes.ParameterProperties.Get(@enum)?.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    excludedNames.Add(name);
                }
            }
        }

        /// <summary>Copies the sources' parameters onto <paramref name="panel"/>; returns the number of sources.</summary>
        public int Carry(Panel panel, Face3D face3D)
        {
            if (panel == null || sources.Count == 0)
            {
                return 0;
            }

            List<Panel> contributors = Contributors(face3D);
            if (contributors.Count == 0)
            {
                return 0;
            }

            List<List<ParameterSet>> parameterSets_Contributors = contributors.ConvertAll(x => x.GetParameterSets() ?? new List<ParameterSet>());
            bool conflict = false;
            foreach (ParameterSet parameterSet in parameterSets_Contributors[0])
            {
                if (parameterSet?.Names == null)
                {
                    continue;
                }

                foreach (string name in parameterSet.Names.ToList())
                {
                    if (parameterSet.Guid == guid_Analytical && excludedNames.Contains(name))
                    {
                        parameterSet.Remove(name);
                        continue;
                    }

                    if (contributors.Count == 1)
                    {
                        continue;
                    }

                    List<object> values = parameterSets_Contributors.ConvertAll(x => x.Find(y => y != null && (y.Guid == parameterSet.Guid || y.Name == parameterSet.Name))?.ToObject(name));
                    if (values.TrueForAll(x => Same(x, values[0])))
                    {
                        continue;
                    }

                    if (values.TrueForAll(x => x is string))
                    {
                        parameterSet.Modify(name, string.Join("; ", values.Cast<string>().Distinct()));
                        continue;
                    }

                    parameterSet.Remove(name);
                    conflictNames.Add(name);
                    conflict = true;
                }

                if (parameterSet.Names.Any())
                {
                    panel.Add(parameterSet);
                }
            }

            carried++;
            if (contributors.Count > 1)
            {
                merged++;
            }

            if (conflict)
            {
                conflicts++;
            }

            return contributors.Count;
        }

        public void AddDiagnostics(OcctCellComplexResult cellComplexResult)
        {
            foreach ((OcctDiagnosticSeverity severity, string code, string message) in Diagnostics())
            {
                cellComplexResult?.AddDiagnostic(severity, code, message);
            }
        }

        public IEnumerable<(OcctDiagnosticSeverity Severity, string Code, string Message)> Diagnostics()
        {
            if (sources.Count == 0)
            {
                yield break;
            }

            yield return (OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SOURCE_PARAMETERS", string.Format("{0} rebuilt panel(s) kept the instance parameters (source name, id, layer, physical thickness, ...) of the supplied panel(s) they lie on; {1} of them lie on more than one, whose agreeing values were kept and differing texts joined.", carried, merged));
            if (conflicts != 0)
            {
                yield return (OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_SOURCE_PARAMETER_CONFLICT", string.Format("{0} rebuilt panel(s) lie on supplied panels that disagree on {1}; those values were not carried rather than picked arbitrarily (e.g. two elements of different physical thickness merged into one face).", conflicts, string.Join(", ", conflictNames.OrderBy(x => x))));
            }
        }

        // Every source coplanar with the face that overlaps it by more than a sliver (a shared edge is not an overlap).
        private List<Panel> Contributors(Face3D face3D)
        {
            List<Panel> result = new List<Panel>();
            Plane plane = face3D?.GetPlane();
            BoundingBox3D boundingBox3D = face3D?.GetBoundingBox(tolerance);
            Face2D face2D = plane?.Convert(face3D);
            if (face2D == null || boundingBox3D == null)
            {
                return result;
            }

            double minArea = System.Math.Max(Tolerance.MacroDistance, tolerance);
            for (int i = 0; i < sources.Count; i++)
            {
                if (!boundingBox3D.InRange(boundingBox3Ds[i]))
                {
                    continue;
                }

                Face3D face3D_Source = sources[i].GetFace3D();
                Plane plane_Source = face3D_Source?.GetPlane();
                if (plane_Source == null || !plane_Source.Coplanar(plane, tolerance))
                {
                    continue;
                }

                Face2D face2D_Source = plane.Convert(plane.Project(face3D_Source));
                double area = face2D_Source == null ? 0 : face2D.Intersection(face2D_Source, tolerance)?.Sum(x => x.GetArea()) ?? 0;
                if (area > minArea)
                {
                    result.Add(sources[i]);
                }
            }

            return result;
        }

        private static bool Same(object value_1, object value_2)
        {
            if (value_1 is double double_1 && value_2 is double double_2)
            {
                return System.Math.Abs(double_1 - double_2) <= 1e-6;
            }

            return Equals(value_1, value_2) || (value_1 != null && value_2 != null && value_1.ToString() == value_2.ToString());
        }
    }
}
