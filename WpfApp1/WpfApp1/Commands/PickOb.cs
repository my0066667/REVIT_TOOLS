using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace WpfApp1.Commands.Model
{
    [Transaction(TransactionMode.Manual)]
    public class PickOb : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            IList<Reference> references = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectFilter());
            StringBuilder sb = new StringBuilder();
            foreach (Reference reference in references) 
            {
                Element element = doc.GetElement(reference);
                ElementId id = element.Id;
                sb.AppendLine("ElementID :" + id.ToString());
            }
            TaskDialog.Show("Addin_v26", sb.ToString());

            return Result.Succeeded;
        }
        public class WallSelectFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Wall;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
