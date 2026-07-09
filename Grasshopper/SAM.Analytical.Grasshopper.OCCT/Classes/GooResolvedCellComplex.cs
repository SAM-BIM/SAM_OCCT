// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Core.Grasshopper;
using SAM.Geometry.OCCT;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.OCCT
{
    /// <summary>
    /// Grasshopper wrapper for <see cref="ResolvedCellComplex"/> (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase
    /// P3): the cell complex a solve validated and adopted, handed across the GH boundary as a value so
    /// <c>SAMOCCT.CreateAdjacencyCluster</c> can consume it directly instead of rebuilding. Pure JSON
    /// round-trip via <see cref="GooJSAMObject{T}"/> (no native handle - <see cref="ResolvedCellComplex"/> is
    /// captured before the native decode is disposed), so internalize/bake/file save-load all just work.
    /// </summary>
    public class GooResolvedCellComplex : GooJSAMObject<ResolvedCellComplex>
    {
        public GooResolvedCellComplex()
            : base()
        {
        }

        public GooResolvedCellComplex(ResolvedCellComplex resolvedCellComplex)
            : base(resolvedCellComplex)
        {
        }

        public override IGH_Goo Duplicate()
        {
            return new GooResolvedCellComplex(Value);
        }

        public override string TypeName
        {
            get
            {
                return Value == null ? typeof(ResolvedCellComplex).Name : Value.GetType().Name;
            }
        }

        public override string ToString()
        {
            if (Value == null)
            {
                return null;
            }

            return string.Format("ResolvedCellComplex [SolveId {0}, {1} cell(s), {2} face(s)]", Value.SolveId, Value.Cells?.Count ?? 0, Value.Faces?.Count ?? 0);
        }
    }

    /// <summary>
    /// The Grasshopper param for <see cref="GooResolvedCellComplex"/>. Solver-output only (there is no
    /// meaningful way to hand-author a cell complex on the canvas), so the interactive prompts are not
    /// implemented - mirrors the existing solver-output Goo params (<c>GooResultParam</c>).
    /// </summary>
    public class GooResolvedCellComplexParam : GH_PersistentParam<GooResolvedCellComplex>
    {
        public override Guid ComponentGuid => new Guid("6b3f9e21-4d7c-4a1b-9e5f-2c8a6d1b3f74");

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public GooResolvedCellComplexParam()
            : base("CellComplex", "CellComplex", "SAM OCCT Resolved CellComplex", "Params", "SAM")
        {
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GooResolvedCellComplex> values)
        {
            throw new NotImplementedException();
        }

        protected override GH_GetterResult Prompt_Singular(ref GooResolvedCellComplex value)
        {
            throw new NotImplementedException();
        }
    }
}
