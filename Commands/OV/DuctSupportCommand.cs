using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO;
using PlugINPRO.UI.Windows.OV;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace PlugINPRO.Commands.OV
{
    [Transaction(TransactionMode.Manual)]
    public class DuctSupportsCommand : IExternalCommand
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
                DuctFittingSettingsWindow settingsWindow = new DuctFittingSettingsWindow(doc);

                bool? dialogResult = null;

                try
                {
                    dialogResult = settingsWindow.ShowDialog();
                }
                catch
                {
                    dialogResult = ShowSimpleDialog(doc);
                }

                if (dialogResult != true || settingsWindow.SelectedFamily == null)
                {
                    return Result.Cancelled;
                }

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

                // 3. Получаем размеры воздуховода
                DuctDimensions ductDims = GetDuctDimensions(duct);
                //Параметры системы трубопровода
                Parameter systemClassification = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                Parameter systemName = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = duct.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 4. Получение кривой воздуховода
                LocationCurve locationCurve = duct.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию воздуховода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double spacingFeet = settingsWindow.Spacing / 304.8;
                double offsetFeet = settingsWindow.Offset / 304.8;

                // 5. Расчет точек размещения
                List<XYZ> placementPoints = CalculatePlacementPoints(curve, spacingFeet, offsetFeet);

                if (placementPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание",
                        $"Не удалось рассчитать точки размещения опор.\n" +
                        $"Длина воздуховода: {curve.Length * 304.8:F0} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Шаг: {settingsWindow.Spacing} мм");
                    return Result.Cancelled;
                }

                // 6. Определяем тип воздуховода (вертикальный/горизонтальный)
                XYZ ductDirection = GetDuctDirection(curve);
                bool isVertical = Math.Abs(ductDirection.Z) > 0.9;

                // 7. Получаем ориентацию для поворота опор
                XYZ rotationOrientation;

                if (isVertical && !ductDims.IsRound)
                {
                    // Для ВЕРТИКАЛЬНЫХ ПРЯМОУГОЛЬНЫХ воздуховодов
                    // Ищем ориентацию из геометрии стенок
                    rotationOrientation = GetDuctWallOrientation(duct);

                    TaskDialog.Show("Информация",
                        $"Вертикальный прямоугольный воздуховод\n" +
                        $"Размер: {ductDims.Width:F0}×{ductDims.Height:F0} мм\n" +
                        $"Ориентация стенки: X={rotationOrientation.X:F3}, Y={rotationOrientation.Y:F3}\n" +
                        $"Угол: {GetAngleFromVector(rotationOrientation):F1}°");
                }
                else if (isVertical && ductDims.IsRound)
                {
                    // Для ВЕРТИКАЛЬНЫХ КРУГЛЫХ воздуховодов
                    // Круглые можно поворачивать произвольно, берем ось X
                    rotationOrientation = XYZ.BasisX;
                }
                else
                {
                    // Для ГОРИЗОНТАЛЬНЫХ воздуховодов
                    // Поворачиваем по направлению воздуховода
                    rotationOrientation = new XYZ(ductDirection.X, ductDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 8. Размещение опор
                using (Transaction trans = new Transaction(doc, "Размещение опор воздуховода"))
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
                                if (ductDims.IsRound)
                                {
                                    SetDiameterParameter(support, ductDims.Diameter);
                                }
                                else
                                {
                                    SetRectangularParameters(support, ductDims.Width, ductDims.Height);
                                }

                                SetSystemParameters(
                                    support,
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
                        App.SelectedSupportFamily = settingsWindow.SelectedFamily;
                    }

                    string resultMessage =
                        $"Установлено опор: {placedCount} из {placementPoints.Count}\n" +
                        $"Шаг: {settingsWindow.Spacing} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Тип: {(isVertical ? "Вертикальный" : "Горизонтальный")}\n" +
                        $"Сечение: {(ductDims.IsRound ? "Круглое" : "Прямоугольное")}";

                    if (!ductDims.IsRound)
                    {
                        resultMessage += $"\nРазмер: {ductDims.Width:F0}×{ductDims.Height:F0} мм";
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
                TaskDialog.Show("Ошибка", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        // ========== ОСНОВНЫЕ МЕТОДЫ ПОВОРОТА ==========

        // Метод поворота опоры
        private void RotateSupport(FamilyInstance support, XYZ point, XYZ orientation)
        {
            try
            {
                // Вычисляем угол из вектора ориентации
                double angleRad = GetAngleFromVector(orientation, true);

                // Создаем вертикальную ось вращения
                XYZ axisStart = point;
                XYZ axisEnd = new XYZ(point.X, point.Y, point.Z + 1.0);
                Line axis = Line.CreateBound(axisStart, axisEnd);

                // Поворачиваем
                ElementTransformUtils.RotateElement(support.Document, support.Id, axis, angleRad);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка поворота: {ex.Message}");
            }
        }

        // Получение ориентации стенки воздуховода из геометрии
        private XYZ GetDuctWallOrientation(Duct duct)
        {
            try
            {
                // Получаем геометрию воздуховода
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = duct.get_Geometry(options);

                // Собираем нормали ВСЕХ граней
                List<XYZ> allNormals = new List<XYZ>();

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                // Получаем нормаль в центре грани
                                XYZ normal = GetFaceNormalAtCenter(face);
                                if (normal != null)
                                {
                                    allNormals.Add(normal);
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Ищем ГОРИЗОНТАЛЬНЫЕ нормали (стенки)
                List<XYZ> wallNormals = new List<XYZ>();
                foreach (XYZ normal in allNormals)
                {
                    // Стенка: нормаль почти горизонтальная (|Z| < 0.3)
                    if (Math.Abs(normal.Z) < 0.3)
                    {
                        wallNormals.Add(normal);
                    }
                }

                if (wallNormals.Count > 0)
                {
                    // Берем первую найденную нормаль стенки
                    XYZ wallNormal = wallNormals[0];

                    // Нормаль перпендикулярна стенке, 
                    // для направления вдоль стенки нужно повернуть на 90°
                    XYZ wallDirection = new XYZ(-wallNormal.Y, wallNormal.X, 0);

                    // Нормализуем
                    wallDirection = wallDirection.Normalize();

                    // Отладочная информация
                    Debug.WriteLine($"Найдено стенок: {wallNormals.Count}");
                    Debug.WriteLine($"Нормаль стенки: X={wallNormal.X:F3}, Y={wallNormal.Y:F3}, Z={wallNormal.Z:F3}");
                    Debug.WriteLine($"Направление стенки: X={wallDirection.X:F3}, Y={wallDirection.Y:F3}");
                    Debug.WriteLine($"Угол: {GetAngleFromVector(wallDirection):F1}°");

                    return wallDirection;
                }

                // Если не нашли стенки, пробуем другой метод
                return GetDuctOrientationFromEdges(duct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка GetDuctWallOrientation: {ex.Message}");
                return GetDuctOrientationFromEdges(duct);
            }
        }

        // Получение нормали грани в центре
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
                return null;
            }
        }

        // Получение ориентации из ребер воздуховода
        private XYZ GetDuctOrientationFromEdges(Duct duct)
        {
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                GeometryElement geometry = duct.get_Geometry(options);

                List<XYZ> edgeVectors = new List<XYZ>();

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid)
                    {
                        foreach (Edge edge in solid.Edges)
                        {
                            Curve curve = edge.AsCurve();

                            if (curve is Line line)
                            {
                                XYZ start = line.GetEndPoint(0);
                                XYZ end = line.GetEndPoint(1);
                                XYZ vector = end - start;

                                // Если ребро почти вертикальное (Z значительный, X,Y маленькие)
                                if (Math.Abs(vector.Z) > 0.1 &&
                                    Math.Abs(vector.X) < 0.01 &&
                                    Math.Abs(vector.Y) < 0.01)
                                {
                                    // Это вертикальное ребро, его положение (X,Y) важно
                                    edgeVectors.Add(new XYZ(start.X, start.Y, 0));
                                }
                            }
                        }
                    }
                }

                // Если нашли хотя бы 2 вертикальных ребра
                if (edgeVectors.Count >= 2)
                {
                    // Сортируем по X, потом по Y
                    edgeVectors.Sort((a, b) =>
                    {
                        int cmp = a.X.CompareTo(b.X);
                        return cmp != 0 ? cmp : a.Y.CompareTo(b.Y);
                    });

                    // Берем два самых разных ребра
                    XYZ edge1 = edgeVectors[0];
                    XYZ edge2 = edgeVectors[edgeVectors.Count - 1];

                    // Вектор между ребрами показывает ориентацию
                    XYZ wallVector = edge2 - edge1;

                    if (wallVector.GetLength() > 0.01)
                    {
                        XYZ direction = wallVector.Normalize();
                        Debug.WriteLine($"Ориентация из ребер: X={direction.X:F3}, Y={direction.Y:F3}");
                        return direction;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка GetDuctOrientationFromEdges: {ex.Message}");
            }

            // По умолчанию - ось X
            return XYZ.BasisX;
        }

        // Получение угла из вектора (0° = ось X, 90° = ось Y)
        private double GetAngleFromVector(XYZ vector, bool inRadians = false)
        {
            double angle = Math.Atan2(vector.Y, vector.X);

            // Приводим к диапазону 0-2π
            if (angle < 0)
                angle += 2 * Math.PI;

            return inRadians ? angle : angle * (180.0 / Math.PI);
        }

        // Получение направления воздуховода из кривой
        private XYZ GetDuctDirection(Curve curve)
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

        // Расчет точек размещения
        private List<XYZ> CalculatePlacementPoints(Curve curve, double spacing, double offset)
        {
            List<XYZ> points = new List<XYZ>();

            try
            {
                double curveLength = curve.Length;

                if (offset >= curveLength)
                {
                    return points;
                }

                double firstPointDistance = offset;
                double currentDistance = firstPointDistance;

                while (currentDistance < curveLength)
                {
                    XYZ point = GetPointAtDistance(curve, currentDistance);
                    if (point != null)
                    {
                        points.Add(point);
                    }

                    currentDistance += spacing;

                    if (currentDistance >= curveLength && points.Count > 0)
                    {
                        double distanceToEnd = curveLength - (currentDistance - spacing);

                        // ✅ ИСПРАВЛЕНО: добавляем точку, если осталось больше 1 мм (0.00328 фута)
                        double minDistanceFeet = 1.0 / 304.8; // 1 мм в футах
                        if (distanceToEnd > minDistanceFeet)
                        {
                            // ✅ ИСПРАВЛЕНО: берем точку строго на конце
                            XYZ endPoint = GetPointAtDistance(curve, curveLength);
                            if (endPoint != null)
                            {
                                points.Add(endPoint);
                            }
                        }
                        break;
                    }
                }

                if (points.Count == 0 && offset < curveLength)
                {
                    double middleDistance = offset + (curveLength - offset) / 2;
                    XYZ middlePoint = GetPointAtDistance(curve, middleDistance);
                    if (middlePoint != null)
                    {
                        points.Add(middlePoint);
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка расчета", $"Ошибка: {ex.Message}");
            }

            return points;
        }

        // Получение точки на кривой по расстоянию
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

        // Фильтр выбора воздуховодов
        private class DuctSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Duct;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        // Класс для размеров воздуховода
        private class DuctDimensions
        {
            public double Width { get; set; }
            public double Height { get; set; }
            public double Diameter { get; set; }
            public bool IsRound { get; set; }
        }

        // Получение размеров воздуховода
        private DuctDimensions GetDuctDimensions(Duct duct)
        {
            DuctDimensions dims = new DuctDimensions();

            try
            {
                // Диаметр для круглых
                Parameter diameterParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (diameterParam != null && diameterParam.HasValue)
                {
                    dims.Diameter = diameterParam.AsDouble() * 304.8;
                    dims.IsRound = true;
                    return dims;
                }

                // Ширина и высота для прямоугольных
                Parameter widthParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                Parameter heightParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

                if (widthParam != null && widthParam.HasValue &&
                    heightParam != null && heightParam.HasValue)
                {
                    dims.Width = widthParam.AsDouble() * 304.8;
                    dims.Height = heightParam.AsDouble() * 304.8;
                    dims.IsRound = false;
                    return dims;
                }

                dims.Diameter = 100.0;
                dims.IsRound = true;
                return dims;
            }
            catch
            {
                dims.Diameter = 100.0;
                dims.IsRound = true;
                return dims;
            }
        }

        // Установка параметров прямоугольных воздуховодов
        private void SetRectangularParameters(FamilyInstance support, double widthMm, double heightMm)
        {
            try
            {
                SetParameterValue(support, "DUCT_H", heightMm);
                SetParameterValue(support, "DUCT_B", widthMm);
                SetParameterValue(support, "H", heightMm);
                SetParameterValue(support, "B", widthMm);
                SetParameterValue(support, "Height", heightMm);
                SetParameterValue(support, "Width", widthMm);
                SetParameterValue(support, "Высота", heightMm);
                SetParameterValue(support, "Ширина", widthMm);
            }
            catch { }
        }

        // Установка значения параметра
        private void SetParameterValue(FamilyInstance support, string paramName, double valueMm)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.Double)
                        param.Set(valueMm / 304.8);
                    else if (param.StorageType == StorageType.String)
                        param.Set(valueMm.ToString("F0"));
                    else if (param.StorageType == StorageType.Integer)
                        param.Set((int)Math.Round(valueMm));
                }
            }
            catch { }
        }

        // Установка параметра диаметра
        private void SetDiameterParameter(FamilyInstance support, double diameterMm)
        {
            SetParameterValue(support, "D", diameterMm);
            SetParameterValue(support, "Диаметр", diameterMm);
            SetParameterValue(support, "Diameter", diameterMm);
        }

        private void SetSystemParameters(
            FamilyInstance support,
            string systemClassification,
            string systemType,
            string systemName,
            string systemAbbreviation)
        {
            SetStringParameterValue(support, "ADSK_Система_Классификация", systemClassification);
            SetStringParameterValue(support, "ADSK_Система_Тип", systemType);
            SetStringParameterValue(support, "ADSK_Система_Имя", systemName);
            SetStringParameterValue(support, "ADSK_Система_Сокращение", systemAbbreviation);
        }

        private void SetStringParameterValue(FamilyInstance support, string paramName, string value)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.String)
                        param.Set(value);
                }
            }
            catch { }
        }
        // Простой диалог (запасной вариант)
        private bool? ShowSimpleDialog(Document doc)
        {
            try
            {
                TaskDialog dialog = new TaskDialog("Настройки креплений");
                dialog.MainInstruction = "Выберите шаг опор:";
                dialog.MainContent = "Использовать предыдущее крепление или выбрать новое?";

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "2000 мм (использовать предыдущее)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "2500 мм (использовать предыдущее)");
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