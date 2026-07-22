using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RebarTag.Cmd
{
    /// <summary>
    /// Places one Rebar Tag for every Rebar hosted by the selected element.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RebarTagCheckerCmd : IExternalCommand
    {
        private const double TagSpacingInMillimetres = 8.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            Document document = uiDocument.Document;
            View view = document.ActiveView;

            if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.Schedule)
            {
                TaskDialog.Show("Rebar Tag", "Open a plan, section, elevation, or drafting view before placing rebar tags.");
                return Result.Cancelled;
            }

            try
            {
                //cho?n host
                Reference hostReference = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    "Select the concrete/structural host containing the rebars");
                Element host = document.GetElement(hostReference);

                //lâ?y rebar trong host
                IList<Rebar> rebars = new FilteredElementCollector(document)
                    .OfClass(typeof(Rebar))
                    .Cast<Rebar>()
                    .Where(rebar => rebar.GetHostId() == host.Id)
                    .OrderBy(rebar => rebar.Id.Value)
                    .ToList();
 
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("Rebar Tag", $"No Rebar is hosted by \"{host.Name}\".");
                    return Result.Succeeded;
                }

                ElementId rebarTagTypeId = FindRebarTagTypeId(document);
                if (rebarTagTypeId == ElementId.InvalidElementId)
                {
                    message = "No loaded Rebar Tag family type was found. Load a tag family in category Structural Rebar Tags (OST_RebarTags) and run the command again.";
                    return Result.Failed;
                }

                //cho?n ?iê?m ???t tag ?â?u tiên
                XYZ firstTagPosition = uiDocument.Selection.PickPoint(
                    "Click the position for the first rebar tag");

                double spacing = UnitUtils.ConvertToInternalUnits(
                    TagSpacingInMillimetres,
                    UnitTypeId.Millimeters);
                XYZ viewUp = view.UpDirection.Normalize();

                using Transaction transaction = new Transaction(document, "Tag rebars in selected host");
                transaction.Start();

                for (int index = 0; index < rebars.Count; index++)
                {
                    XYZ tagPosition = firstTagPosition + viewUp * (index * spacing);
                    Reference rebarReference = new Reference(rebars[index]);

                    IndependentTag.Create(
                        document,
                        rebarTagTypeId,
                        view.Id,
                        rebarReference,
                        true,
                        TagOrientation.Horizontal,
                        tagPosition);
                }

                transaction.Commit();
                TaskDialog.Show("Rebar Tag", $"Placed {rebars.Count} rebar tag(s).");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return Result.Failed;
            }
        }

        private static ElementId FindRebarTagTypeId(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Category?.Id.Value == (long)BuiltInCategory.OST_RebarTags)
                ?.Id ?? ElementId.InvalidElementId;
        }
    }
}
