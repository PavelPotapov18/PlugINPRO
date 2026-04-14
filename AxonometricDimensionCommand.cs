using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class AxonometricDimensionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                DimensionSettingsData settings = DimensionSettingsData.Load();

                View activeView = doc.ActiveView;
                if (!(activeView is View3D))
                {
                    TaskDialog.Show("Ошибка", "Пожалуйста, активируйте 3D вид (аксонометрию) для простановки размеров.");
                    return Result.Cancelled;
                }

                DimensionType dimensionType = null;
                if (!string.IsNullOrEmpty(settings.DimensionTypeId))
                {
                    ElementId typeId = new ElementId(Convert.ToInt32(settings.DimensionTypeId));
                    dimensionType = doc.GetElement(typeId) as DimensionType;
                }

                if (dimensionType == null)
                {
                    dimensionType = new FilteredElementCollector(doc)
                        .OfClass(typeof(DimensionType))
                        .FirstOrDefault() as DimensionType;
                }

                ISelectionFilter ductAndPipeFilter = new DuctAndPipeSelectionFilter();

                IList<Reference> selectedRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    ductAndPipeFilter,
                    "Выберите трубы/воздуховоды и фитинги/опоры для образмеривания"
                );

                if (selectedRefs.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Элементы не выбраны.");
                    return Result.Cancelled;
                }

                List<Element> selectedMepElements = new List<Element>();
                List<FamilyInstance> selectedFittings = new List<FamilyInstance>();

                foreach (Reference refElem in selectedRefs)
                {
                    Element element = doc.GetElement(refElem);
                    if ((element is Pipe || element is Duct) && element.Category != null)
                    {
                        selectedMepElements.Add(element);
                    }
                    else if (element is FamilyInstance fitting && fitting.Category != null)
                    {
                        selectedFittings.Add(fitting);
                    }
                }

                if (selectedMepElements.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Среди выбранных элементов не найдено труб или воздуховодов.");
                    return Result.Cancelled;
                }

                using (Transaction trans = new Transaction(doc, "Простановка размеров на аксонометрии"))
                {
                    trans.Start();

                    int dimensionsCreated = 0;
                    int errorsCount = 0;
                    string errorMessage = "";
                    Dictionary<string, SketchPlane> sketchPlaneCache = new Dictionary<string, SketchPlane>();

                    foreach (Element element in selectedMepElements)
                    {
                        bool success = false;

                        if (settings.Mode == DimensionMode.ByPipeLength)
                        {
                            if (element is Pipe pipe)
                                success = CreateDimensionForPipe(doc, pipe, activeView, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                            else if (element is Duct duct)
                                success = CreateDimensionForDuct(doc, duct, activeView, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                        }
                        else if (settings.Mode == DimensionMode.ByFittingCenter)
                        {
                            if (element is Pipe pipe)
                                success = CreateDimensionFromFittingCentersForPipe(doc, pipe, activeView, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                            else if (element is Duct duct)
                                success = CreateDimensionFromFittingCentersForDuct(doc, duct, activeView, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                        }
                        else if (settings.Mode == DimensionMode.BySupportsToPipe)
                        {
                            if (element is Pipe pipe)
                                success = CreateDimensionBetweenSupportsToPipe(doc, pipe, activeView, dimensionType, settings, selectedFittings, sketchPlaneCache, ref errorMessage);
                            else if (element is Duct duct)
                                success = CreateDimensionBetweenSupportsToDuct(doc, duct, activeView, dimensionType, settings, selectedFittings, sketchPlaneCache, ref errorMessage);
                        }
                        else if (settings.Mode == DimensionMode.BySupportsToFittings)
                        {
                            if (element is Pipe pipe)
                                success = CreateDimensionBetweenSupportsToFittingsForPipe(doc, pipe, activeView, dimensionType, settings, selectedFittings, sketchPlaneCache, ref errorMessage);
                            else if (element is Duct duct)
                                success = CreateDimensionBetweenSupportsToFittingsForDuct(doc, duct, activeView, dimensionType, settings, selectedFittings, sketchPlaneCache, ref errorMessage);
                        }

                        if (success) dimensionsCreated++;
                        else errorsCount++;
                    }

                    trans.Commit();

                    string resultMessage = $"Создано размеров: {dimensionsCreated} из {selectedMepElements.Count} элементов";
                    if (errorsCount > 0 && !string.IsNullOrEmpty(errorMessage))
                    {
                        resultMessage += $"\n\nОшибки:\n{errorMessage}";
                    }

                    TaskDialog.Show("Результат", resultMessage);
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Произошла ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        // ====================================================================
        // РЕЖИМ 1: РАЗМЕР ПО ДЛИНЕ ТРУБЫ/ВОЗДУХОВОДА
        // ====================================================================
        private bool CreateDimensionForPipe(Document doc, Pipe pipe, View view, DimensionType dimensionType,
            DimensionSettingsData settings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Труба {pipe.Id}: не удалось получить LocationCurve"; return false; }
                Line pipeAxis = locationCurve.Curve as Line;
                if (pipeAxis == null) { errorMessage += $"\n- Труба {pipe.Id}: ось не является прямой линией"; return false; }
                XYZ startPoint = pipeAxis.GetEndPoint(0);
                XYZ endPoint = pipeAxis.GetEndPoint(1);
                if (startPoint.DistanceTo(endPoint) < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: нулевая длина"; return false; }

                XYZ direction = (endPoint - startPoint).Normalize();
                XYZ up = XYZ.BasisZ;
                if (Math.Abs(direction.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = direction.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                Line dimensionLine = Line.CreateBound(startPoint + offset, endPoint + offset);
                XYZ normal = direction.CrossProduct(offsetDir).Normalize();
                string normalKey = $"{Math.Round(normal.X, 6)},{Math.Round(normal.Y, 6)},{Math.Round(normal.Z, 6)}";

                SketchPlane sketchPlane;
                if (sketchPlaneCache.ContainsKey(normalKey)) sketchPlane = sketchPlaneCache[normalKey];
                else { Plane plane = Plane.CreateByNormalAndOrigin(normal, startPoint); sketchPlane = SketchPlane.Create(doc, plane); sketchPlaneCache[normalKey] = sketchPlane; }

                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;

                Reference startRef = null, endRef = null;
                try { startRef = pipeAxis.GetEndPointReference(0); endRef = pipeAxis.GetEndPointReference(1); } catch { }
                if (startRef == null || endRef == null)
                {
                    List<Reference> endRefs = GetPipeEndReferences(pipe, startPoint, endPoint);
                    if (endRefs == null || endRefs.Count < 2) { errorMessage += $"\n- Труба {pipe.Id}: не удалось получить References"; view.SketchPlane = originalSketchPlane; return false; }
                    startRef = endRefs[0]; endRef = endRefs[1];
                }

                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(startRef); refArray.Append(endRef);
                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                if (dimensionType != null) dimension.DimensionType = dimensionType;
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Труба {pipe.Id}: {ex.Message}"; return false; }
        }

        private bool CreateDimensionForDuct(Document doc, Duct duct, View view, DimensionType dimensionType,
            DimensionSettingsData settings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Воздуховод {duct.Id}: не удалось получить LocationCurve"; return false; }
                Line ductAxis = locationCurve.Curve as Line;
                if (ductAxis == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ось не является прямой линией"; return false; }
                XYZ startPoint = ductAxis.GetEndPoint(0);
                XYZ endPoint = ductAxis.GetEndPoint(1);
                if (startPoint.DistanceTo(endPoint) < 0.001) { errorMessage += $"\n- Воздуховод {duct.Id}: нулевая длина"; return false; }

                XYZ direction = (endPoint - startPoint).Normalize();
                XYZ up = XYZ.BasisZ;
                if (Math.Abs(direction.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = direction.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                Line dimensionLine = Line.CreateBound(startPoint + offset, endPoint + offset);
                XYZ normal = direction.CrossProduct(offsetDir).Normalize();
                string normalKey = $"{Math.Round(normal.X, 6)},{Math.Round(normal.Y, 6)},{Math.Round(normal.Z, 6)}";

                SketchPlane sketchPlane;
                if (sketchPlaneCache.ContainsKey(normalKey)) sketchPlane = sketchPlaneCache[normalKey];
                else { Plane plane = Plane.CreateByNormalAndOrigin(normal, startPoint); sketchPlane = SketchPlane.Create(doc, plane); sketchPlaneCache[normalKey] = sketchPlane; }

                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;

                Reference startRef = null, endRef = null;
                try { startRef = ductAxis.GetEndPointReference(0); endRef = ductAxis.GetEndPointReference(1); } catch { }
                if (startRef == null || endRef == null)
                {
                    List<Reference> endRefs = GetDuctEndReferences(duct, startPoint, endPoint);
                    if (endRefs == null || endRefs.Count < 2) { errorMessage += $"\n- Воздуховод {duct.Id}: не удалось получить References"; view.SketchPlane = originalSketchPlane; return false; }
                    startRef = endRefs[0]; endRef = endRefs[1];
                }

                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(startRef); refArray.Append(endRef);
                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                if (dimensionType != null) dimension.DimensionType = dimensionType;
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Воздуховод {duct.Id}: {ex.Message}"; return false; }
        }

        // ====================================================================
        // РЕЖИМ 2: РАЗМЕР ОТ ЦЕНТРА ФИТИНГА
        // ====================================================================
        private bool CreateDimensionFromFittingCentersForPipe(Document doc, Pipe pipe, View view, DimensionType dimensionType,
            DimensionSettingsData settings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line pipeAxis = locationCurve.Curve as Line;
                if (pipeAxis == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ pipeStart = pipeAxis.GetEndPoint(0);
                XYZ pipeEnd = pipeAxis.GetEndPoint(1);
                if (pipeStart.DistanceTo(pipeEnd) < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: ❌ нулевая длина"; return false; }

                ConnectorManager connectorManager = pipe.ConnectorManager;
                if (connectorManager == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ нет ConnectorManager"; return false; }

                FamilyInstance startFitting = null, endFitting = null;
                foreach (Connector conn in connectorManager.Connectors)
                {
                    if (conn.Origin.DistanceTo(pipeStart) < 0.01 && conn.IsConnected)
                    {
                        foreach (Connector connectedConn in conn.AllRefs)
                        {
                            Element connectedElement = connectedConn.Owner;
                            if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Pipe Fittings")
                            { startFitting = fitting; break; }
                        }
                    }
                    if (conn.Origin.DistanceTo(pipeEnd) < 0.01 && conn.IsConnected)
                    {
                        foreach (Connector connectedConn in conn.AllRefs)
                        {
                            Element connectedElement = connectedConn.Owner;
                            if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Pipe Fittings")
                            { endFitting = fitting; break; }
                        }
                    }
                }

                if (startFitting == null && endFitting == null)
                { return CreateDimensionForPipe(doc, pipe, view, dimensionType, settings, sketchPlaneCache, ref errorMessage); }

                XYZ pointA = pipeStart, pointB = pipeEnd;
                string sizeDescription = "";

                if (startFitting != null && endFitting != null)
                {
                    XYZ centerA = GetFittingCenter(startFitting);
                    XYZ centerB = GetFittingCenter(endFitting);
                    if (centerA == null || centerB == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центры фитингов"; return false; }
                    pointA = centerA; pointB = centerB;
                }
                else if (startFitting != null)
                {
                    XYZ center = GetFittingCenter(startFitting);
                    if (center == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центр фитинга"; return false; }
                    pointA = center; pointB = pipeEnd;
                }
                else if (endFitting != null)
                {
                    XYZ center = GetFittingCenter(endFitting);
                    if (center == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центр фитинга"; return false; }
                    pointA = pipeStart; pointB = center;
                }

                double distance = pointA.DistanceTo(pointB);
                if (distance < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: ⚠️ точки совпадают"; return false; }

                XYZ direction = (pipeEnd - pipeStart).Normalize();
                XYZ up = XYZ.BasisZ;
                if (Math.Abs(direction.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = direction.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                Line modelLine = Line.CreateBound(pointA, pointB);
                XYZ normal = direction.CrossProduct(offsetDir).Normalize();
                Plane plane = Plane.CreateByNormalAndOrigin(normal, pointA);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;
                ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                if (helperLine == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось создать ModelLine"; view.SketchPlane = originalSketchPlane; return false; }
                Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                if (dimensionType != null) dimension.DimensionType = dimensionType;
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Труба {pipe.Id}: 💥 {ex.Message}"; return false; }
        }

        private bool CreateDimensionFromFittingCentersForDuct(Document doc, Duct duct, View view, DimensionType dimensionType,
            DimensionSettingsData settings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line ductAxis = locationCurve.Curve as Line;
                if (ductAxis == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ ductStart = ductAxis.GetEndPoint(0);
                XYZ ductEnd = ductAxis.GetEndPoint(1);
                if (ductStart.DistanceTo(ductEnd) < 0.001) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ нулевая длина"; return false; }

                ConnectorManager connectorManager = duct.ConnectorManager;
                if (connectorManager == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ нет ConnectorManager"; return false; }

                FamilyInstance startFitting = null, endFitting = null;
                foreach (Connector conn in connectorManager.Connectors)
                {
                    if (conn.Origin.DistanceTo(ductStart) < 0.01 && conn.IsConnected)
                    {
                        foreach (Connector connectedConn in conn.AllRefs)
                        {
                            Element connectedElement = connectedConn.Owner;
                            if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Duct Fittings")
                            { startFitting = fitting; break; }
                        }
                    }
                    if (conn.Origin.DistanceTo(ductEnd) < 0.01 && conn.IsConnected)
                    {
                        foreach (Connector connectedConn in conn.AllRefs)
                        {
                            Element connectedElement = connectedConn.Owner;
                            if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Duct Fittings")
                            { endFitting = fitting; break; }
                        }
                    }
                }

                if (startFitting == null && endFitting == null)
                { return CreateDimensionForDuct(doc, duct, view, dimensionType, settings, sketchPlaneCache, ref errorMessage); }

                XYZ pointA = ductStart, pointB = ductEnd;
                if (startFitting != null && endFitting != null)
                {
                    XYZ centerA = GetFittingCenter(startFitting);
                    XYZ centerB = GetFittingCenter(endFitting);
                    if (centerA == null || centerB == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центры фитингов"; return false; }
                    pointA = centerA; pointB = centerB;
                }
                else if (startFitting != null)
                {
                    XYZ center = GetFittingCenter(startFitting);
                    if (center == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центр фитинга"; return false; }
                    pointA = center; pointB = ductEnd;
                }
                else if (endFitting != null)
                {
                    XYZ center = GetFittingCenter(endFitting);
                    if (center == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центр фитинга"; return false; }
                    pointA = ductStart; pointB = center;
                }

                XYZ direction = (ductEnd - ductStart).Normalize();
                XYZ up = XYZ.BasisZ;
                if (Math.Abs(direction.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = direction.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                Line modelLine = Line.CreateBound(pointA, pointB);
                XYZ normal = direction.CrossProduct(offsetDir).Normalize();
                Plane plane = Plane.CreateByNormalAndOrigin(normal, pointA);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;
                ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                if (helperLine == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось создать ModelLine"; view.SketchPlane = originalSketchPlane; return false; }
                Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                if (dimensionType != null) dimension.DimensionType = dimensionType;
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Воздуховод {duct.Id}: 💥 {ex.Message}"; return false; }
        }

        // ====================================================================
        // РЕЖИМ 3: РАЗМЕРЫ МЕЖДУ ОПОРАМИ ДО ГРАНИЦ ТРУБЫ/ВОЗДУХОВОДА
        // ====================================================================
        private bool CreateDimensionBetweenSupportsToPipe(Document doc, Pipe pipe, View view, DimensionType dimensionType,
            DimensionSettingsData settings, List<FamilyInstance> selectedFittings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line pipeAxis = locationCurve.Curve as Line;
                if (pipeAxis == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ pipeStart = pipeAxis.GetEndPoint(0);
                XYZ pipeEnd = pipeAxis.GetEndPoint(1);
                if (pipeStart.DistanceTo(pipeEnd) < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: ❌ нулевая длина"; return false; }

                XYZ pipeDir = (pipeEnd - pipeStart).Normalize();
                double pipeLength = pipeStart.DistanceTo(pipeEnd);

                // Находим опоры на оси трубы
                List<FamilyInstance> supportsOnPipe = new List<FamilyInstance>();
                foreach (FamilyInstance fitting in selectedFittings)
                {
                    XYZ fittingCenter = GetFittingCenter(fitting);
                    if (fittingCenter == null) continue;
                    XYZ projection = pipeStart + pipeDir * (fittingCenter - pipeStart).DotProduct(pipeDir);
                    double distanceToAxis = fittingCenter.DistanceTo(projection);
                    double projectionValue = (fittingCenter - pipeStart).DotProduct(pipeDir);
                    if (distanceToAxis < 0.05 && projectionValue >= -0.05 && projectionValue <= pipeLength + 0.05)
                        supportsOnPipe.Add(fitting);
                }

                if (supportsOnPipe.Count == 0)
                    return CreateDimensionForPipe(doc, pipe, view, dimensionType, settings, sketchPlaneCache, ref errorMessage);

                var sortedSupports = supportsOnPipe.OrderBy(s =>
                {
                    XYZ center = GetFittingCenter(s);
                    if (center == null) return double.MaxValue;
                    return (center - pipeStart).DotProduct(pipeDir);
                }).ToList();

                // Точки: начало трубы + опоры + конец трубы
                List<XYZ> points = new List<XYZ>();
                points.Add(pipeStart);
                foreach (var support in sortedSupports)
                {
                    XYZ center = GetFittingCenter(support);
                    if (center != null) points.Add(center);
                }
                points.Add(pipeEnd);
                points = points.Distinct(new XYZComparer()).ToList();

                if (points.Count < 2) return false;

                // Создаем размеры
                XYZ up = XYZ.BasisZ;
                if (Math.Abs(pipeDir.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = pipeDir.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                XYZ normal = pipeDir.CrossProduct(offsetDir).Normalize();
                Plane plane = Plane.CreateByNormalAndOrigin(normal, points[0]);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ pointA = points[i];
                    XYZ pointB = points[i + 1];
                    if (pointA.DistanceTo(pointB) < 0.001) continue;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                    if (helperLine == null) continue;
                    Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                }
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Труба {pipe.Id}: 💥 {ex.Message}"; return false; }
        }

        private bool CreateDimensionBetweenSupportsToDuct(Document doc, Duct duct, View view, DimensionType dimensionType,
            DimensionSettingsData settings, List<FamilyInstance> selectedFittings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line ductAxis = locationCurve.Curve as Line;
                if (ductAxis == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ ductStart = ductAxis.GetEndPoint(0);
                XYZ ductEnd = ductAxis.GetEndPoint(1);
                if (ductStart.DistanceTo(ductEnd) < 0.001) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ нулевая длина"; return false; }

                XYZ ductDir = (ductEnd - ductStart).Normalize();
                double ductLength = ductStart.DistanceTo(ductEnd);

                List<FamilyInstance> supportsOnDuct = new List<FamilyInstance>();
                foreach (FamilyInstance fitting in selectedFittings)
                {
                    XYZ fittingCenter = GetFittingCenter(fitting);
                    if (fittingCenter == null) continue;
                    XYZ projection = ductStart + ductDir * (fittingCenter - ductStart).DotProduct(ductDir);
                    double distanceToAxis = fittingCenter.DistanceTo(projection);
                    double projectionValue = (fittingCenter - ductStart).DotProduct(ductDir);
                    if (distanceToAxis < 0.05 && projectionValue >= -0.05 && projectionValue <= ductLength + 0.05)
                        supportsOnDuct.Add(fitting);
                }

                if (supportsOnDuct.Count == 0)
                    return CreateDimensionForDuct(doc, duct, view, dimensionType, settings, sketchPlaneCache, ref errorMessage);

                var sortedSupports = supportsOnDuct.OrderBy(s =>
                {
                    XYZ center = GetFittingCenter(s);
                    if (center == null) return double.MaxValue;
                    return (center - ductStart).DotProduct(ductDir);
                }).ToList();

                List<XYZ> points = new List<XYZ>();
                points.Add(ductStart);
                foreach (var support in sortedSupports)
                {
                    XYZ center = GetFittingCenter(support);
                    if (center != null) points.Add(center);
                }
                points.Add(ductEnd);
                points = points.Distinct(new XYZComparer()).ToList();

                if (points.Count < 2) return false;

                XYZ up = XYZ.BasisZ;
                if (Math.Abs(ductDir.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                XYZ offsetDir = ductDir.CrossProduct(up).Normalize();
                XYZ offset = offsetDir * settings.OffsetDistance;
                XYZ normal = ductDir.CrossProduct(offsetDir).Normalize();
                Plane plane = Plane.CreateByNormalAndOrigin(normal, points[0]);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                SketchPlane originalSketchPlane = view.SketchPlane;
                view.SketchPlane = sketchPlane;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ pointA = points[i];
                    XYZ pointB = points[i + 1];
                    if (pointA.DistanceTo(pointB) < 0.001) continue;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                    if (helperLine == null) continue;
                    Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                }
                view.SketchPlane = originalSketchPlane;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Воздуховод {duct.Id}: 💥 {ex.Message}"; return false; }
        }

        // ====================================================================
        // РЕЖИМ 4: РАЗМЕРЫ МЕЖДУ ОПОРАМИ ДО ФИТИНГОВ (ИСПРАВЛЕННЫЙ)
        // ====================================================================
        private bool CreateDimensionBetweenSupportsToFittingsForPipe(Document doc, Pipe pipe, View view, DimensionType dimensionType,
            DimensionSettingsData settings, List<FamilyInstance> selectedFittings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line pipeAxis = locationCurve.Curve as Line;
                if (pipeAxis == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ pipeStart = pipeAxis.GetEndPoint(0);
                XYZ pipeEnd = pipeAxis.GetEndPoint(1);
                if (pipeStart.DistanceTo(pipeEnd) < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: ❌ нулевая длина"; return false; }

                XYZ pipeDir = (pipeEnd - pipeStart).Normalize();
                double pipeLength = pipeStart.DistanceTo(pipeEnd);

                // Находим опоры на оси трубы
                List<FamilyInstance> supportsOnPipe = new List<FamilyInstance>();
                foreach (FamilyInstance fitting in selectedFittings)
                {
                    XYZ fittingCenter = GetFittingCenter(fitting);
                    if (fittingCenter == null) continue;
                    XYZ projection = pipeStart + pipeDir * (fittingCenter - pipeStart).DotProduct(pipeDir);
                    double distanceToAxis = fittingCenter.DistanceTo(projection);
                    double projectionValue = (fittingCenter - pipeStart).DotProduct(pipeDir);
                    if (distanceToAxis < 0.05 && projectionValue >= -0.05 && projectionValue <= pipeLength + 0.05)
                        supportsOnPipe.Add(fitting);
                }

                // НАХОДИМ КОНЦЕВЫЕ ФИТИНГИ (всегда ищем, даже если есть опоры)
                ConnectorManager connectorManager = pipe.ConnectorManager;
                FamilyInstance startFitting = null, endFitting = null;
                if (connectorManager != null)
                {
                    foreach (Connector conn in connectorManager.Connectors)
                    {
                        if (conn.Origin.DistanceTo(pipeStart) < 0.01 && conn.IsConnected)
                        {
                            foreach (Connector connectedConn in conn.AllRefs)
                            {
                                Element connectedElement = connectedConn.Owner;
                                if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Pipe Fittings")
                                { startFitting = fitting; break; }
                            }
                        }
                        if (conn.Origin.DistanceTo(pipeEnd) < 0.01 && conn.IsConnected)
                        {
                            foreach (Connector connectedConn in conn.AllRefs)
                            {
                                Element connectedElement = connectedConn.Owner;
                                if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Pipe Fittings")
                                { endFitting = fitting; break; }
                            }
                        }
                    }
                }

                // ================================================================
                // ЕСЛИ ОПОР НЕТ - ДЕЛАЕМ РАЗМЕР МЕЖДУ ЦЕНТРАМИ ФИТИНГОВ (как в режиме 2)
                // ================================================================
                if (supportsOnPipe.Count == 0)
                {
                    errorMessage += $"\n- Труба {pipe.Id}: ℹ️ опоры не найдены, создаем размер между центрами фитингов";

                    // Если нет ни одного фитинга - просто размер по трубе
                    if (startFitting == null && endFitting == null)
                    {
                        return CreateDimensionForPipe(doc, pipe, view, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                    }

                    // Определяем точки для размера
                    XYZ pointA = pipeStart;
                    XYZ pointB = pipeEnd;

                    if (startFitting != null && endFitting != null)
                    {
                        XYZ centerA = GetFittingCenter(startFitting);
                        XYZ centerB = GetFittingCenter(endFitting);
                        if (centerA == null || centerB == null)
                        {
                            errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центры фитингов";
                            return false;
                        }
                        pointA = centerA;
                        pointB = centerB;
                    }
                    else if (startFitting != null)
                    {
                        XYZ center = GetFittingCenter(startFitting);
                        if (center == null)
                        {
                            errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центр фитинга";
                            return false;
                        }
                        pointA = center;
                        pointB = pipeEnd;
                    }
                    else if (endFitting != null)
                    {
                        XYZ center = GetFittingCenter(endFitting);
                        if (center == null)
                        {
                            errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось получить центр фитинга";
                            return false;
                        }
                        pointA = pipeStart;
                        pointB = center;
                    }

                    // Создаем размер между точками
                    double distance = pointA.DistanceTo(pointB);
                    if (distance < 0.001) { errorMessage += $"\n- Труба {pipe.Id}: ⚠️ точки совпадают"; return false; }

                    XYZ up = XYZ.BasisZ;
                    if (Math.Abs(pipeDir.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                    XYZ offsetDir = pipeDir.CrossProduct(up).Normalize();
                    XYZ offset = offsetDir * settings.OffsetDistance;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    XYZ normal = pipeDir.CrossProduct(offsetDir).Normalize();
                    Plane plane = Plane.CreateByNormalAndOrigin(normal, pointA);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    SketchPlane originalSketchPlane = view.SketchPlane;
                    view.SketchPlane = sketchPlane;
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                    if (helperLine == null) { errorMessage += $"\n- Труба {pipe.Id}: ❌ не удалось создать ModelLine"; view.SketchPlane = originalSketchPlane; return false; }
                    Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                    view.SketchPlane = originalSketchPlane;
                    return true;
                }

                // ================================================================
                // ЕСЛИ ОПОРЫ ЕСТЬ - ДЕЛАЕМ РАЗМЕРЫ МЕЖДУ ОПОРАМИ И ФИТИНГАМИ
                // ================================================================
                var sortedSupports = supportsOnPipe.OrderBy(s =>
                {
                    XYZ center = GetFittingCenter(s);
                    if (center == null) return double.MaxValue;
                    return (center - pipeStart).DotProduct(pipeDir);
                }).ToList();

                // Точки: начало (фитинг или конец трубы) + опоры + конец (фитинг или конец трубы)
                List<XYZ> points = new List<XYZ>();

                if (startFitting != null)
                {
                    XYZ center = GetFittingCenter(startFitting);
                    points.Add(center ?? pipeStart);
                }
                else points.Add(pipeStart);

                foreach (var support in sortedSupports)
                {
                    XYZ center = GetFittingCenter(support);
                    if (center != null) points.Add(center);
                }

                if (endFitting != null)
                {
                    XYZ center = GetFittingCenter(endFitting);
                    points.Add(center ?? pipeEnd);
                }
                else points.Add(pipeEnd);

                points = points.Distinct(new XYZComparer()).ToList();
                if (points.Count < 2) return false;

                XYZ up2 = XYZ.BasisZ;
                if (Math.Abs(pipeDir.DotProduct(up2)) > 0.99) up2 = XYZ.BasisX;
                XYZ offsetDir2 = pipeDir.CrossProduct(up2).Normalize();
                XYZ offset2 = offsetDir2 * settings.OffsetDistance;
                XYZ normal2 = pipeDir.CrossProduct(offsetDir2).Normalize();
                Plane plane2 = Plane.CreateByNormalAndOrigin(normal2, points[0]);
                SketchPlane sketchPlane2 = SketchPlane.Create(doc, plane2);
                SketchPlane originalSketchPlane2 = view.SketchPlane;
                view.SketchPlane = sketchPlane2;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ pointA = points[i];
                    XYZ pointB = points[i + 1];
                    if (pointA.DistanceTo(pointB) < 0.001) continue;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane2) as ModelLine;
                    if (helperLine == null) continue;
                    Line dimensionLine = Line.CreateBound(pointA + offset2, pointB + offset2);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                }
                view.SketchPlane = originalSketchPlane2;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Труба {pipe.Id}: 💥 {ex.Message}"; return false; }
        }

        private bool CreateDimensionBetweenSupportsToFittingsForDuct(Document doc, Duct duct, View view, DimensionType dimensionType,
            DimensionSettingsData settings, List<FamilyInstance> selectedFittings, Dictionary<string, SketchPlane> sketchPlaneCache, ref string errorMessage)
        {
            try
            {
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить LocationCurve"; return false; }
                Line ductAxis = locationCurve.Curve as Line;
                if (ductAxis == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ ось не является прямой линией"; return false; }
                XYZ ductStart = ductAxis.GetEndPoint(0);
                XYZ ductEnd = ductAxis.GetEndPoint(1);
                if (ductStart.DistanceTo(ductEnd) < 0.001) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ нулевая длина"; return false; }

                XYZ ductDir = (ductEnd - ductStart).Normalize();
                double ductLength = ductStart.DistanceTo(ductEnd);

                // Находим опоры на оси воздуховода
                List<FamilyInstance> supportsOnDuct = new List<FamilyInstance>();
                foreach (FamilyInstance fitting in selectedFittings)
                {
                    XYZ fittingCenter = GetFittingCenter(fitting);
                    if (fittingCenter == null) continue;
                    XYZ projection = ductStart + ductDir * (fittingCenter - ductStart).DotProduct(ductDir);
                    double distanceToAxis = fittingCenter.DistanceTo(projection);
                    double projectionValue = (fittingCenter - ductStart).DotProduct(ductDir);
                    if (distanceToAxis < 0.05 && projectionValue >= -0.05 && projectionValue <= ductLength + 0.05)
                        supportsOnDuct.Add(fitting);
                }

                // Находим концевые фитинги
                ConnectorManager connectorManager = duct.ConnectorManager;
                FamilyInstance startFitting = null, endFitting = null;
                if (connectorManager != null)
                {
                    foreach (Connector conn in connectorManager.Connectors)
                    {
                        if (conn.Origin.DistanceTo(ductStart) < 0.01 && conn.IsConnected)
                        {
                            foreach (Connector connectedConn in conn.AllRefs)
                            {
                                Element connectedElement = connectedConn.Owner;
                                if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Duct Fittings")
                                { startFitting = fitting; break; }
                            }
                        }
                        if (conn.Origin.DistanceTo(ductEnd) < 0.01 && conn.IsConnected)
                        {
                            foreach (Connector connectedConn in conn.AllRefs)
                            {
                                Element connectedElement = connectedConn.Owner;
                                if (connectedElement is FamilyInstance fitting && fitting.Category != null && fitting.Category.Name == "Duct Fittings")
                                { endFitting = fitting; break; }
                            }
                        }
                    }
                }

                // ЕСЛИ ОПОР НЕТ - ДЕЛАЕМ РАЗМЕР МЕЖДУ ЦЕНТРАМИ ФИТИНГОВ
                if (supportsOnDuct.Count == 0)
                {
                    errorMessage += $"\n- Воздуховод {duct.Id}: ℹ️ опоры не найдены, создаем размер между центрами фитингов";

                    if (startFitting == null && endFitting == null)
                    {
                        return CreateDimensionForDuct(doc, duct, view, dimensionType, settings, sketchPlaneCache, ref errorMessage);
                    }

                    XYZ pointA = ductStart;
                    XYZ pointB = ductEnd;

                    if (startFitting != null && endFitting != null)
                    {
                        XYZ centerA = GetFittingCenter(startFitting);
                        XYZ centerB = GetFittingCenter(endFitting);
                        if (centerA == null || centerB == null)
                        {
                            errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центры фитингов";
                            return false;
                        }
                        pointA = centerA;
                        pointB = centerB;
                    }
                    else if (startFitting != null)
                    {
                        XYZ center = GetFittingCenter(startFitting);
                        if (center == null)
                        {
                            errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центр фитинга";
                            return false;
                        }
                        pointA = center;
                        pointB = ductEnd;
                    }
                    else if (endFitting != null)
                    {
                        XYZ center = GetFittingCenter(endFitting);
                        if (center == null)
                        {
                            errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось получить центр фитинга";
                            return false;
                        }
                        pointA = ductStart;
                        pointB = center;
                    }

                    double distance = pointA.DistanceTo(pointB);
                    if (distance < 0.001) { errorMessage += $"\n- Воздуховод {duct.Id}: ⚠️ точки совпадают"; return false; }

                    XYZ up = XYZ.BasisZ;
                    if (Math.Abs(ductDir.DotProduct(up)) > 0.99) up = XYZ.BasisX;
                    XYZ offsetDir = ductDir.CrossProduct(up).Normalize();
                    XYZ offset = offsetDir * settings.OffsetDistance;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    XYZ normal = ductDir.CrossProduct(offsetDir).Normalize();
                    Plane plane = Plane.CreateByNormalAndOrigin(normal, pointA);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    SketchPlane originalSketchPlane = view.SketchPlane;
                    view.SketchPlane = sketchPlane;
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane) as ModelLine;
                    if (helperLine == null) { errorMessage += $"\n- Воздуховод {duct.Id}: ❌ не удалось создать ModelLine"; view.SketchPlane = originalSketchPlane; return false; }
                    Line dimensionLine = Line.CreateBound(pointA + offset, pointB + offset);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                    view.SketchPlane = originalSketchPlane;
                    return true;
                }

                // ЕСЛИ ОПОРЫ ЕСТЬ
                var sortedSupports = supportsOnDuct.OrderBy(s =>
                {
                    XYZ center = GetFittingCenter(s);
                    if (center == null) return double.MaxValue;
                    return (center - ductStart).DotProduct(ductDir);
                }).ToList();

                List<XYZ> points = new List<XYZ>();
                if (startFitting != null)
                {
                    XYZ center = GetFittingCenter(startFitting);
                    points.Add(center ?? ductStart);
                }
                else points.Add(ductStart);

                foreach (var support in sortedSupports)
                {
                    XYZ center = GetFittingCenter(support);
                    if (center != null) points.Add(center);
                }

                if (endFitting != null)
                {
                    XYZ center = GetFittingCenter(endFitting);
                    points.Add(center ?? ductEnd);
                }
                else points.Add(ductEnd);

                points = points.Distinct(new XYZComparer()).ToList();
                if (points.Count < 2) return false;

                XYZ up2 = XYZ.BasisZ;
                if (Math.Abs(ductDir.DotProduct(up2)) > 0.99) up2 = XYZ.BasisX;
                XYZ offsetDir2 = ductDir.CrossProduct(up2).Normalize();
                XYZ offset2 = offsetDir2 * settings.OffsetDistance;
                XYZ normal2 = ductDir.CrossProduct(offsetDir2).Normalize();
                Plane plane2 = Plane.CreateByNormalAndOrigin(normal2, points[0]);
                SketchPlane sketchPlane2 = SketchPlane.Create(doc, plane2);
                SketchPlane originalSketchPlane2 = view.SketchPlane;
                view.SketchPlane = sketchPlane2;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ pointA = points[i];
                    XYZ pointB = points[i + 1];
                    if (pointA.DistanceTo(pointB) < 0.001) continue;
                    Line modelLine = Line.CreateBound(pointA, pointB);
                    ModelLine helperLine = doc.Create.NewModelCurve(modelLine, sketchPlane2) as ModelLine;
                    if (helperLine == null) continue;
                    Line dimensionLine = Line.CreateBound(pointA + offset2, pointB + offset2);
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(0));
                    refArray.Append(helperLine.GeometryCurve.GetEndPointReference(1));
                    Dimension dimension = doc.Create.NewDimension(view, dimensionLine, refArray);
                    if (dimensionType != null) dimension.DimensionType = dimensionType;
                }
                view.SketchPlane = originalSketchPlane2;
                return true;
            }
            catch (Exception ex) { errorMessage += $"\n- Воздуховод {duct.Id}: 💥 {ex.Message}"; return false; }
        }

        // ====================================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ====================================================================
        private XYZ GetFittingCenter(FamilyInstance fitting)
        {
            try
            {
                LocationPoint locPoint = fitting.Location as LocationPoint;
                if (locPoint != null && locPoint.Point != null) return locPoint.Point;
                BoundingBoxXYZ bbox = fitting.get_BoundingBox(null);
                if (bbox != null) return (bbox.Min + bbox.Max) / 2;
                return null;
            }
            catch { return null; }
        }

        private List<Reference> GetPipeEndReferences(Pipe pipe, XYZ startPoint, XYZ endPoint)
        {
            try
            {
                List<Reference> references = new List<Reference>();
                Options geomOptions = new Options();
                geomOptions.ComputeReferences = true;
                geomOptions.DetailLevel = ViewDetailLevel.Fine;
                geomOptions.IncludeNonVisibleObjects = true;
                GeometryElement geomElement = pipe.get_Geometry(geomOptions);
                if (geomElement == null) return null;
                Reference startRef = null, endRef = null;
                double startMinDistance = 0.05, endMinDistance = 0.05;
                foreach (GeometryObject geomObj in geomElement)
                {
                    if (geomObj is Solid solid && solid.Faces.Size > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                BoundingBoxUV bbox = face.GetBoundingBox();
                                if (bbox == null) continue;
                                List<UV> testPoints = new List<UV> { (bbox.Min + bbox.Max) / 2, bbox.Min, bbox.Max, new UV(bbox.Min.U, bbox.Max.V), new UV(bbox.Max.U, bbox.Min.V) };
                                foreach (UV uv in testPoints)
                                {
                                    XYZ facePoint = face.Evaluate(uv);
                                    double startDistance = facePoint.DistanceTo(startPoint);
                                    if (startDistance < startMinDistance) { startMinDistance = startDistance; startRef = face.Reference; }
                                    double endDistance = facePoint.DistanceTo(endPoint);
                                    if (endDistance < endMinDistance) { endMinDistance = endDistance; endRef = face.Reference; }
                                }
                            }
                            catch { continue; }
                        }
                    }
                }
                if (startRef != null && endRef != null) { references.Add(startRef); references.Add(endRef); return references; }
                return null;
            }
            catch { return null; }
        }

        private List<Reference> GetDuctEndReferences(Duct duct, XYZ startPoint, XYZ endPoint)
        {
            try
            {
                List<Reference> references = new List<Reference>();
                Options geomOptions = new Options();
                geomOptions.ComputeReferences = true;
                geomOptions.DetailLevel = ViewDetailLevel.Fine;
                geomOptions.IncludeNonVisibleObjects = true;
                GeometryElement geomElement = duct.get_Geometry(geomOptions);
                if (geomElement == null) return null;
                Reference startRef = null, endRef = null;
                double startMinDistance = 0.05, endMinDistance = 0.05;
                foreach (GeometryObject geomObj in geomElement)
                {
                    if (geomObj is Solid solid && solid.Faces.Size > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                BoundingBoxUV bbox = face.GetBoundingBox();
                                if (bbox == null) continue;
                                List<UV> testPoints = new List<UV> { (bbox.Min + bbox.Max) / 2, bbox.Min, bbox.Max, new UV(bbox.Min.U, bbox.Max.V), new UV(bbox.Max.U, bbox.Min.V) };
                                foreach (UV uv in testPoints)
                                {
                                    XYZ facePoint = face.Evaluate(uv);
                                    double startDistance = facePoint.DistanceTo(startPoint);
                                    if (startDistance < startMinDistance) { startMinDistance = startDistance; startRef = face.Reference; }
                                    double endDistance = facePoint.DistanceTo(endPoint);
                                    if (endDistance < endMinDistance) { endMinDistance = endDistance; endRef = face.Reference; }
                                }
                            }
                            catch { continue; }
                        }
                    }
                }
                if (startRef != null && endRef != null) { references.Add(startRef); references.Add(endRef); return references; }
                return null;
            }
            catch { return null; }
        }
    }

    public class XYZComparer : IEqualityComparer<XYZ>
    {
        private double tolerance = 0.001;
        public bool Equals(XYZ x, XYZ y) { if (x == null || y == null) return false; return x.DistanceTo(y) < tolerance; }
        public int GetHashCode(XYZ obj) { return obj.GetHashCode(); }
    }

    public class DuctAndPipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is Pipe) return true;
            if (elem is Duct) return true;
            if (elem is FamilyInstance familyInstance && familyInstance.Category != null)
            {
                string catName = familyInstance.Category.Name;
                if (catName.Contains("Fittings") || catName.Contains("Support") || catName.Contains("Опора") || catName.Contains("Крепление"))
                    return true;
            }
            return false;
        }
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}