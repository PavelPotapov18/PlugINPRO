using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.VK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class PipeSupportCopyCommand : IExternalCommand
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

                // 2. Находим трубу, на которой стоит опора
                Pipe pipe = FindHostPipe(support, doc);

                if (pipe == null)
                {
                    TaskDialog.Show("Ошибка",
                        "Не удалось найти трубу, на которой установлена опора.\n" +
                        "Убедитесь, что опора находится на трубе.");
                    return Result.Failed;
                }

                // 3. Определяем тип трубы (вертикальная/горизонтальная) и уклон
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию трубопровода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                XYZ pipeDirection = GetPipeDirection(curve);
                bool isVertical = Math.Abs(pipeDirection.Z) > 0.9;

                double slope = 0.0;
                if (!isVertical)
                {
                    slope = GetPipeSlope(pipe);
                }

                double slopeAngle = Math.Atan(slope);
                double slopeCorrection = Math.Cos(slopeAngle);

                // 4. Показываем окно настроек
                PipeSupportCopySettingsWindow settingsWindow = new PipeSupportCopySettingsWindow(
                    support, pipe, slope, isVertical);

                bool? dialogResult = settingsWindow.ShowDialog();

                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                // 5. Получаем настройки от пользователя
                double offsetHorizontalMm = settingsWindow.Offset;
                int copyCount = settingsWindow.Count;
                double spacingHorizontalMm = settingsWindow.Spacing;
                bool copyMultiple = settingsWindow.CopyMultiple;

                // 6. Корректируем смещение с учетом уклона
                double offsetAlongPipeMm = offsetHorizontalMm / slopeCorrection;
                double offsetAlongPipeFeet = UnitUtils.ConvertToInternalUnits(offsetAlongPipeMm, UnitTypeId.Millimeters);

                double spacingAlongPipeFeet = 0;
                if (copyMultiple && copyCount > 1)
                {
                    double spacingAlongPipeMm = spacingHorizontalMm / slopeCorrection;
                    spacingAlongPipeFeet = UnitUtils.ConvertToInternalUnits(spacingAlongPipeMm, UnitTypeId.Millimeters);
                }

                // 7. Находим позицию опоры на кривой трубы
                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить положение опоры");
                    return Result.Failed;
                }

                // Проецируем точку на кривую
                XYZ projectedPoint = GetClosestPointOnCurve(curve, supportPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось спроецировать точку на трассу трубы");
                    return Result.Failed;
                }

                // Находим параметр t для точки на кривой
                double parameter = GetParameterOnCurve(curve, projectedPoint);
                if (parameter < 0)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить параметр точки на кривой");
                    return Result.Failed;
                }

                // 8. Вычисляем позиции для копирования
                double curveLength = curve.Length;
                double currentDistance = parameter * curveLength;

                List<XYZ> newPoints = new List<XYZ>();
                double currentOffset = offsetAlongPipeFeet;

                int maxCopies = copyMultiple ? copyCount : 1;

                for (int i = 0; i < maxCopies; i++)
                {
                    double newDistance = currentDistance + currentOffset;

                    // Проверяем, что точка не выходит за пределы трубы
                    if (newDistance < 0 || newDistance > curveLength)
                    {
                        string direction = currentOffset > 0 ? "вперед" : "назад";
                        double maxOffsetMm = (currentOffset > 0 ?
                            curveLength - currentDistance : currentDistance) * 304.8 * slopeCorrection;

                        if (i == 0)
                        {
                            TaskDialog.Show("Внимание",
                                $"Смещение {offsetHorizontalMm:F0} мм {direction} выходит за пределы трубы!\n" +
                                $"Максимальное смещение: {Math.Abs(maxOffsetMm):F0} мм");
                            return Result.Cancelled;
                        }
                        else
                        {
                            TaskDialog.Show("Внимание",
                                $"Копирование остановлено на опоре #{i + 1}.\n" +
                                $"Смещение выходит за пределы трубы!\n" +
                                $"Создано опор: {i}");
                            break;
                        }
                    }

                    XYZ newPoint = GetPointAtDistance(curve, newDistance);
                    if (newPoint != null)
                    {
                        newPoints.Add(newPoint);
                    }

                    currentOffset += spacingAlongPipeFeet;
                }

                if (newPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание", "Не удалось создать ни одной копии опоры!");
                    return Result.Cancelled;
                }

                // 9. Получаем параметры системы трубы
                Parameter systemClassification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                Parameter systemName = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = pipe.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 10. Получаем размер трубы
                double diameter = GetPipeDiameter(pipe);

                // 11. Получаем ориентацию для поворота
                XYZ rotationOrientation;

                if (isVertical)
                {
                    rotationOrientation = GetPipeWallOrientation(pipe);
                }
                else
                {
                    rotationOrientation = new XYZ(pipeDirection.X, pipeDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 12. Создаем новые опоры
                using (Transaction trans = new Transaction(doc, "Копирование опор"))
                {
                    trans.Start();

                    // Получаем семейство опоры
                    FamilySymbol familySymbol = support.Symbol;

                    // Активируем семейство если не активно
                    if (!familySymbol.IsActive)
                        familySymbol.Activate();

                    int placedCount = 0;

                    foreach (XYZ point in newPoints)
                    {
                        try
                        {
                            // Создаем опору
                            FamilyInstance newSupport = doc.Create.NewFamilyInstance(
                                point,
                                familySymbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (newSupport != null)
                            {
                                // Устанавливаем параметры
                                SetPipeParameters(newSupport,
                                    diameter,
                                    systemClassification?.AsString() ?? "",
                                    systemType != null ? doc.GetElement(systemType.AsElementId())?.Name ?? "" : "",
                                    systemName?.AsString() ?? "",
                                    systemAbbreviation?.AsString() ?? "");

                                // Поворачиваем опору
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

                    // Формируем сообщение о результате
                    string directionText = offsetHorizontalMm > 0 ? "вперед" : "назад";
                    string verticalInfo = isVertical ? " (ВЕРТИКАЛЬНАЯ)" : "";
                    string slopeInfo = "";

                    if (!isVertical && slope != 0)
                    {
                        slopeInfo = $"\nУклон трубы: {slope * 100:F1}%";
                    }

                    string multipleInfo = "";
                    if (copyMultiple && copyCount > 1)
                    {
                        string spacingDirection = spacingHorizontalMm > 0 ? "вперед" : "назад";
                        multipleInfo = $"\nКоличество опор: {placedCount}\nШаг: {Math.Abs(spacingHorizontalMm):F0} мм ({spacingDirection})";
                    }

                    TaskDialog.Show("Результат",
                        $"Создано опор: {placedCount} из {newPoints.Count}\n" +
                        $"Начальное смещение: {Math.Abs(offsetHorizontalMm):F0} мм {directionText} (по горизонтали){verticalInfo}{slopeInfo}{multipleInfo}");
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
                    if (cat != null && cat.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting)
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private double GetPipeSlope(Pipe pipe)
        {
            try
            {
                Parameter slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                if (slopeParam != null && slopeParam.HasValue)
                {
                    return slopeParam.AsDouble();
                }
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private Pipe FindHostPipe(FamilyInstance support, Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ICollection<Element> pipes = collector
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .ToElements();

                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null) return null;

                double minDistance = double.MaxValue;
                Pipe closestPipe = null;

                foreach (Pipe pipe in pipes)
                {
                    LocationCurve locationCurve = pipe.Location as LocationCurve;
                    if (locationCurve == null) continue;

                    Curve curve = locationCurve.Curve;

                    IntersectionResult result = curve.Project(supportPoint);
                    if (result != null)
                    {
                        double distance = result.Distance;
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestPipe = pipe;
                        }
                    }
                }

                double maxDistanceFeet = 1.0 / 304.8; // 1 мм в футах
                if (closestPipe != null && minDistance < maxDistanceFeet)
                {
                    return closestPipe;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка FindHostPipe: {ex.Message}");
                return null;
            }
        }

        private XYZ GetSupportLocation(FamilyInstance support)
        {
            try
            {
                LocationPoint locPoint = support.Location as LocationPoint;
                if (locPoint != null)
                {
                    return locPoint.Point;
                }

                Options options = new Options();
                options.ComputeReferences = true;
                GeometryElement geometry = support.get_Geometry(options);

                if (geometry != null)
                {
                    BoundingBoxXYZ bbox = support.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        return (bbox.Min + bbox.Max) / 2;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private XYZ GetClosestPointOnCurve(Curve curve, XYZ point)
        {
            try
            {
                IntersectionResult result = curve.Project(point);
                if (result != null)
                {
                    return result.XYZPoint;
                }
                return null;
            }
            catch
            {
                return null;
            }
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

                    double t = projection / length;
                    return Math.Max(0, Math.Min(1, t));
                }
                else
                {
                    IntersectionResult result = curve.Project(point);
                    if (result != null)
                    {
                        return result.Parameter;
                    }
                    return -1;
                }
            }
            catch
            {
                return -1;
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

        private double GetPipeDiameter(Pipe pipe)
        {
            try
            {
                Parameter diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParam != null && diameterParam.HasValue)
                {
                    return UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                }

                diameterParam = pipe.LookupParameter("Диаметр");
                if (diameterParam != null && diameterParam.HasValue)
                {
                    if (diameterParam.StorageType == StorageType.Double)
                        return UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                    else if (diameterParam.StorageType == StorageType.String)
                    {
                        string stringValue = diameterParam.AsString();
                        if (!string.IsNullOrEmpty(stringValue) && double.TryParse(stringValue, out double result))
                            return result;
                    }
                }
                return 50.0;
            }
            catch
            {
                return 50.0;
            }
        }

        private XYZ GetPipeDirection(Curve curve)
        {
            try
            {
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ direction = end - start;

                if (direction.GetLength() < 0.001)
                    return XYZ.BasisZ;

                return direction.Normalize();
            }
            catch
            {
                return XYZ.BasisZ;
            }
        }

        private XYZ GetPipeWallOrientation(Pipe pipe)
        {
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = pipe.get_Geometry(options);
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
                                if (normal.GetLength() > 0.001 && Math.Abs(normal.Z) < 0.3)
                                {
                                    wallNormals.Add(normal);
                                }
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
                    {
                        wallDirection = wallDirection.Normalize();
                    }
                    else
                    {
                        wallDirection = XYZ.BasisX;
                    }

                    return wallDirection;
                }

                return XYZ.BasisX;
            }
            catch
            {
                return XYZ.BasisX;
            }
        }

        private XYZ GetFaceNormalAtCenter(Face face)
        {
            try
            {
                BoundingBoxUV bbox = face.GetBoundingBox();
                UV centerUV = new UV(
                    (bbox.Min.U + bbox.Max.U) * 0.5,
                    (bbox.Min.V + bbox.Max.V) * 0.5
                );

                return face.ComputeNormal(centerUV);
            }
            catch
            {
                return XYZ.Zero;
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка поворота: {ex.Message}");
            }
        }

        private void SetPipeParameters(
            FamilyInstance support,
            double diameterMm,
            string systemClassification,
            string systemType,
            string systemName,
            string systemAbbreviation)
        {
            try
            {
                SetDoubleParameterValue(support, "D", diameterMm);
                SetDoubleParameterValue(support, "DN", diameterMm);
                SetDoubleParameterValue(support, "Диаметр", diameterMm);
                SetDoubleParameterValue(support, "Diameter", diameterMm);
                SetDoubleParameterValue(support, "PIPE_D", diameterMm);
                SetDoubleParameterValue(support, "PIPE_DIAMETER", diameterMm);

                SetStringParameterValue(support, "Размер_трубы", $"{diameterMm:F0}");
                SetStringParameterValue(support, "Pipe_Size", $"{diameterMm:F0}");
                SetStringParameterValue(support, "Size", $"{diameterMm:F0}");

                SetStringParameterValue(support, "ADSK_Система_Классификация", systemClassification);
                SetStringParameterValue(support, "ADSK_Система_Тип", systemType);
                SetStringParameterValue(support, "ADSK_Система_Имя", systemName);
                SetStringParameterValue(support, "ADSK_Система_Сокращение", systemAbbreviation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка SetPipeParameters: {ex.Message}");
            }
        }

        private void SetDoubleParameterValue(FamilyInstance support, string paramName, double valueMm)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.Double)
                {
                    double valueInFeet = valueMm / 304.8;
                    param.Set(valueInFeet);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка SetDoubleParameterValue для {paramName}: {ex.Message}");
            }
        }

        private void SetStringParameterValue(FamilyInstance support, string paramName, string value)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                {
                    param.Set(value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка SetStringParameterValue для {paramName}: {ex.Message}");
            }
        }
    }
}