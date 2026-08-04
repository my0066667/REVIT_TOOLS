using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WpfApp1.Commands.Model
{
    [Transaction(TransactionMode.Manual)]
    public class PickOb : IExternalCommand
    {
        private const string TagFamilyPath =
            @"\\whpl.local\whvn\VIET\05 Prefab\0C VN Training - PPVC\My\Slab_Tag.rfa";

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            IList<Reference> selectedReferences;
            try
            {
                selectedReferences = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new SlabFilter(),
                    "Chọn các Floor cần đặt tag");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            List<Floor> floors = selectedReferences
                .Select(r => doc.GetElement(r) as Floor)
                .Where(f => f != null)
                .ToList();

            if (floors.Count == 0)
            {
                message = "Không có Floor hợp lệ được chọn.";
                return Result.Failed;
            }

            if (!File.Exists(TagFamilyPath))
            {
                message = $"Không tìm thấy file tag family:\n{TagFamilyPath}";
                return Result.Failed;
            }

            try
            {
                using (Transaction transaction = new Transaction(doc, "Đặt Slab Tag"))
                {
                    transaction.Start();

                    Family family = LoadTagFamily(doc, TagFamilyPath);
                    if (family == null)
                    {
                        message = "Không thể load Tag Family.";
                        transaction.RollBack();
                        return Result.Failed;
                    }

                    FamilySymbol symbol = GetFirstSymbol(doc, family);
                    if (symbol == null)
                    {
                        message = "Family không có Family Type.";
                        transaction.RollBack();
                        return Result.Failed;
                    }

                    foreach (Floor floor in floors)
                    {
                        XYZ topPoint;
                        if (!TryGetTopFaceCenter(floor, out topPoint))
                        {
                            continue;
                        }

                        PlaceSlabTag(doc, doc.ActiveView, floor, symbol, topPoint);
                    }

                    transaction.Commit();
                }

                TaskDialog.Show("Tool", "Đã đặt Slab Tag.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool TryGetTopFaceCenter(Floor floor, out XYZ topPoint)
        {
            topPoint = null;
            double maxZ = double.MinValue;
            Options options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = floor.get_Geometry(options);
            if (geometry == null)
                return false;

            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;
                if (solid == null || solid.Volume <= 0)
                    continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace planarFace = face as PlanarFace;
                    if (planarFace == null)
                        continue;

                    if (!planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ))
                        continue;

                    BoundingBoxUV boundingBox = planarFace.GetBoundingBox();
                    UV centerUv = (boundingBox.Min + boundingBox.Max) * 0.5;
                    XYZ centerPoint = planarFace.Evaluate(centerUv);

                    if (centerPoint.Z > maxZ)
                    {
                        maxZ = centerPoint.Z;
                        topPoint = centerPoint;
                    }
                }

            }

            return topPoint != null;
        }

        private static bool TryGetBotFaceCenter(Floor floor, out XYZ botPoint)
        {
            botPoint = null;
            double minZ = double.MaxValue;
            Options options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = floor.get_Geometry(options);
            if (geometry == null)
                return false;

            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;
                if (solid == null || solid.Volume <= 0)
                    continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace planarFace = face as PlanarFace;
                    if (planarFace == null)
                        continue;

                    if (!planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ))
                        continue;

                    BoundingBoxUV boundingBox = planarFace.GetBoundingBox();
                    UV centerUv = (boundingBox.Min + boundingBox.Max) * 0.5;
                    XYZ centerPoint = planarFace.Evaluate(centerUv);

                    if (centerPoint.Z < minZ)
                    {
                        minZ = centerPoint.Z;
                        botPoint = centerPoint;
                    }
                }

            }

            return botPoint != null;
        }

        private static IndependentTag PlaceSlabTag(
            Document doc,
            View view,
            Floor floor,
            FamilySymbol symbol,
            XYZ tagPoint)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            return IndependentTag.Create(
                doc,
                symbol.Id,
                view.Id,
                new Reference(floor),
                false,
                TagOrientation.Horizontal,
                tagPoint);
        }

        private static FamilySymbol GetFirstSymbol(Document doc, Family family)
        {
            return family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .FirstOrDefault(symbol => symbol != null);
        }

        // Hàm này phải được gọi khi Transaction đã mở.
        private static Family LoadTagFamily(Document doc, string familyPath)
        {
            string familyName = Path.GetFileNameWithoutExtension(familyPath);

            Family existingFamily = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(
                    familyName,
                    StringComparison.OrdinalIgnoreCase));

            if (existingFamily != null)
                return existingFamily;

            Family loadedFamily;
            return doc.LoadFamily(familyPath, out loadedFamily)
                ? loadedFamily
                : null;
        }

        public class SlabFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element is Floor;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}