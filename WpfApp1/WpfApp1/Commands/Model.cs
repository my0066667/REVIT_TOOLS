using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace WpfApp1.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Model : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View viewId = uidoc.ActiveView;
            try
            {
                var column = new FilteredElementCollector(doc, viewId.Id).OfCategory(BuiltInCategory.OST_StructuralFraming).ToElements();
                var ele = column.ToString();
                TaskDialog.Show("Tool" , "column id: " + ele);
                MessageBox.Show("column count: " + column);
   
            }
            catch
            {
                message = "Something wrong, please try again";
            }

            return Result.Cancelled;
        }
    }
}
