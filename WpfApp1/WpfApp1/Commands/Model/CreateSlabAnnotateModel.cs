using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WpfApp1.Commands.Model
{

    public class CreateSlabAnnotateOptions
    {
        public string FamilyName { get; set; } = "Slab Annotation";
        public string SymbolName { get; set; } = "Default";
        public string ThicknessParameterName { get; set; } = "Thickness";
        public IList<BuiltInCategory> VoidCategories { get; set; } = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructuralFoundation
        };
    }
    public class CreateSlabAnnotateResult
    {
        public bool IsSuccess { get; set; } = true;
        public int CreatedCount { get; set; }
        public IList<string> Messages { get; } = new List<string>();
    }

    public class AnnotationPlacement
    {
        public Face HostFace { get; set; }
        public XYZ Point { get; set; }
        public XYZ Direction { get; set; }
    }

    public class CreateSlabAnnotateModel
    {
        private const double UpwardNormalThreshold = 0.3;
        private const double MinimumFaceAreaSqFt = 0.01;
        private const double FeetToMillimeters = 304.8;
        private const double DuplicateDistanceTolerance = 0.1; // feet
        private readonly Document _document;
        private readonly CreateSlabAnnotateOptions _options;

        private FamilySymbol _cachedSymbol;
        private readonly Dictionary<long, Solid> _solidWithRefsCache = new Dictionary<long, Solid>();
        private readonly Dictionary<long, Solid> _solidNoRefsCache = new Dictionary<long, Solid>();

        public CreateSlabAnnotateModel(Document document, CreateSlabAnnotateOptions options)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // ═════════════════════════════════════════════════════════════════
        #region Main Processing
        // ═════════════════════════════════════════════════════════════════

        public CreateSlabAnnotateResult ProcessFloor(Floor floor)
        {
            CreateSlabAnnotateResult result = new CreateSlabAnnotateResult();

            if (floor == null)
            {
                result.IsSuccess = false;
                result.Messages.Add("Floor element is null.");
                return result;
            }

            try
            {
                // 1. Resolve and cache annotation symbol
                FamilySymbol annotationSymbol = GetAnnotationSymbol();
                if (annotationSymbol == null)
                {
                    result.IsSuccess = false;
                    result.Messages.Add($"Family '{_options.FamilyName}' / '{_options.SymbolName}' not found in project.");
                    return result;
                }

                // 2. Extract the top face (with valid Reference for hosting)
                Face topFace = GetTopFace(floor);
                if (topFace == null)
                {
                    result.IsSuccess = false;
                    result.Messages.Add("Could not find the top face of the floor.");
                    return result;
                }

                // 3. Calculate slab thickness in mm
                double thicknessMm = GetSlabThickness(floor);

                // 4. Find void families that intersect this floor
                IList<FamilyInstance> voidFamilies = FindVoidFamilies(floor);

                // 5. Build placement locations based on void count
                IList<AnnotationPlacement> placements =
                    BuildAnnotationPlacements(floor, topFace, voidFamilies);

                // 6. Create an annotation at each placement
                foreach (AnnotationPlacement placement in placements)
                {
                    try
                    {
                        FamilyInstance annotation = CreateAnnotation(
                            placement.HostFace,
                            placement.Point,
                            placement.Direction,
                            annotationSymbol);

                        if (annotation != null)
                        {
                            SetThicknessParameter(annotation, thicknessMm);
                            result.CreatedCount++;
                        }
                    }
                    catch (Exception placementEx)
                    {
                        Log($"Annotation placement failed: {placementEx.Message}");
                        result.Messages.Add($"Placement failed: {placementEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Messages.Add(ex.Message);
                Log($"Floor {floor.Id.Value}: {ex}");
            }

            return result;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Family Symbol
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches the document for a <see cref="FamilySymbol"/> matching
        /// <see cref="CreateSlabAnnotateOptions.FamilyName"/> and
        /// <see cref="CreateSlabAnnotateOptions.SymbolName"/>.
        /// The result is cached after the first successful lookup.
        /// Returns <c>null</c> when the family has not been loaded.
        /// </summary>
        public FamilySymbol GetAnnotationSymbol()
        {
            if (_cachedSymbol != null)
                return _cachedSymbol;

            FilteredElementCollector collector = new FilteredElementCollector(_document);

            FamilySymbol matchedSymbol = collector
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(symbol =>
                    symbol.Family.Name.Equals(_options.FamilyName, StringComparison.OrdinalIgnoreCase)
                    && symbol.Name.Equals(_options.SymbolName, StringComparison.OrdinalIgnoreCase));

            if (matchedSymbol == null)
            {
                Log($"FamilySymbol not found: {_options.FamilyName} / {_options.SymbolName}");
                return null;
            }

            // Activate the symbol so it can be placed
            if (!matchedSymbol.IsActive)
            {
                matchedSymbol.Activate();
                _document.Regenerate();
            }

            _cachedSymbol = matchedSymbol;
            return _cachedSymbol;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Solid Extraction
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Extracts the largest solid from a floor's geometry.
        /// Uses <c>ComputeReferences = true</c> so faces carry valid
        /// <see cref="Reference"/> objects required for face-based hosting.
        /// The result is cached per floor Id.
        /// </summary>
        public Solid GetFloorSolid(Floor floor)
        {
            long floorId = floor.Id.Value;
            if (_solidWithRefsCache.TryGetValue(floorId, out Solid cached))
                return cached;

            Options geometryOptions = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            Solid floorSolid = ExtractLargestSolid(floor.get_Geometry(geometryOptions));

            if (floorSolid != null)
                _solidWithRefsCache[floorId] = floorSolid;

            return floorSolid;
        }

        /// <summary>
        /// Extracts the floor solid without computed references.
        /// Suitable for boolean operations which do not require references
        /// and can sometimes fail with referenced solids.
        /// The result is cached per floor Id.
        /// </summary>
        private Solid GetFloorSolidForBoolean(Floor floor)
        {
            long floorId = floor.Id.Value;
            if (_solidNoRefsCache.TryGetValue(floorId, out Solid cached))
                return cached;

            Options geometryOptions = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            Solid floorSolid = ExtractLargestSolid(floor.get_Geometry(geometryOptions));

            if (floorSolid != null)
                _solidNoRefsCache[floorId] = floorSolid;

            return floorSolid;
        }

        /// <summary>
        /// Extracts the largest solid from an arbitrary element's geometry.
        /// Handles both direct <see cref="Solid"/> objects and nested
        /// <see cref="GeometryInstance"/> objects (common for family instances).
        /// </summary>
        public Solid GetElementSolid(Element element)
        {
            Options geometryOptions = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            return ExtractLargestSolid(element.get_Geometry(geometryOptions));
        }

        /// <summary>
        /// Walks through a <see cref="GeometryElement"/> recursively and
        /// returns the <see cref="Solid"/> with the greatest volume.
        /// Skips empty or degenerate solids (volume ≤ 0 or no faces).
        /// </summary>
        private Solid ExtractLargestSolid(GeometryElement geometryElement)
        {
            if (geometryElement == null)
                return null;

            Solid largestSolid = null;
            double largestVolume = 0;

            foreach (GeometryObject geometryObject in geometryElement)
            {
                // Direct solid
                if (geometryObject is Solid directSolid
                    && directSolid.Faces.Size > 0
                    && directSolid.Volume > largestVolume)
                {
                    largestSolid = directSolid;
                    largestVolume = directSolid.Volume;
                }

                // Nested GeometryInstance (e.g., family instances)
                if (geometryObject is GeometryInstance geometryInstance)
                {
                    GeometryElement instanceGeometry = geometryInstance.GetInstanceGeometry();
                    Solid nestedSolid = ExtractLargestSolid(instanceGeometry);

                    if (nestedSolid != null && nestedSolid.Volume > largestVolume)
                    {
                        largestSolid = nestedSolid;
                        largestVolume = nestedSolid.Volume;
                    }
                }
            }

            return largestSolid;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Top Face / Bottom Face
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the top face of a floor by analysing its solid geometry.
        /// Selects the face whose average normal has the largest upward (Z)
        /// component. Among equally-upward faces the one with the greatest
        /// area wins. Works with flat, sloped, and shape-edited floors.
        /// Does <b>not</b> use BoundingBox to identify the top face.
        /// </summary>
        public Face GetTopFace(Floor floor)
        {
            Solid floorSolid = GetFloorSolid(floor);
            if (floorSolid == null)
            {
                Log($"Floor {floor.Id.Value}: No solid geometry found.");
                return null;
            }

            return FindBestTopFace(floorSolid);
        }

        /// <summary>
        /// Scans every face of a solid and returns the one that best
        /// represents the "top" surface. Selection criteria:
        /// 1. Normal must point upward (Z &gt; <see cref="UpwardNormalThreshold"/>).
        /// 2. Prefer higher Z-component in the normal.
        /// 3. Break ties by larger face area.
        /// </summary>
        private Face FindBestTopFace(Solid solid)
        {
            Face bestFace = null;
            double bestNormalZ = 0;
            double bestArea = 0;

            foreach (Face candidateFace in solid.Faces)
            {
                XYZ averageNormal = ComputeFaceAverageNormal(candidateFace);
                if (averageNormal == null)
                    continue;

                // Skip faces that do not point upward
                if (averageNormal.Z <= UpwardNormalThreshold)
                    continue;

                double candidateArea = candidateFace.Area;

                // A face is a better candidate if its normal is significantly
                // more upward, or if the normals are similar but the face is larger.
                bool isMoreUpward = averageNormal.Z > bestNormalZ + 0.1;
                bool isSimilarButLarger =
                    Math.Abs(averageNormal.Z - bestNormalZ) <= 0.1
                    && candidateArea > bestArea;

                if (isMoreUpward || isSimilarButLarger)
                {
                    bestFace = candidateFace;
                    bestNormalZ = averageNormal.Z;
                    bestArea = candidateArea;
                }
            }

            return bestFace;
        }

        /// <summary>
        /// Returns every upward-facing face from a solid whose area exceeds
        /// <see cref="MinimumFaceAreaSqFt"/>. Used after boolean operations
        /// to identify separate floor regions.
        /// </summary>
        private IList<Face> FindAllTopFaces(Solid solid)
        {
            List<Face> topFaces = new List<Face>();

            foreach (Face candidateFace in solid.Faces)
            {
                XYZ averageNormal = ComputeFaceAverageNormal(candidateFace);
                if (averageNormal == null)
                    continue;

                if (averageNormal.Z > UpwardNormalThreshold
                    && candidateFace.Area > MinimumFaceAreaSqFt)
                {
                    topFaces.Add(candidateFace);
                }
            }

            return topFaces;
        }

        /// <summary>
        /// Finds the bottom face of a solid – the face whose average normal
        /// has the most negative Z component and the largest area.
        /// Used for thickness calculation when CompoundStructure is unavailable.
        /// </summary>
        private Face FindBottomFace(Solid solid)
        {
            Face bestFace = null;
            double bestNegativeZ = 0;

            foreach (Face candidateFace in solid.Faces)
            {
                XYZ averageNormal = ComputeFaceAverageNormal(candidateFace);
                if (averageNormal == null)
                    continue;

                // Bottom faces point downward
                if (averageNormal.Z >= -UpwardNormalThreshold)
                    continue;

                bool isMoreDownward = averageNormal.Z < bestNegativeZ - 0.1;
                bool isSimilarButLarger =
                    Math.Abs(averageNormal.Z - bestNegativeZ) <= 0.1
                    && (bestFace == null || candidateFace.Area > bestFace.Area);

                if (isMoreDownward || isSimilarButLarger)
                {
                    bestFace = candidateFace;
                    bestNegativeZ = averageNormal.Z;
                }
            }

            return bestFace;
        }

        /// <summary>
        /// Evaluates the face normal at the centre of its UV bounding box.
        /// Handles both planar and curved/warped faces gracefully.
        /// Returns <c>null</c> on failure.
        /// </summary>
        private XYZ ComputeFaceAverageNormal(Face face)
        {
            try
            {
                BoundingBoxUV uvBounds = face.GetBoundingBox();
                UV midUv = new UV(
                    (uvBounds.Min.U + uvBounds.Max.U) / 2.0,
                    (uvBounds.Min.V + uvBounds.Max.V) / 2.0);

                return face.ComputeNormal(midUv).Normalize();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Slab Thickness
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates the slab thickness in millimetres using a three-level
        /// fallback strategy:
        /// <list type="number">
        ///   <item>CompoundStructure – sum of all layer widths</item>
        ///   <item>Face distance – projected distance between top and bottom faces</item>
        ///   <item>Solid height – bounding-box Z extent of the floor's solid</item>
        /// </list>
        /// The result is rounded to 1 mm.
        /// Works with standard floors, structural floors, sloped floors,
        /// and shape-edited floors.
        /// </summary>
        public double GetSlabThickness(Floor floor)
        {
            // Priority 1: CompoundStructure
            double thicknessFromLayers = GetThicknessFromCompoundStructure(floor);
            if (thicknessFromLayers > 0)
                return thicknessFromLayers;

            // Priority 2: Top-face / bottom-face distance
            double thicknessFromFaces = GetThicknessFromFaceDistance(floor);
            if (thicknessFromFaces > 0)
                return thicknessFromFaces;

            // Priority 3: Solid bounding-box height
            double thicknessFromBBox = GetThicknessFromSolidHeight(floor);
            if (thicknessFromBBox > 0)
                return thicknessFromBBox;

            Log($"Floor {floor.Id.Value}: Could not determine thickness – defaulting to 0 mm.");
            return 0;
        }

        /// <summary>
        /// Reads the <see cref="CompoundStructure"/> from the floor's type
        /// and sums every layer width. Returns the total in mm (rounded)
        /// or 0 if the structure is unavailable.
        /// </summary>
        private double GetThicknessFromCompoundStructure(Floor floor)
        {
            try
            {
                FloorType floorType = floor.FloorType;
                CompoundStructure compoundStructure = floorType.GetCompoundStructure();

                if (compoundStructure == null)
                    return 0;

                double totalWidthFeet = 0;
                int layerCount = compoundStructure.LayerCount;

                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    totalWidthFeet += compoundStructure.GetLayerWidth(layerIndex);
                }

                if (totalWidthFeet <= 0)
                    return 0;

                double thicknessMm = totalWidthFeet * FeetToMillimeters;
                return Math.Round(thicknessMm, 0);
            }
            catch (Exception ex)
            {
                Log($"CompoundStructure thickness failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Measures the projected distance between the centres of the top
        /// and bottom faces along the top-face normal direction.
        /// Returns the thickness in mm (rounded) or 0 on failure.
        /// </summary>
        private double GetThicknessFromFaceDistance(Floor floor)
        {
            try
            {
                Solid floorSolid = GetFloorSolid(floor);
                if (floorSolid == null)
                    return 0;

                Face topFace = FindBestTopFace(floorSolid);
                Face bottomFace = FindBottomFace(floorSolid);

                if (topFace == null || bottomFace == null)
                    return 0;

                XYZ topCenter = GetFaceCenter(topFace);
                XYZ bottomCenter = GetFaceCenter(bottomFace);

                if (topCenter == null || bottomCenter == null)
                    return 0;

                // Project the displacement onto the top-face normal
                XYZ topNormal = ComputeFaceAverageNormal(topFace);
                if (topNormal == null)
                    return 0;

                double distanceFeet = Math.Abs(
                    (topCenter - bottomCenter).DotProduct(topNormal));

                double thicknessMm = distanceFeet * FeetToMillimeters;
                return Math.Round(thicknessMm, 0);
            }
            catch (Exception ex)
            {
                Log($"Face-distance thickness failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Last-resort fallback: uses the Z-extent of the floor element's
        /// bounding box as an approximation of thickness.
        /// Returns the thickness in mm (rounded) or 0 on failure.
        /// </summary>
        private double GetThicknessFromSolidHeight(Floor floor)
        {
            try
            {
                BoundingBoxXYZ boundingBox = floor.get_BoundingBox(null);
                if (boundingBox == null)
                    return 0;

                double heightFeet = boundingBox.Max.Z - boundingBox.Min.Z;
                double thicknessMm = heightFeet * FeetToMillimeters;
                return Math.Round(thicknessMm, 0);
            }
            catch (Exception ex)
            {
                Log($"Solid-height thickness failed: {ex.Message}");
                return 0;
            }
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Face Center
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates the centroid of a face.
        /// Primary method: triangulate the face into a mesh and compute
        /// the area-weighted average of all triangle centroids.
        /// Fallback: evaluate the face at the midpoint of its UV domain.
        /// Does <b>not</b> use BoundingBox centre.
        /// </summary>
        public XYZ GetFaceCenter(Face face)
        {
            // Primary: area-weighted centroid from mesh triangulation
            XYZ meshCentroid = ComputeCentroidFromMesh(face);
            if (meshCentroid != null)
                return meshCentroid;

            // Fallback: UV midpoint evaluation
            XYZ uvCentroid = ComputeCentroidFromUv(face);
            if (uvCentroid != null)
                return uvCentroid;

            Log("GetFaceCenter: Both mesh and UV methods failed.");
            return null;
        }

        /// <summary>
        /// Triangulates the face and returns the area-weighted centroid
        /// of all mesh triangles:
        ///   centroid = Σ(tri_centroid × tri_area) / Σ(tri_area)
        /// Correctly accounts for holes (inner loops) because the mesh
        /// only covers the visible surface.
        /// </summary>
        private XYZ ComputeCentroidFromMesh(Face face)
        {
            try
            {
                Mesh mesh = face.Triangulate(0.5);
                if (mesh == null || mesh.NumTriangles == 0)
                    return null;

                XYZ weightedSum = XYZ.Zero;
                double totalArea = 0;

                for (int triangleIndex = 0; triangleIndex < mesh.NumTriangles; triangleIndex++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(triangleIndex);

                    XYZ vertex0 = triangle.get_Vertex(0);
                    XYZ vertex1 = triangle.get_Vertex(1);
                    XYZ vertex2 = triangle.get_Vertex(2);

                    // Centroid of the triangle
                    XYZ triangleCentroid = (vertex0 + vertex1 + vertex2).Divide(3.0);

                    // Area via cross product: |edge1 × edge2| / 2
                    XYZ edge1 = vertex1 - vertex0;
                    XYZ edge2 = vertex2 - vertex0;
                    double triangleArea = edge1.CrossProduct(edge2).GetLength() / 2.0;

                    weightedSum = weightedSum + triangleCentroid.Multiply(triangleArea);
                    totalArea += triangleArea;
                }

                if (totalArea < 1e-10)
                    return null;

                return weightedSum.Divide(totalArea);
            }
            catch (Exception ex)
            {
                Log($"Mesh centroid failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Evaluates the face at the midpoint of its UV bounding box.
        /// Simple fallback when triangulation is unavailable.
        /// </summary>
        private XYZ ComputeCentroidFromUv(Face face)
        {
            try
            {
                BoundingBoxUV uvBounds = face.GetBoundingBox();
                UV midUv = new UV(
                    (uvBounds.Min.U + uvBounds.Max.U) / 2.0,
                    (uvBounds.Min.V + uvBounds.Max.V) / 2.0);

                return face.Evaluate(midUv);
            }
            catch (Exception ex)
            {
                Log($"UV centroid failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Void Detection
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds <see cref="FamilyInstance"/> elements whose geometry
        /// intersects the floor's solid.
        /// Uses <see cref="ElementIntersectsSolidFilter"/> for accurate
        /// geometric testing – does <b>not</b> rely on BoundingBox alone.
        /// Only scans categories listed in
        /// <see cref="CreateSlabAnnotateOptions.VoidCategories"/>.
        /// </summary>
        public IList<FamilyInstance> FindVoidFamilies(Floor floor)
        {
            List<FamilyInstance> voidFamilies = new List<FamilyInstance>();

            try
            {
                Solid floorSolid = GetFloorSolid(floor);
                if (floorSolid == null || floorSolid.Volume <= 0)
                {
                    Log($"Floor {floor.Id.Value}: No valid solid for void detection.");
                    return voidFamilies;
                }

                if (_options.VoidCategories == null || _options.VoidCategories.Count == 0)
                    return voidFamilies;

                // Build filters
                ElementMulticategoryFilter categoryFilter =
                    new ElementMulticategoryFilter(_options.VoidCategories);
                ElementIntersectsSolidFilter intersectFilter =
                    new ElementIntersectsSolidFilter(floorSolid);

                FilteredElementCollector collector = new FilteredElementCollector(_document)
                    .WherePasses(categoryFilter)
                    .WhereElementIsNotElementType()
                    .WherePasses(intersectFilter);

                foreach (Element element in collector)
                {
                    // Safety: skip the floor itself (category mismatch should
                    // already prevent this, but belt-and-braces)
                    if (element.Id == floor.Id)
                        continue;

                    if (element is FamilyInstance familyInstance)
                    {
                        voidFamilies.Add(familyInstance);
                    }
                }

                Log($"Floor {floor.Id.Value}: Found {voidFamilies.Count} void family(ies).");
            }
            catch (Exception ex)
            {
                Log($"Void detection failed for Floor {floor.Id.Value}: {ex.Message}");
            }

            return voidFamilies;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Annotation Placement Logic
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the list of <see cref="AnnotationPlacement"/> objects
        /// that describe where annotations should be created.
        /// Delegates to a specialised method based on the void count:
        /// <list type="bullet">
        ///   <item>0 voids – single annotation at face centroid</item>
        ///   <item>1 void  – boolean difference, one annotation per region</item>
        ///   <item>N voids – iterative boolean difference, one annotation per region</item>
        /// </list>
        /// </summary>
        public IList<AnnotationPlacement> BuildAnnotationPlacements(
            Floor floor, Face topFace, IList<FamilyInstance> voidFamilies)
        {
            if (voidFamilies == null || voidFamilies.Count == 0)
            {
                return BuildPlacementsNoVoid(topFace);
            }

            if (voidFamilies.Count == 1)
            {
                return BuildPlacementsWithSingleVoid(floor, voidFamilies[0], topFace);
            }

            return BuildPlacementsWithMultipleVoids(floor, voidFamilies, topFace);
        }

        /// <summary>
        /// No voids detected – creates a single placement at the centroid
        /// of the top face.
        /// </summary>
        private IList<AnnotationPlacement> BuildPlacementsNoVoid(Face topFace)
        {
            List<AnnotationPlacement> placements = new List<AnnotationPlacement>();

            XYZ center = GetFaceCenter(topFace);
            if (center != null)
            {
                placements.Add(new AnnotationPlacement
                {
                    HostFace = topFace,
                    Point = center,
                    Direction = ComputeFaceDirection(topFace)
                });
            }

            return placements;
        }

        /// <summary>
        /// Exactly one void: performs a boolean difference
        /// (floor − void) and creates one placement per resulting
        /// top-face region.
        /// Falls back to bounding-box splitting if the boolean
        /// operation fails.
        /// </summary>
        private IList<AnnotationPlacement> BuildPlacementsWithSingleVoid(
            Floor floor, FamilyInstance voidFamily, Face topFace)
        {
            try
            {
                Solid floorSolid = GetFloorSolidForBoolean(floor);
                Solid voidSolid = GetElementSolid(voidFamily);

                if (floorSolid == null || voidSolid == null)
                {
                    Log("Single void: cannot obtain solids – using BoundingBox fallback.");
                    return FallbackSplitByBoundingBox(floor, voidFamily, topFace);
                }

                Solid differenceSolid = PerformBooleanDifference(floorSolid, voidSolid);
                if (differenceSolid == null)
                {
                    Log("Single void: boolean difference failed – using BoundingBox fallback.");
                    return FallbackSplitByBoundingBox(floor, voidFamily, topFace);
                }

                return BuildPlacementsFromBooleanResult(differenceSolid, topFace);
            }
            catch (Exception ex)
            {
                Log($"Single void processing error: {ex.Message}");
                return FallbackSplitByBoundingBox(floor, voidFamily, topFace);
            }
        }

        /// <summary>
        /// Multiple voids: iteratively subtracts each void solid from the
        /// floor solid, then creates one placement per remaining top-face
        /// region. If all boolean operations fail, falls back to a single
        /// placement at the original face centroid.
        /// </summary>
        private IList<AnnotationPlacement> BuildPlacementsWithMultipleVoids(
            Floor floor, IList<FamilyInstance> voidFamilies, Face topFace)
        {
            try
            {
                Solid remainingSolid = GetFloorSolidForBoolean(floor);
                if (remainingSolid == null)
                {
                    Log("Multiple voids: cannot get floor solid – falling back to no-void placement.");
                    return BuildPlacementsNoVoid(topFace);
                }

                // Subtract each void one by one
                foreach (FamilyInstance voidFamily in voidFamilies)
                {
                    try
                    {
                        Solid voidSolid = GetElementSolid(voidFamily);
                        if (voidSolid == null || voidSolid.Volume <= 0)
                            continue;

                        Solid differenceResult = PerformBooleanDifference(remainingSolid, voidSolid);

                        if (differenceResult != null && differenceResult.Volume > 0)
                        {
                            remainingSolid = differenceResult;
                        }
                    }
                    catch (Exception voidEx)
                    {
                        Log($"Boolean with void {voidFamily.Id.Value} skipped: {voidEx.Message}");
                    }
                }

                return BuildPlacementsFromBooleanResult(remainingSolid, topFace);
            }
            catch (Exception ex)
            {
                Log($"Multiple void processing error: {ex.Message}");
                return BuildPlacementsNoVoid(topFace);
            }
        }

        /// <summary>
        /// Given a solid produced by boolean operations, finds every
        /// top-facing face, computes its centroid, and returns one
        /// <see cref="AnnotationPlacement"/> per region.
        /// All placements are hosted on the original floor top face.
        /// Duplicate placements (within tolerance) are removed.
        /// </summary>
        private IList<AnnotationPlacement> BuildPlacementsFromBooleanResult(
            Solid booleanResultSolid, Face originalTopFace)
        {
            List<AnnotationPlacement> placements = new List<AnnotationPlacement>();

            IList<Face> regionTopFaces = FindAllTopFaces(booleanResultSolid);

            // If no top faces survived the boolean, fall back to original
            if (regionTopFaces.Count == 0)
            {
                return BuildPlacementsNoVoid(originalTopFace);
            }

            XYZ faceDirection = ComputeFaceDirection(originalTopFace);

            foreach (Face regionFace in regionTopFaces)
            {
                XYZ regionCentroid = GetFaceCenter(regionFace);
                if (regionCentroid == null)
                    continue;

                placements.Add(new AnnotationPlacement
                {
                    HostFace = originalTopFace,
                    Point = regionCentroid,
                    Direction = faceDirection
                });
            }

            return RemoveDuplicatePlacements(placements);
        }

        /// <summary>
        /// Removes placements that are closer together than
        /// <see cref="DuplicateDistanceTolerance"/> to prevent
        /// overlapping annotations.
        /// </summary>
        private IList<AnnotationPlacement> RemoveDuplicatePlacements(
            List<AnnotationPlacement> placements)
        {
            List<AnnotationPlacement> uniquePlacements = new List<AnnotationPlacement>();

            foreach (AnnotationPlacement candidate in placements)
            {
                bool isDuplicate = false;

                foreach (AnnotationPlacement existing in uniquePlacements)
                {
                    if (candidate.Point.DistanceTo(existing.Point) < DuplicateDistanceTolerance)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    uniquePlacements.Add(candidate);
                }
            }

            return uniquePlacements;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Boolean Operations
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes <c>solidA − solidB</c> using
        /// <see cref="BooleanOperationsUtils.ExecuteBooleanOperation"/>.
        /// Returns <c>null</c> when the operation fails or produces an
        /// empty / degenerate solid.
        /// </summary>
        private Solid PerformBooleanDifference(Solid solidA, Solid solidB)
        {
            try
            {
                Solid resultSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solidA, solidB, BooleanOperationsType.Difference);

                if (resultSolid != null && resultSolid.Volume > 0 && resultSolid.Faces.Size > 0)
                {
                    return resultSolid;
                }

                Log("Boolean difference produced an empty or invalid solid.");
                return null;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                Log($"Boolean InvalidOperationException: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Log($"Boolean difference failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fallback when boolean operations fail for a single void.
        /// Splits the floor into two halves along the longer axis
        /// at the void's bounding-box centre, and returns one
        /// placement per half.
        /// </summary>
        private IList<AnnotationPlacement> FallbackSplitByBoundingBox(
            Floor floor, FamilyInstance voidFamily, Face topFace)
        {
            List<AnnotationPlacement> placements = new List<AnnotationPlacement>();

            try
            {
                BoundingBoxXYZ floorBBox = floor.get_BoundingBox(null);
                BoundingBoxXYZ voidBBox = voidFamily.get_BoundingBox(null);

                if (floorBBox == null || voidBBox == null)
                {
                    Log("FallbackSplit: missing bounding box – reverting to no-void placement.");
                    return BuildPlacementsNoVoid(topFace);
                }

                XYZ floorCenter = (floorBBox.Min + floorBBox.Max).Divide(2.0);
                XYZ faceDirection = ComputeFaceDirection(topFace);

                double lengthX = floorBBox.Max.X - floorBBox.Min.X;
                double lengthY = floorBBox.Max.Y - floorBBox.Min.Y;

                // Split along the longer axis of the floor
                if (lengthX >= lengthY)
                {
                    double leftX = (floorBBox.Min.X + voidBBox.Min.X) / 2.0;
                    double rightX = (floorBBox.Max.X + voidBBox.Max.X) / 2.0;

                    placements.Add(CreatePlacement(
                        topFace,
                        new XYZ(leftX, floorCenter.Y, floorCenter.Z),
                        faceDirection));

                    placements.Add(CreatePlacement(
                        topFace,
                        new XYZ(rightX, floorCenter.Y, floorCenter.Z),
                        faceDirection));
                }
                else
                {
                    double bottomY = (floorBBox.Min.Y + voidBBox.Min.Y) / 2.0;
                    double topY = (floorBBox.Max.Y + voidBBox.Max.Y) / 2.0;

                    placements.Add(CreatePlacement(
                        topFace,
                        new XYZ(floorCenter.X, bottomY, floorCenter.Z),
                        faceDirection));

                    placements.Add(CreatePlacement(
                        topFace,
                        new XYZ(floorCenter.X, topY, floorCenter.Z),
                        faceDirection));
                }
            }
            catch (Exception ex)
            {
                Log($"FallbackSplit error: {ex.Message}");
                return BuildPlacementsNoVoid(topFace);
            }

            return placements;
        }

        /// <summary>
        /// Convenience factory for creating an <see cref="AnnotationPlacement"/>.
        /// </summary>
        private AnnotationPlacement CreatePlacement(Face hostFace, XYZ point, XYZ direction)
        {
            return new AnnotationPlacement
            {
                HostFace = hostFace,
                Point = point,
                Direction = direction
            };
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Annotation Creation
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Places an annotation family instance on the specified host face.
        /// Attempts face-based placement first (required for families that
        /// need a host). If that fails, falls back to point-based placement
        /// with <see cref="StructuralType.NonStructural"/>.
        /// The <paramref name="referenceDirection"/> controls the rotation
        /// of the family on the face plane, calculated from the face normal
        /// by <see cref="ComputeFaceDirection"/>.
        /// </summary>
        public FamilyInstance CreateAnnotation(
            Face hostFace,
            XYZ placementPoint,
            XYZ referenceDirection,
            FamilySymbol symbol)
        {
            // Attempt 1: face-based placement (honours host and rotation)
            try
            {
                FamilyInstance faceInstance = _document.Create.NewFamilyInstance(
                    hostFace, placementPoint, referenceDirection, symbol);

                if (faceInstance != null)
                {
                    Log($"Created face-hosted annotation at " +
                        $"({placementPoint.X:F3}, {placementPoint.Y:F3}, {placementPoint.Z:F3})");
                    return faceInstance;
                }
            }
            catch (Exception faceEx)
            {
                Log($"Face-based placement failed: {faceEx.Message} – trying point-based.");
            }

            // Attempt 2: point-based placement (fallback)
            try
            {
                FamilyInstance pointInstance = _document.Create.NewFamilyInstance(
                    placementPoint, symbol, StructuralType.NonStructural);

                Log($"Created point-based annotation at " +
                    $"({placementPoint.X:F3}, {placementPoint.Y:F3}, {placementPoint.Z:F3})");
                return pointInstance;
            }
            catch (Exception pointEx)
            {
                Log($"Point-based placement also failed: {pointEx.Message}");
                throw;
            }
        }

        /// <summary>
        /// Computes a reference direction for placing a family on a face.
        /// Projects the global X-axis onto the face plane. If the X-axis
        /// is nearly parallel to the face normal, uses the global Y-axis
        /// instead. This ensures the family rotates correctly on sloped
        /// floors without using a fixed rotation angle.
        /// </summary>
        public XYZ ComputeFaceDirection(Face face)
        {
            try
            {
                XYZ faceNormal = ComputeFaceAverageNormal(face);
                if (faceNormal == null)
                    return XYZ.BasisX;

                // Project global X-axis onto the face plane:
                //   projected = X − (X · N) × N
                double dotX = faceNormal.DotProduct(XYZ.BasisX);
                XYZ projectedX = XYZ.BasisX - dotX * faceNormal;

                if (projectedX.GetLength() > 1e-6)
                    return projectedX.Normalize();

                // X-axis is parallel to the normal → use Y-axis instead
                double dotY = faceNormal.DotProduct(XYZ.BasisY);
                XYZ projectedY = XYZ.BasisY - dotY * faceNormal;

                if (projectedY.GetLength() > 1e-6)
                    return projectedY.Normalize();

                // Extreme fallback
                return XYZ.BasisX;
            }
            catch
            {
                return XYZ.BasisX;
            }
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Parameter Assignment
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets the thickness parameter on an annotation family instance.
        /// <list type="bullet">
        ///   <item>Double (length) – converts mm to Revit internal units (feet)</item>
        ///   <item>String – writes in the format <c>"150 mm"</c></item>
        ///   <item>Integer – rounds to the nearest whole number</item>
        /// </list>
        /// Silently returns if the parameter does not exist or is read-only.
        /// Never throws an exception.
        /// </summary>
        public void SetThicknessParameter(FamilyInstance annotation, double thicknessMm)
        {
            try
            {
                Parameter thicknessParam = annotation.LookupParameter(_options.ThicknessParameterName);

                if (thicknessParam == null)
                {
                    Log($"Parameter '{_options.ThicknessParameterName}' not found – skipping.");
                    return;
                }

                if (thicknessParam.IsReadOnly)
                {
                    Log($"Parameter '{_options.ThicknessParameterName}' is read-only – skipping.");
                    return;
                }

                switch (thicknessParam.StorageType)
                {
                    case StorageType.Double:
                        SetParameterAsDouble(thicknessParam, thicknessMm);
                        break;

                    case StorageType.String:
                        SetParameterAsString(thicknessParam, thicknessMm);
                        break;

                    case StorageType.Integer:
                        thicknessParam.Set((int)Math.Round(thicknessMm));
                        break;

                    default:
                        Log($"Unsupported StorageType '{thicknessParam.StorageType}' " +
                            $"for parameter '{_options.ThicknessParameterName}'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Parameter assignment must never crash the command
                Log($"SetThicknessParameter failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts millimetres to Revit internal units and sets the parameter.
        /// </summary>
        private void SetParameterAsDouble(Parameter parameter, double valueMm)
        {
            double internalValue = UnitUtils.ConvertToInternalUnits(
                valueMm, UnitTypeId.Millimeters);
            parameter.Set(internalValue);
        }

        /// <summary>
        /// Formats the thickness as <c>"150 mm"</c> and sets the parameter.
        /// </summary>
        private void SetParameterAsString(Parameter parameter, double valueMm)
        {
            int roundedValue = (int)Math.Round(valueMm);
            string formattedValue = $"{roundedValue} mm";
            parameter.Set(formattedValue);
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Logging
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes a timestamped debug message to the Visual Studio
        /// Output window. Does not use Console.
        /// </summary>
        private void Log(string message)
        {
            Debug.WriteLine($"[SlabAnnotate {DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        #endregion
    }
}
