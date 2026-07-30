using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.OV;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlugINPRO.Commands.OV
{
    [Transaction(TransactionMode.Manual)]
    public class DuctSupportCopyCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Выбираем существующую опору
                Reference supportRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new SupportSelectionFilter(),
                    "Выберите опору для копирования");

                FamilyInstance support = doc.GetElement(supportRef) as FamilyInstance;
                if (support == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является опорой");
                    return Result.Failed;
                }

                // 2. Находим воздуховод, на котором стоит опора
                Duct duct = FindHostDuct(support, doc);
                if (duct == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось найти воздуховод, на котором установлена опора");
                    return Result.Failed;
                }

                // 3. Получаем геометрию
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию воздуховода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                XYZ ductDirection = GetDuctDirection(curve);
                bool isVertical = Math.Abs(ductDirection.Z) > 0.9;

                // 4. Показываем окно настроек
                DuctSupportCopySettingsWindow settingsWindow = new DuctSupportCopySettingsWindow(
                    support, duct, isVertical);

                bool? dialogResult = settingsWindow.ShowDialog();
                if (dialogResult != true) return Result.Cancelled;

                // 5. Получаем настройки
                double offsetMm = settingsWindow.Offset;
                int copyCount = settingsWindow.Count;
                double spacingMm = settingsWindow.Spacing;
                bool copyMultiple = settingsWindow.CopyMultiple;

                double offsetFeet = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
                double spacingFeet = copyMultiple ? UnitUtils.ConvertToInternalUnits(spacingMm, UnitTypeId.Millimeters) : 0;

                // 6. Находим позицию опоры
                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить положение опоры");
                    return Result.Failed;
                }

                XYZ projectedPoint = GetClosestPointOnCurve(curve, supportPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось спроецировать точку на трассу воздуховода");
                    return Result.Failed;
                }

                double parameter = GetParameterOnCurve(curve, projectedPoint);
                if (parameter < 0)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить параметр точки на кривой");
                    return Result.Failed;
                }

                // 7. Вычисляем позиции
                double curveLength = curve.Length;
                double currentDistance = parameter * curveLength;

                List<XYZ> newPoints = new List<XYZ>();
                double currentOffset = offsetFeet;
                int maxCopies = copyMultiple ? copyCount : 1;

                for (int i = 0; i < maxCopies; i++)
                {
                    double newDistance = currentDistance + currentOffset;
                    if (newDistance < 0 || newDistance > curveLength)
                    {
                        if (i == 0)
                        {
                            TaskDialog.Show("Внимание", $"Смещение {offsetMm:F0} мм выходит за пределы воздуховода!");
                            return Result.Cancelled;
                        }
                        break;
                    }

                    XYZ newPoint = GetPointAtDistance(curve, newDistance);
                    if (newPoint != null) newPoints.Add(newPoint);
                    currentOffset += spacingFeet;
                }

                if (newPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание", "Не удалось создать ни одной копии опоры!");
                    return Result.Cancelled;
                }

                // 8. Получаем параметры системы
                Parameter systemClassification = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                Parameter systemName = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = duct.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 9. Получаем размеры
                DuctDimensions dims = GetDuctDimensions(duct);

                // 10. Ориентация для поворота
                XYZ rotationOrientation;
                if (isVertical && !dims.IsRound)
                    rotationOrientation = GetDuctWallOrientation(duct);
                else
                {
                    rotationOrientation = new XYZ(ductDirection.X, ductDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 11. Создаем новые опоры
                using (Transaction trans = new Transaction(doc, "Копирование опор"))
                {
                    trans.Start();

                    FamilySymbol familySymbol = support.Symbol;
                    if (!familySymbol.IsActive) familySymbol.Activate();

                    int placedCount = 0;

                    foreach (XYZ point in newPoints)
                    {
                        try
                        {
                            FamilyInstance newSupport = doc.Create.NewFamilyInstance(
                                point,
                                familySymbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (newSupport != null)
                            {
                                SetDuctParameters(newSupport, dims,
                                    systemClassification?.AsString() ?? "",
                                    systemType != null ? doc.GetElement(systemType.AsElementId())?.Name ?? "" : "",
                                    systemName?.AsString() ?? "",
                                    systemAbbreviation?.AsString() ?? "");

                                RotateSupport(newSupport, point, rotationOrientation);
                                placedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Ошибка создания опоры: {ex.Message}");
                        }
                    }

                    trans.Commit();

                    string directionText = offsetMm > 0 ? "вперед" : "назад";
                    string multipleInfo = copyMultiple ? $"\nКоличество опор: {placedCount}\nШаг: {spacingMm:F0} мм" : "";

                    TaskDialog.Show("Результат",
                        $"Создано опор: {placedCount} из {newPoints.Count}\n" +
                        $"Начальное смещение: {Math.Abs(offsetMm):F0} мм {directionText}{multipleInfo}");
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
                TaskDialog.Show("Ошибка", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private class SupportSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem is FamilyInstance instance)
                {
                    Category cat = instance.Category;
                    if (cat != null && cat.Id.IntegerValue == (int)BuiltInCategory.OST_DuctFitting)
                        return true;
                }
                return false;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private class DuctDimensions
        {
            public double Width { get; set; }
            public double Height { get; set; }
            public double Diameter { get; set; }
            public bool IsRound { get; set; }
        }

        private DuctDimensions GetDuctDimensions(Duct duct)
        {
            DuctDimensions dims = new DuctDimensions();
            try
            {
                Parameter diameterParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (diameterParam != null && diameterParam.HasValue)
                {
                    dims.Diameter = UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                    dims.IsRound = true;
                    return dims;
                }

                Parameter widthParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                Parameter heightParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

                if (widthParam != null && widthParam.HasValue && heightParam != null && heightParam.HasValue)
                {
                    dims.Width = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Millimeters);
                    dims.Height = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Millimeters);
                    dims.IsRound = false;
                }
                else
                {
                    dims.Diameter = 100.0;
                    dims.IsRound = true;
                }
            }
            catch
            {
                dims.Diameter = 100.0;
                dims.IsRound = true;
            }
            return dims;
        }

        private XYZ GetDuctDirection(Curve curve)
        {
            try
            {
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ direction = end - start;
                if (direction.GetLength() < 0.001) return XYZ.BasisZ;
                return direction.Normalize();
            }
            catch { return XYZ.BasisZ; }
        }

        private XYZ GetDuctWallOrientation(Duct duct)
        {
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = duct.get_Geometry(options);
                List<XYZ> wallNormals = new List<XYZ>();

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                XYZ normal = GetFaceNormalAtCenter(face);
                                if (normal != null && normal.GetLength() > 0.001 && Math.Abs(normal.Z) < 0.3)
                                    wallNormals.Add(normal);
                            }
                            catch { }
                        }
                    }
                }

                if (wallNormals.Count > 0)
                {
                    XYZ wallNormal = wallNormals[0];
                    XYZ wallDirection = new XYZ(-wallNormal.Y, wallNormal.X, 0);
                    if (wallDirection.GetLength() > 0.001)
                        return wallDirection.Normalize();
                }
                return XYZ.BasisX;
            }
            catch { return XYZ.BasisX; }
        }

        private XYZ GetFaceNormalAtCenter(Face face)
        {
            try
            {
                BoundingBoxUV bbox = face.GetBoundingBox();
                UV centerUV = new UV((bbox.Min.U + bbox.Max.U) * 0.5, (bbox.Min.V + bbox.Max.V) * 0.5);
                return face.ComputeNormal(centerUV);
            }
            catch { return null; }
        }

        private Duct FindHostDuct(FamilyInstance support, Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ICollection<Element> ducts = collector
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .ToElements();

                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null) return null;

                double minDistance = double.MaxValue;
                Duct closestDuct = null;

                foreach (Duct duct in ducts)
                {
                    LocationCurve locationCurve = duct.Location as LocationCurve;
                    if (locationCurve == null) continue;

                    IntersectionResult result = locationCurve.Curve.Project(supportPoint);
                    if (result != null && result.Distance < minDistance)
                    {
                        minDistance = result.Distance;
                        closestDuct = duct;
                    }
                }

                double maxDistanceFeet = 1.0 / 304.8;
                if (closestDuct != null && minDistance < maxDistanceFeet)
                    return closestDuct;

                return null;
            }
            catch { return null; }
        }

        private XYZ GetSupportLocation(FamilyInstance support)
        {
            try
            {
                LocationPoint locPoint = support.Location as LocationPoint;
                if (locPoint != null) return locPoint.Point;

                Options options = new Options();
                options.ComputeReferences = true;
                GeometryElement geometry = support.get_Geometry(options);

                if (geometry != null)
                {
                    BoundingBoxXYZ bbox = support.get_BoundingBox(null);
                    if (bbox != null) return (bbox.Min + bbox.Max) / 2;
                }
                return null;
            }
            catch { return null; }
        }

        private XYZ GetClosestPointOnCurve(Curve curve, XYZ point)
        {
            try
            {
                IntersectionResult result = curve.Project(point);
                return result?.XYZPoint;
            }
            catch { return null; }
        }

        private double GetParameterOnCurve(Curve curve, XYZ point)
        {
            try
            {
                if (curve is Line line)
                {
                    XYZ start = line.GetEndPoint(0);
                    XYZ end = line.GetEndPoint(1);
                    XYZ direction = end - start;
                    double length = direction.GetLength();
                    if (length < 0.0001) return 0;

                    XYZ vector = point - start;
                    double projection = vector.DotProduct(direction) / length;
                    return Math.Max(0, Math.Min(1, projection / length));
                }
                else
                {
                    IntersectionResult result = curve.Project(point);
                    return result?.Parameter ?? -1;
                }
            }
            catch { return -1; }
        }

        private XYZ GetPointAtDistance(Curve curve, double distance)
        {
            try
            {
                if (distance < 0) distance = 0;
                if (distance > curve.Length) distance = curve.Length;

                if (curve is Line line)
                {
                    XYZ start = line.GetEndPoint(0);
                    XYZ end = line.GetEndPoint(1);
                    double t = distance / curve.Length;
                    t = Math.Max(0.0, Math.Min(1.0, t));
                    return new XYZ(
                        start.X + t * (end.X - start.X),
                        start.Y + t * (end.Y - start.Y),
                        start.Z + t * (end.Z - start.Z)
                    );
                }
                else
                {
                    double param = distance / curve.Length;
                    param = Math.Max(0.0, Math.Min(1.0, param));
                    return curve.Evaluate(param, false);
                }
            }
            catch { return null; }
        }

        private void RotateSupport(FamilyInstance support, XYZ point, XYZ orientation)
        {
            try
            {
                double angleRad = Math.Atan2(orientation.Y, orientation.X);
                if (angleRad < 0) angleRad += 2 * Math.PI;

                XYZ axisStart = point;
                XYZ axisEnd = new XYZ(point.X, point.Y, point.Z + 1.0);
                Line axis = Line.CreateBound(axisStart, axisEnd);

                ElementTransformUtils.RotateElement(support.Document, support.Id, axis, angleRad);
            }
            catch { }
        }

        private void SetDuctParameters(FamilyInstance support, DuctDimensions dims,
            string systemClassification, string systemType, string systemName, string systemAbbreviation)
        {
            try
            {
                if (dims.IsRound)
                {
                    SetDoubleParameterValue(support, "D", dims.Diameter);
                    SetDoubleParameterValue(support, "DN", dims.Diameter);
                    SetDoubleParameterValue(support, "Диаметр", dims.Diameter);
                    SetDoubleParameterValue(support, "Diameter", dims.Diameter);
                }
                else
                {
                    SetDoubleParameterValue(support, "DUCT_H", dims.Height);
                    SetDoubleParameterValue(support, "DUCT_B", dims.Width);
                    SetDoubleParameterValue(support, "H", dims.Height);
                    SetDoubleParameterValue(support, "B", dims.Width);
                    SetDoubleParameterValue(support, "Height", dims.Height);
                    SetDoubleParameterValue(support, "Width", dims.Width);
                    SetDoubleParameterValue(support, "Высота", dims.Height);
                    SetDoubleParameterValue(support, "Ширина", dims.Width);
                }

                SetStringParameterValue(support, "ADSK_Система_Классификация", systemClassification);
                SetStringParameterValue(support, "ADSK_Система_Тип", systemType);
                SetStringParameterValue(support, "ADSK_Система_Имя", systemName);
                SetStringParameterValue(support, "ADSK_Система_Сокращение", systemAbbreviation);
            }
            catch { }
        }

        private void SetDoubleParameterValue(FamilyInstance support, string paramName, double valueMm)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.Double)
                    param.Set(UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters));
            }
            catch { }
        }

        private void SetStringParameterValue(FamilyInstance support, string paramName, string value)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                    param.Set(value);
            }
            catch { }
        }
    }
}