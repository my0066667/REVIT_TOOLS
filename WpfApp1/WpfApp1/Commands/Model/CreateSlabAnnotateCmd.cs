using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Text;

namespace WpfApp1.Commands.Model
{
    /// <summary>
    /// External command that creates slab annotation families on selected floors.
    /// Handles user selection, transaction management, and result reporting.
    /// All geometry and business logic is delegated to <see cref="CreateSlabAnnotateModel"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CreateSlabAnnotateCmd : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // ── Step 1: Select floors ────────────────────────────────
                IList<Floor> selectedFloors = SelectFloors(uiDoc, doc);

                if (selectedFloors == null || selectedFloors.Count == 0)
                {
                    return Result.Cancelled;
                }

                // ── Step 2: Initialise model and options ─────────────────
                CreateSlabAnnotateOptions options = new CreateSlabAnnotateOptions
                {
                    FamilyName = "Slab Annotation",      // Đổi tên Family phù hợp
                    SymbolName = "Default",              // Đổi tên Type phù hợp
                    ThicknessParameterName = "Thickness"  // Đổi tên Parameter phù hợp
                };

                CreateSlabAnnotateModel model = new CreateSlabAnnotateModel(doc, options);

                // ── Step 3: Verify family is loaded ──────────────────────
                FamilySymbol annotationSymbol = model.GetAnnotationSymbol();

                if (annotationSymbol == null)
                {
                    TaskDialog.Show(
                        "Lỗi – Family chưa được load",
                        $"Family '{options.FamilyName}' với Type '{options.SymbolName}' " +
                        $"chưa được load vào project.\n\n" +
                        $"Vui lòng load Family trước khi chạy lệnh.");
                    return Result.Failed;
                }

                // ── Step 4: Process all floors in one transaction ────────
                int totalCreatedCount = 0;
                List<string> failedFloorIds = new List<string>();
                List<string> errorMessages = new List<string>();

                using (Transaction transaction = new Transaction(doc, "Create Slab Annotations"))
                {
                    transaction.Start();

                    foreach (Floor floor in selectedFloors)
                    {
                        CreateSlabAnnotateResult result = model.ProcessFloor(floor);

                        totalCreatedCount += result.CreatedCount;

                        if (!result.IsSuccess)
                        {
                            failedFloorIds.Add(floor.Id.Value.ToString());

                            foreach (string errorMessage in result.Messages)
                            {
                                errorMessages.Add($"Floor {floor.Id.Value}: {errorMessage}");
                            }
                        }
                    }

                    transaction.Commit();
                }

                // ── Step 5: Show report ──────────────────────────────────
                ShowResultReport(
                    selectedFloors.Count,
                    totalCreatedCount,
                    failedFloorIds,
                    errorMessages);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User pressed Escape during selection
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi Hệ Thống", ex.ToString());
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Prompts the user to pick one or more Floor elements from the model.
        /// Returns <c>null</c> if no valid floors are selected.
        /// Throws <see cref="Autodesk.Revit.Exceptions.OperationCanceledException"/>
        /// if the user presses Escape.
        /// </summary>
        private IList<Floor> SelectFloors(UIDocument uiDoc, Document doc)
        {
            List<Floor> selectedFloors = new List<Floor>();

            IList<Reference> pickedReferences = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new FloorSelectionFilter(),
                "Chọn các sàn (Floor) cần tạo Slab Annotation");

            foreach (Reference reference in pickedReferences)
            {
                Floor floor = doc.GetElement(reference) as Floor;

                if (floor != null)
                {
                    selectedFloors.Add(floor);
                }
            }

            return selectedFloors;
        }

        /// <summary>
        /// Displays a <see cref="TaskDialog"/> summarising how many
        /// annotations were created and listing any floors that failed.
        /// </summary>
        private void ShowResultReport(
            int totalFloorCount,
            int totalCreatedCount,
            List<string> failedFloorIds,
            List<string> errorMessages)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine($"Số sàn đã xử lý:          {totalFloorCount}");
            report.AppendLine($"Số Annotation đã tạo:     {totalCreatedCount}");

            if (failedFloorIds.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"Sàn bị lỗi ({failedFloorIds.Count}):");
                report.AppendLine(string.Join(", ", failedFloorIds));
            }

            if (errorMessages.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Chi tiết lỗi:");

                foreach (string errorMessage in errorMessages)
                {
                    report.AppendLine($"  • {errorMessage}");
                }
            }

            TaskDialog.Show("Create Slab Annotation – Kết quả", report.ToString());
        }
    }

    /// <summary>
    /// Selection filter that only allows <see cref="Floor"/> elements
    /// to be picked by the user.
    /// </summary>
    public class FloorSelectionFilter : ISelectionFilter
    {
        /// <summary>
        /// Returns <c>true</c> only for Floor elements.
        /// </summary>
        public bool AllowElement(Element elem)
        {
            return elem is Floor;
        }

        /// <summary>
        /// Always returns <c>true</c> – no sub-element filtering required.
        /// </summary>
        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
