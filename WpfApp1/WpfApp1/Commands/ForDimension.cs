using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;

namespace PREFAB.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ForDimension : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("PREFAB TEAM", "Loading...");
            return Result.Succeeded;
        }
    }
}
