using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace WpfApp1.Commands.Model
{
    [Transaction(TransactionMode.Manual)]
    public class pickFloor : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            try
            {
                var total = 0;
                IList<Reference> selection = uidoc.Selection.PickObjects(ObjectType.Element, new rebarFilter());
                foreach (Reference reference in selection)
                {
                    Rebar rebar = doc.GetElement(reference) as Rebar;

                    int quantity = rebar.NumberOfBarPositions;
                    total = total + quantity;

                }
                MessageBox.Show("Quantity = " + total);
            }
            catch
            {
                return Result.Succeeded;
            }

            try
            {
                XYZ pickP = uidoc.Selection.PickPoint();
                TaskDialog.Show("tool", "XYZ = " + pickP);
            }
            catch
            {
                return Result.Failed;
            }
           return Result.Succeeded;
        }
        public class rebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Rebar;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}