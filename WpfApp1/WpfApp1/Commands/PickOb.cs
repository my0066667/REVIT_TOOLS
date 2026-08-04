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

            //IList<Reference> references = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectFilter());
            //StringBuilder sb = new StringBuilder();
            //foreach (Reference reference in references) 
            //{
            //    Element element = doc.GetElement(reference);
            //    ElementId id = element.Id;
            //    sb.AppendLine("ElementID :" + id.ToString());
            //}
            //TaskDialog.Show("Addin_v26", sb.ToString());

            //return Result.Succeeded;
            StringBuilder sb = new StringBuilder();
            try
            {
                IList<Reference> references = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectFilter());
                foreach (Reference reference in references)
                {
                    Element element = doc.GetElement(reference);
                    ElementId id = element.Id;
                    Options options = new Options();
                    GeometryElement geo = element.get_Geometry(options);
                    foreach(GeometryObject obj in geo)
                    {
                        if(obj is Solid solid && solid.Volume > 0)
                        {
                            foreach(Face face in solid.Faces)
                            {

                            }   
                            foreach(Edge edge in solid.Edges)
                            {

                            }    
                        }
                    }    
                    sb.AppendLine("Model:" + id);
                }
            }
            catch
            {
                TaskDialog.Show("Addin_v26", "Fail, please try again");
            }
            TaskDialog.Show("Done",sb.ToString());
            return Result.Cancelled;
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
