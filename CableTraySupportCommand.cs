using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Windows;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class CableTraySupportsCommand : IExternalCommand
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
                // 1. Показываем окно настроек для лотков
                CableTrayFittingSettingsWindow settingsWindow = new CableTrayFittingSettingsWindow(doc);

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

                // 2. Выбор кабельного лотка
                Reference trayRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new CableTraySelectionFilter(),
                    "Выберите кабельный лоток");

                CableTray tray = doc.GetElement(trayRef) as CableTray;

                if (tray == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является кабельным лотком");
                    return Result.Failed;
                }

                // 3. Получаем размеры лотка
                CableTrayDimensions trayDims = GetCableTrayDimensions(tray);

                // 4. Получение кривой лотка
                LocationCurve locationCurve = tray.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию лотка");
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
                        $"Длина лотка: {curve.Length * 304.8:F0} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Шаг: {settingsWindow.Spacing} мм");
                    return Result.Cancelled;
                }

                // 6. Определяем тип лотка (вертикальный/горизонтальный)
                XYZ trayDirection = GetTrayDirection(curve);
                bool isVertical = Math.Abs(trayDirection.Z) > 0.9;

                // 7. Получаем ориентацию для поворота опор
                XYZ rotationOrientation;

                if (isVertical)
                {
                    // Для ВЕРТИКАЛЬНЫХ лотков ищем ориентацию из геометрии
                    rotationOrientation = GetTrayWallOrientation(tray);

                    TaskDialog.Show("Информация",
                        $"Вертикальный лоток\n" +
                        $"Размер: {trayDims.Width:F0}×{trayDims.Height:F0} мм\n" +
                        $"Ориентация: X={rotationOrientation.X:F3}, Y={rotationOrientation.Y:F3}\n" +
                        $"Угол: {GetAngleFromVector(rotationOrientation):F1}°");
                }
                else
                {
                    // Для ГОРИЗОНТАЛЬНЫХ лотков
                    rotationOrientation = new XYZ(trayDirection.X, trayDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                // 8. Размещение опор
                using (Transaction trans = new Transaction(doc, "Размещение опор кабельного лотка"))
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
                                SetCableTrayParameters(support, trayDims.Width, trayDims.Height);

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
                        App.SelectedCableTraySupportFamily = settingsWindow.SelectedFamily;
                    }

                    string resultMessage =
                        $"Установлено опор: {placedCount} из {placementPoints.Count}\n" +
                        $"Шаг: {settingsWindow.Spacing} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Тип: {(isVertical ? "Вертикальный" : "Горизонтальный")}\n" +
                        $"Размер лотка: {trayDims.Width:F0}×{trayDims.Height:F0} мм";

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

        // Получение ориентации стенки лотка из геометрии
        private XYZ GetTrayWallOrientation(CableTray tray)
        {
            try
            {
                // Получаем геометрию лотка
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = tray.get_Geometry(options);

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

                    Debug.WriteLine($"Найдено стенок лотка: {wallNormals.Count}");
                    Debug.WriteLine($"Направление стенки лотка: X={wallDirection.X:F3}, Y={wallDirection.Y:F3}");

                    return wallDirection;
                }

                // По умолчанию - ось X
                return XYZ.BasisX;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка GetTrayWallOrientation: {ex.Message}");
                return XYZ.BasisX;
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

        // Получение угла из вектора
        private double GetAngleFromVector(XYZ vector, bool inRadians = false)
        {
            double angle = Math.Atan2(vector.Y, vector.X);

            if (angle < 0)
                angle += 2 * Math.PI;

            return inRadians ? angle : angle * (180.0 / Math.PI);
        }

        // Получение направления лотка из кривой
        private XYZ GetTrayDirection(Curve curve)
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

        // Фильтр выбора кабельных лотков
        private class CableTraySelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is CableTray;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        // Класс для размеров лотка
        private class CableTrayDimensions
        {
            public double Width { get; set; }
            public double Height { get; set; }
        }

        // Получение размеров лотка
        private CableTrayDimensions GetCableTrayDimensions(CableTray tray)
        {
            CableTrayDimensions dims = new CableTrayDimensions();

            try
            {
                // Ширина лотка
                Parameter widthParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                if (widthParam != null && widthParam.HasValue)
                {
                    dims.Width = widthParam.AsDouble() * 304.8;
                }
                else
                {
                    dims.Width = 100.0;
                }

                // Высота лотка
                Parameter heightParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                if (heightParam != null && heightParam.HasValue)
                {
                    dims.Height = heightParam.AsDouble() * 304.8;
                }
                else
                {
                    dims.Height = 50.0;
                }
            }
            catch
            {
                dims.Width = 100.0;
                dims.Height = 50.0;
            }

            return dims;
        }

        // Установка параметров лотка
        private void SetCableTrayParameters(FamilyInstance support, double widthMm, double heightMm)
        {
            try
            {
                SetParameterValue(support, "CABLETRAY_H", heightMm);
                SetParameterValue(support, "CABLETRAY_B", widthMm);
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

        // Простой диалог
        private bool? ShowSimpleDialog(Document doc)
        {
            try
            {
                TaskDialog dialog = new TaskDialog("Настройки креплений лотков");
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