using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace SAM.Core.Grasshopper.OCCT
{
    public class AssemblyInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "SAM Core OCCT";
            }
        }

        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return SAM.Core.Grasshopper.TMP.Properties.Resources.SAM_OCCT24;
            }
        }

        public override Bitmap AssemblyIcon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return SAM.Core.Grasshopper.TMP.Properties.Resources.SAM_OCCT24;
            }
        }

        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "SAM Grasshopper OCCT Toolkit";
            }
        }

        public override Guid Id
        {
            get
            {
                return new Guid("2ebace7b-7fe8-4a10-8534-64beaaad6454");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "Michal Dengusiak & Jakub Ziolkowski at Hoare Lea";
            }
        }

        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "Michal Dengusiak -> michaldengusiak@hoarelea.com and Jakub Ziolkowski -> jakubziolkowski@hoarelea.com";
            }
        }
    }
}
