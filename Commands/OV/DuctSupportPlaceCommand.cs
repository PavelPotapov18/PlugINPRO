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
    public class DuctSupportPlaceCommand : IExternalCommand
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
                // 1. Выбираем семейство опоры
                DuctSupportSelectWindow selectWindow = new DuctSupportSelectWindow(doc);
                bool? dialogResult = selectWindow.ShowDialog();

                if (dialogResult != true || selectWindow.SelectedFamily == null)
                    return Result.Cancelled;

                FamilySymbol selectedFamily = selectWindow.SelectedFamily;

                // 2. Выбираем воздуховод
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

                // 3. Получаем геометрию
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию воздуховода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double curveLengthFeet = curve.Length;
                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(curveLengthFeet, UnitTypeId.Millimeters);

                // 4. Клик на воздуховод
                Reference clickRef = uidoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new DuctSelectionFilter(),
                    "Кликните на воздуховод в месте установки опоры");

                XYZ clickPoint = clickRef.GlobalPoint;
                if (clickPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить точку клика");
                    return Result.Failed;
                }

                // 5. ✅ НАХОДИМ ТОЧКУ НА ОСИ ВОЗДУХОВОДА (исправленный метод)
                XYZ projectedPoint = GetPointOnCurveAtXY(curve, clickPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось найти точку на оси воздуховода. Кликните ближе к воздуховоду.");
                    return Result.Failed;
                }

                // 6. Расстояния
                double distanceFromStartFeet = GetDistanceAlongCurve(curve, projectedPoint);
                double distanceFromStartMm = UnitUtils.ConvertFromInternalUnits(distanceFromStartFeet, UnitTypeId.Millimeters);
                double distanceFromEndMm = totalLengthMm - distanceFromStartMm;

                // 7. Окно настроек
                DuctSupportPlaceSettingsWindow settingsWindow = new DuctSupportPlaceSettingsWindow(
                    distanceFromStartMm,
                    distanceFromEndMm,
                    totalLengthMm);

                bool? settingsResult = settingsWindow.ShowDialog();
                if (settingsResult != true) return Result.Cancelled;

                // 8. Получаем настройки
                double userOffsetMm = settingsWindow.UserOffset;
                bool isFromStart = settingsWindow.IsFromStart;

                double offsetFromStartMm = isFromStart ? userOffsetMm : totalLengthMm - userOffsetMm;
                if (offsetFromStartMm < 0 || offsetFromStartMm > totalLengthMm)
                {
                    TaskDialog.Show("Внимание", $"Отступ выходит за пределы воздуховода!\nМаксимальный отступ: {totalLengthMm:F0} мм");
                    return Result.Cancelled;
                }

                double offsetFromStartFeet = UnitUtils.ConvertToInternalUnits(offsetFromStartMm, UnitTypeId.Millimeters);

                // 9. Точка установки
                XYZ placePoint = GetPointAtDistance(curve, offsetFromStartFeet);
                if (placePoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось вычислить точку установки");
                    return Result.Failed;
                }

                // 10. Параметры системы
                Parameter systemClassification = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                Parameter systemName = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = duct.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 11. Размеры
                DuctDimensions dims = GetDuctDimensions(duct);

                // 12. Ориентация
                XYZ ductDirection = GetDuctDirection(curve);
                bool isVertical = Math.Abs(ductDirection.Z) > 0.9;

                XYZ rotationOrientation;
                if (isVertical && !dims.IsRound)
                    rotationOrientation = GetDuctWallOrientation(duct);
                else
                {
                    rotationOrientation = new XYZ(ductDirection.X, ductDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 13. Установка
                using (Transaction trans = new Transaction(doc, "Установка опоры"))
                {
                    trans.Start();

                    if (!selectedFamily.IsActive)
                        selectedFamily.Activate();

                    FamilyInstance support = doc.Create.NewFamilyInstance(
                        placePoint,
                        selectedFamily,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                    if (support != null)
                    {
                        SetDuctParameters(support, dims,
                            systemClassification?.AsString() ?? "",
                            systemType != null ? doc.GetElement(systemType.AsElementId())?.Name ?? "" : "",
                            systemName?.AsString() ?? "",
                            systemAbbreviation?.AsString() ?? "");

                        RotateSupport(support, placePoint, rotationOrientation);

                        trans.Commit();

                        string directionText = isFromStart ? "от начала" : "от конца";
                        TaskDialog.Show("Результат",
                            $"✅ Опора установлена!\n" +
                            $"Отступ: {userOffsetMm:F0} мм {directionText}\n" +
                            $"Позиция: X={placePoint.X:F2}, Y={placePoint.Y:F2}, Z={placePoint.Z:F2}");
                    }
                    else
                    {
                        trans.RollBack();
                        TaskDialog.Show("Ошибка", "Не удалось создать опору");
                        return Result.Failed;
                    }
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
            public bool AllowReference(Reference reference, XYZ position) => true;
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

        /// <summary>
        /// Находит точку на кривой с заданными X и Y
        /// </summary>
        private XYZ GetPointOnCurveAtXY(Curve curve, XYZ point)
        {
            try
            {
                // Если кривая - прямая линия
                if (curve is Line line)
                {
                    XYZ start = line.GetEndPoint(0);
                    XYZ end = line.GetEndPoint(1);
                    XYZ direction = (end - start).Normalize();

                    // Если труба вертикальная
                    if (Math.Abs(direction.X) < 0.001 && Math.Abs(direction.Y) < 0.001)
                    {
                        return new XYZ(point.X, point.Y, (start.Z + end.Z) / 2);
                    }

                    // Находим параметр t, при котором X и Y совпадают
                    double t;
                    if (Math.Abs(direction.X) > 0.001)
                    {
                        t = (point.X - start.X) / direction.X;
                    }
                    else
                    {
                        t = (point.Y - start.Y) / direction.Y;
                    }

                    // Ограничиваем t от 0 до 1
                    t = Math.Max(0, Math.Min(1, t));

                    // Получаем точку на кривой
                    XYZ curvePoint = start + t * (end - start);

                    // Возвращаем точку с теми же X и Y, но с Z от кривой
                    return new XYZ(point.X, point.Y, curvePoint.Z);
                }

                // Для изогнутых кривых - перебор точек
                int segments = 200;
                double minDist = double.MaxValue;
                XYZ bestPoint = null;

                for (int i = 0; i <= segments; i++)
                {
                    double t = (double)i / segments;
                    XYZ curvePoint = curve.Evaluate(t, true);

                    double dx = curvePoint.X - point.X;
                    double dy = curvePoint.Y - point.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestPoint = curvePoint;
                    }
                }

                // Если нашли точку с расстоянием меньше 10 мм
                if (bestPoint != null && minDist < 0.01)
                {
                    return new XYZ(point.X, point.Y, bestPoint.Z);
                }

                // Если не нашли - пробуем через Project
                IntersectionResult intersectionResult = curve.Project(point);
                if (intersectionResult != null)
                {
                    return new XYZ(point.X, point.Y, intersectionResult.XYZPoint.Z);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private double GetDistanceAlongCurve(Curve curve, XYZ point)
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

                    return Math.Max(0, Math.Min(projection, length));
                }
                else
                {
                    IntersectionResult result = curve.Project(point);
                    if (result != null)
                    {
                        return result.Parameter * curve.Length;
                    }
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
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
            catch
            {
                return null;
            }
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