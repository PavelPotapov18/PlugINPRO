using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.OV;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlugINPRO.Commands.OV
{
    [Transaction(TransactionMode.Manual)]
    public class DuctSupportsByLengthCommand : IExternalCommand
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
                // 1. Показываем окно настроек
                DuctFittingByLengthSettingsWindow settingsWindow = new DuctFittingByLengthSettingsWindow(doc);
                bool? dialogResult = settingsWindow.ShowDialog();

                if (dialogResult != true || settingsWindow.SelectedFamily == null)
                    return Result.Cancelled;

                // 2. Выбор воздуховода
                Reference ductRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new DuctSelectionFilter(),
                    "Выберите воздуховод");

                Duct duct = doc.GetElement(ductRef) as Duct;
                if (duct == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является воздуховодом");
                    return Result.Failed;
                }

                // 3. Получаем размеры
                DuctDimensions ductDims = GetDuctDimensions(duct);

                // Параметры системы
                Parameter systemClassification = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                Parameter systemName = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = duct.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 4. Получение кривой
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию воздуховода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double curveLength = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);

                // 5. Расчет шага и отступа (у воздуховодов нет уклона)
                double spacingFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Spacing, UnitTypeId.Millimeters);
                double offsetFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Offset, UnitTypeId.Millimeters);

                // 6. Расчет точек
                List<XYZ> placementPoints = CalculatePlacementPoints(curve, spacingFeet, offsetFeet);

                if (placementPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание", $"Не удалось рассчитать точки размещения опор.\nДлина: {curveLength:F0} мм");
                    return Result.Cancelled;
                }

                // 7. Определяем тип
                XYZ ductDirection = GetDuctDirection(curve);
                bool isVertical = Math.Abs(ductDirection.Z) > 0.9;

                // 8. Ориентация для поворота
                XYZ rotationOrientation;
                if (isVertical && !ductDims.IsRound)
                    rotationOrientation = GetDuctWallOrientation(duct);
                else
                {
                    rotationOrientation = new XYZ(ductDirection.X, ductDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 9. Размещение
                using (Transaction trans = new Transaction(doc, "Размещение опор воздуховода"))
                {
                    trans.Start();

                    if (!settingsWindow.SelectedFamily.IsActive)
                        settingsWindow.SelectedFamily.Activate();

                    int placedCount = 0;

                    foreach (XYZ point in placementPoints)
                    {
                        try
                        {
                            FamilyInstance support = doc.Create.NewFamilyInstance(
                                point,
                                settingsWindow.SelectedFamily,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (support != null)
                            {
                                SetDuctParameters(support, ductDims,
                                    systemClassification?.AsString() ?? "",
                                    systemType != null ? doc.GetElement(systemType.AsElementId())?.Name ?? "" : "",
                                    systemName?.AsString() ?? "",
                                    systemAbbreviation?.AsString() ?? "");

                                RotateSupport(support, point, rotationOrientation);
                                placedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Ошибка: {ex.Message}");
                        }
                    }

                    trans.Commit();

                    if (!settingsWindow.UsePreviousFamily)
                        App.SelectedSupportFamily = settingsWindow.SelectedFamily;

                    string resultMessage =
                        $"Установлено опор: {placedCount} из {placementPoints.Count}\n" +
                        $"Шаг: {settingsWindow.Spacing} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Тип: {(isVertical ? "Вертикальный" : "Горизонтальный")}\n" +
                        $"Сечение: {(ductDims.IsRound ? "Круглое" : "Прямоугольное")}";

                    if (ductDims.IsRound)
                        resultMessage += $"\nДиаметр: Ø{ductDims.Diameter:F0} мм";
                    else
                        resultMessage += $"\nРазмер: {ductDims.Width:F0}×{ductDims.Height:F0} мм";

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
                TaskDialog.Show("Ошибка", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private class DuctSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Duct;
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

        private List<XYZ> CalculatePlacementPoints(Curve curve, double spacing, double offset)
        {
            List<XYZ> points = new List<XYZ>();
            try
            {
                double curveLength = curve.Length;
                if (offset >= curveLength) return points;

                double currentDistance = offset;
                while (currentDistance < curveLength)
                {
                    XYZ point = GetPointAtDistance(curve, currentDistance);
                    if (point != null) points.Add(point);
                    currentDistance += spacing;

                    if (currentDistance >= curveLength && points.Count > 0)
                    {
                        double distanceToEnd = curveLength - (currentDistance - spacing);
                        double minDistanceFeet = 1.0 / 304.8;
                        if (distanceToEnd > minDistanceFeet)
                        {
                            XYZ endPoint = GetPointAtDistance(curve, curveLength);
                            if (endPoint != null) points.Add(endPoint);
                        }
                        break;
                    }
                }

                if (points.Count == 0 && offset < curveLength)
                {
                    double middleDistance = offset + (curveLength - offset) / 2;
                    XYZ middlePoint = GetPointAtDistance(curve, middleDistance);
                    if (middlePoint != null) points.Add(middlePoint);
                }
            }
            catch { }
            return points;
        }

        private XYZ GetPointAtDistance(Curve curve, double distance)
        {
            try
            {
                double tolerance = 0.0001;
                if (distance < 0) distance = 0;
                if (distance > curve.Length) distance = curve.Length;

                if (Math.Abs(distance - curve.Length) < tolerance)
                    return curve.GetEndPoint(1);

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
            catch { return curve.GetEndPoint(0); }
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