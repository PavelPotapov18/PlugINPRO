using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.VK;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class PipeSupportsByNumberCommand : IExternalCommand
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
                // 1. Показываем окно настроек для труб
                PipeFittingByNumberSettingsWindow settingsWindow = new PipeFittingByNumberSettingsWindow(doc);

                bool? dialogResult = null;

                try
                {
                    dialogResult = settingsWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка окна: {ex.Message}");
                    dialogResult = ShowSimpleDialog(doc);
                }

                if (dialogResult != true || settingsWindow.SelectedFamily == null)
                {
                    return Result.Cancelled;
                }

                // 2. Выбор трубы
                Reference pipeRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "Выберите трубопровод");

                Pipe pipe = doc.GetElement(pipeRef) as Pipe;

                if (pipe == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является трубопроводом");
                    return Result.Failed;
                }
                

                // 3. Получаем размеры трубы, угол наклона трубы по уклону в %
                PipeDimensions pipeDims = GetPipeDimensions(pipe);
                double slopeAngle = Math.Atan(pipeDims.Slope);

                //Параметры системы трубопровода
                Parameter systemClassification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                Parameter systemName = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = pipe.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 4. Получение кривой трубы
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию трубопровода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double curveLengthMeters = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);

                double spacingFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Spacing / Math.Cos(slopeAngle), UnitTypeId.Millimeters);
                double offsetFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Offset / Math.Cos(slopeAngle), UnitTypeId.Millimeters);

                // 5. Расчет точек размещения
                List<XYZ> placementPoints = CalculatePlacementPoints(curve, spacingFeet, offsetFeet, settingsWindow.Number);

                if (placementPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание",
                        $"Не удалось рассчитать точки размещения опор.\n" +
                        $"Длина трубопровода: {curveLengthMeters:F0} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Шаг: {settingsWindow.Spacing} мм");
                    return Result.Cancelled;
                }

                // 6. Определяем тип трубы (вертикальная/горизонтальная)
                XYZ pipeDirection = GetPipeDirection(curve);
                bool isVertical = Math.Abs(pipeDirection.Z) > 0.9;

                // 7. Получаем ориентацию для поворота опор
                XYZ rotationOrientation;

                if (isVertical)
                {
                    // Для ВЕРТИКАЛЬНЫХ труб
                    rotationOrientation = GetPipeWallOrientation(pipe);

                    TaskDialog.Show("Информация",
                        $"Вертикальный трубопровод\n" +
                        $"Диаметр: Ø{pipeDims.Diameter:F0} мм\n" +
                        $"Ориентация: X={rotationOrientation.X:F3}, Y={rotationOrientation.Y:F3}\n" +
                        $"Угол: {GetAngleFromVector(rotationOrientation):F1}°");
                }
                else
                {
                    // Для ГОРИЗОНТАЛЬНЫХ труб
                    rotationOrientation = new XYZ(pipeDirection.X, pipeDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 8. Размещение опор
                using (Transaction trans = new Transaction(doc, "Размещение опор трубопровода"))
                {
                    trans.Start();

                    // Активация семейства если не активно
                    if (!settingsWindow.SelectedFamily.IsActive)
                        settingsWindow.SelectedFamily.Activate();

                    // Размещение опор
                    int placedCount = 0;

                    foreach (XYZ point in placementPoints)
                    {
                        try
                        {
                            // Создаем опору по точке
                            FamilyInstance support = doc.Create.NewFamilyInstance(
                                point,
                                settingsWindow.SelectedFamily,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (support != null)
                            {
                                // Устанавливаем параметры
                                SetPipeParameters(support,
                                    pipeDims.Diameter,
                                    systemClassification.AsString(),
                                    doc.GetElement(systemType.AsElementId()).Name,
                                    systemName.AsString(),
                                    systemAbbreviation.AsString());

                                // ПОВОРАЧИВАЕМ опору
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

                    // Сохраняем выбранное семейство
                    if (!settingsWindow.UsePreviousFamily)
                    {
                        App.SelectedPipeSupportFamily = settingsWindow.SelectedFamily;
                    }

                    string resultMessage =
                        $"Установлено опор: {placedCount} из {placementPoints.Count}\n" +
                        $"Шаг: {settingsWindow.Spacing} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Тип: {(isVertical ? "Вертикальный" : "Горизонтальный")}\n" +
                        $"Диаметр трубы: Ø{pipeDims.Diameter:F0} мм\n" +
                        $"Уклон трубы: {pipeDims.Slope}%";

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

        // ========== ОСНОВНЫЕ МЕТОДЫ ПОВОРОТА ==========

        private void RotateSupport(FamilyInstance support, XYZ point, XYZ orientation)
        {
            try
            {
                double angleRad = GetAngleFromVector(orientation, true);

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

        private XYZ GetPipeWallOrientation(Pipe pipe)
        {
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = pipe.get_Geometry(options);

                List<XYZ> allNormals = new List<XYZ>();

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                XYZ normal = GetFaceNormalAtCenter(face);
                                if (normal.GetLength() > 0.001)
                                {
                                    allNormals.Add(normal);
                                }
                            }
                            catch { }
                        }
                    }
                }

                List<XYZ> wallNormals = new List<XYZ>();
                foreach (XYZ normal in allNormals)
                {
                    if (Math.Abs(normal.Z) < 0.3 && normal.GetLength() > 0.001)
                    {
                        wallNormals.Add(normal);
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

                    Debug.WriteLine($"Найдено нормалей трубы: {wallNormals.Count}");
                    Debug.WriteLine($"Направление опоры трубы: X={wallDirection.X:F3}, Y={wallDirection.Y:F3}");

                    return wallDirection;
                }

                return XYZ.BasisX;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка GetPipeWallOrientation: {ex.Message}");
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

        private double GetAngleFromVector(XYZ vector, bool inRadians = false)
        {
            double angle = Math.Atan2(vector.Y, vector.X);

            if (angle < 0)
                angle += 2 * Math.PI;

            return inRadians ? angle : angle * (180.0 / Math.PI);
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

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private List<XYZ> CalculatePlacementPoints(Curve curve, double spacing, double offset, int number)
        {
            List<XYZ> points = new List<XYZ>();

            try
            {
                double curveLength = curve.Length;

                if (offset >= curveLength)
                {
                    return points;
                }

                double calcLength = offset + spacing * (number - 1);

                if (calcLength >= curveLength)
                    calcLength = curveLength;

                double firstPointDistance = offset;
                double currentDistance = firstPointDistance;

                while (currentDistance <= calcLength)
                {
                    XYZ point = GetPointAtDistance(curve, currentDistance);
                    if (point != null)
                    {
                        points.Add(point);
                    }

                    currentDistance += spacing;

                    //if (currentDistance >= curveLength && points.Count > 0)
                    //{
                    //    double distanceToEnd = curveLength - (currentDistance - spacing);

                    //    // ✅ ИСПРАВЛЕНО: добавляем точку, если осталось больше 1 мм (0.00328 фута)
                    //    double minDistanceFeet = 1.0 / 304.8; // 1 мм в футах
                    //    if (distanceToEnd > minDistanceFeet)
                    //    {
                    //        // ✅ ИСПРАВЛЕНО: берем точку строго на конце
                    //        XYZ endPoint = GetPointAtDistance(curve, curveLength);
                    //        if (endPoint != null)
                    //        {
                    //            points.Add(endPoint);
                    //        }
                    //    }
                    //    break;
                    //}
                }

                //if (points.Count == 0 && offset < curveLength)
                //{
                //    double middleDistance = offset + (curveLength - offset) / 2;
                //    XYZ middlePoint = GetPointAtDistance(curve, middleDistance);
                //    if (middlePoint != null)
                //    {
                //        points.Add(middlePoint);
                //    }
                //}
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка расчета", $"Ошибка: {ex.Message}");
            }

            return points;
        }

        private XYZ GetPointAtDistance(Curve curve, double distance)
        {
            try
            {
                // ✅ ИСПРАВЛЕНО: допуск 0.001 фута (0.3 мм)
                double tolerance = 0.0001; // 0.03 мм
                if (distance < 0) distance = 0;
                if (distance > curve.Length) distance = curve.Length;

                // ✅ ИСПРАВЛЕНО: для конца используем точное значение
                if (Math.Abs(distance - curve.Length) < tolerance)
                {
                    return curve.GetEndPoint(1);
                }

                if (curve is Line line)
                {
                    XYZ start = line.GetEndPoint(0);
                    XYZ end = line.GetEndPoint(1);

                    // ✅ ИСПРАВЛЕНО: используем параметр t от 0 до 1
                    double t = distance / curve.Length;
                    t = Math.Max(0.0, Math.Min(1.0, t));

                    // Линейная интерполяция (точнее чем Evaluate для Line)
                    return new XYZ(
                        start.X + t * (end.X - start.X),
                        start.Y + t * (end.Y - start.Y),
                        start.Z + t * (end.Z - start.Z)
                    );
                }
                else
                {
                    // Для изогнутых кривых
                    double param = distance / curve.Length;
                    param = Math.Max(0.0, Math.Min(1.0, param));
                    return curve.Evaluate(param, false);
                }
            }
            catch
            {
                return curve.GetEndPoint(0);
            }
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Pipe;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private class PipeDimensions
        {
            public double Diameter { get; set; }
            public double Slope { get; set; }
        }

        private PipeDimensions GetPipeDimensions(Pipe pipe)
        {
            PipeDimensions dims = new PipeDimensions();

            try
            {
                Parameter diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                
                if (diameterParam != null && diameterParam.HasValue)
                {
                    dims.Diameter = UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters); 
                }
                else
                {
                    diameterParam = pipe.LookupParameter("Диаметр");
                    if (diameterParam != null && diameterParam.HasValue)
                    {
                        if (diameterParam.StorageType == StorageType.Double)
                            dims.Diameter = UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                        else if (diameterParam.StorageType == StorageType.String)
                        {
                            string stringValue = diameterParam.AsString();
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                double.TryParse(stringValue, out double result);
                                dims.Diameter = result;
                            }
                        }
                    }
                    else
                    {
                        dims.Diameter = 50.0;
                    }
                }
                Parameter slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                dims.Slope = slopeParam.AsDouble();
            }
            catch
            {
                dims.Diameter = 50.0;
                dims.Slope = 0.0;
            }

            return dims;
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
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.Double)
                    {
                        // Безопасный вызов без ref
                        double valueInFeet = valueMm / 304.8;
                        param.Set(valueInFeet);
                    }
                    else if (param.StorageType == StorageType.Integer)
                    {
                        int intValue = (int)Math.Round(valueMm);
                        param.Set(intValue);
                    }
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
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.String)
                    {
                        // Безопасный вызов без ref
                        param.Set(value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка SetStringParameterValue для {paramName}: {ex.Message}");
            }
        }

        private bool? ShowSimpleDialog(Document doc)
        {
            try
            {
                TaskDialog dialog = new TaskDialog("Настройки креплений трубопроводов");
                dialog.MainInstruction = "Выберите шаг опор:";
                dialog.MainContent = "Использовать предыдущее крепление или выбрать новое?";

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "1500 мм (использовать предыдущее)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "2000 мм (использовать предыдущее)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Выбрать новое крепление");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Отмена");

                TaskDialogResult result = dialog.Show();

                if (result == TaskDialogResult.CommandLink1 ||
                    result == TaskDialogResult.CommandLink2 ||
                    result == TaskDialogResult.CommandLink3)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}