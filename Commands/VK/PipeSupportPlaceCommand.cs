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
    public class PipeSupportPlaceCommand : IExternalCommand
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
                PipeSupportSelectWindow selectWindow = new PipeSupportSelectWindow(doc);
                bool? dialogResult = selectWindow.ShowDialog();

                if (dialogResult != true || selectWindow.SelectedFamily == null)
                {
                    return Result.Cancelled;
                }

                FamilySymbol selectedFamily = selectWindow.SelectedFamily;

                // 2. Выбираем трубу
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

                // 3. Получаем геометрию трубы
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию трубопровода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);
                double curveLengthFeet = curve.Length;
                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(curveLengthFeet, UnitTypeId.Millimeters);

                // Определяем уклон трубы
                XYZ pipeDirection = GetPipeDirection(curve);
                bool isVertical = Math.Abs(pipeDirection.Z) > 0.9;
                double slope = 0.0;
                if (!isVertical)
                {
                    slope = GetPipeSlope(pipe);
                }
                double slopeAngle = Math.Atan(slope);
                double slopeCorrection = Math.Cos(slopeAngle);

                // 4. Пользователь кликает на трубу, чтобы указать примерное место
                Reference clickRef = uidoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new PipeSelectionFilter(),
                    "Кликните на трубу в месте установки опоры");

                XYZ clickPoint = clickRef.GlobalPoint;
                if (clickPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить точку клика");
                    return Result.Failed;
                }

                // 5. Проецируем точку клика на ось трубы
                XYZ projectedPoint = GetClosestPointOnCurve(curve, clickPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось спроецировать точку на ось трубы");
                    return Result.Failed;
                }

                // 6. Вычисляем расстояние от начала и конца трубы
                double distanceFromStartFeet = GetDistanceAlongCurve(curve, projectedPoint);
                double distanceFromStartMm = UnitUtils.ConvertFromInternalUnits(distanceFromStartFeet, UnitTypeId.Millimeters);
                double distanceFromEndMm = totalLengthMm - distanceFromStartMm;

                // 7. Показываем окно с настройкой отступа
                PipeSupportPlaceSettingsWindow settingsWindow = new PipeSupportPlaceSettingsWindow(
                    distanceFromStartMm,
                    distanceFromEndMm,
                    totalLengthMm);

                bool? settingsResult = settingsWindow.ShowDialog();

                if (settingsResult != true)
                {
                    return Result.Cancelled;
                }

                // 8. Получаем настройки от пользователя
                double userOffsetMm = settingsWindow.UserOffset;
                bool isFromStart = settingsWindow.IsFromStart;
                bool projectToHorizontal = settingsWindow.ProjectToHorizontal;

                // ═══════════════════════════════════════════════════════════
                // 🔥 ПРАВИЛЬНАЯ ЛОГИКА РАСЧЕТА ОТСТУПА
                // ═══════════════════════════════════════════════════════════

                // 1. Переводим отступ пользователя в расстояние вдоль трубы
                double offsetAlongPipeMm;
                if (projectToHorizontal)
                {
                    // Галка включена: пользователь ввел ГОРИЗОНТАЛЬНОЕ расстояние
                    // Расстояние вдоль трубы = горизонтальное / cos(угла)
                    offsetAlongPipeMm = userOffsetMm / slopeCorrection;
                }
                else
                {
                    // Галка выключена: пользователь ввел расстояние вдоль трубы
                    offsetAlongPipeMm = userOffsetMm;
                }

                // 2. Считаем отступ от НАЧАЛА трубы (вдоль трубы)
                double offsetFromStartMm;
                if (isFromStart)
                {
                    // Пользователь считает от начала
                    offsetFromStartMm = offsetAlongPipeMm;
                }
                else
                {
                    // Пользователь считает от конца
                    // totalLengthMm - это уже длина вдоль оси трубы
                    offsetFromStartMm = totalLengthMm - offsetAlongPipeMm;
                }

                // 3. Проверяем, что отступ не выходит за пределы
                double maxOffsetAlongPipe = totalLengthMm;
                if (offsetFromStartMm < 0 || offsetFromStartMm > maxOffsetAlongPipe)
                {
                    double maxOffsetDisplay = projectToHorizontal ? totalLengthMm * slopeCorrection : totalLengthMm;
                    TaskDialog.Show("Внимание",
                        $"Отступ выходит за пределы трубы!\n" +
                        $"Максимальный отступ: {maxOffsetDisplay:F0} мм");
                    return Result.Cancelled;
                }

                // 4. Переводим в футы для Revit
                double offsetFromStartFeet = UnitUtils.ConvertToInternalUnits(offsetFromStartMm, UnitTypeId.Millimeters);

                // 5. Находим точку на кривой
                XYZ placePoint = GetPointAtDistance(curve, offsetFromStartFeet);
                if (placePoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось вычислить точку установки");
                    return Result.Failed;
                }

                // 10. Получаем параметры системы трубы
                Parameter systemClassification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                Parameter systemType = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                Parameter systemName = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                Parameter systemAbbreviation = pipe.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM);

                // 11. Получаем размер трубы
                double diameter = GetPipeDiameter(pipe);

                // 12. Получаем ориентацию для поворота
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

                // 13. Устанавливаем опору
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
                        SetPipeParameters(support,
                            diameter,
                            systemClassification?.AsString() ?? "",
                            systemType != null ? doc.GetElement(systemType.AsElementId())?.Name ?? "" : "",
                            systemName?.AsString() ?? "",
                            systemAbbreviation?.AsString() ?? "");

                        RotateSupport(support, placePoint, rotationOrientation);

                        trans.Commit();

                        string directionText = isFromStart ? "от начала" : "от конца";
                        string projectionInfo = projectToHorizontal
                            ? "с учетом уклона (горизонтальная проекция)"
                            : "по оси трубы (без учета уклона)";

                        // Показываем пользователю реальный отступ
                        double displayOffset;
                        if (isFromStart)
                        {
                            displayOffset = projectToHorizontal ? userOffsetMm : userOffsetMm;
                        }
                        else
                        {
                            // От конца: показываем горизонтальное расстояние от конца
                            double fromEndAlongPipe = totalLengthMm - offsetFromStartMm;
                            displayOffset = projectToHorizontal ? fromEndAlongPipe * slopeCorrection : fromEndAlongPipe;
                        }

                        TaskDialog.Show("Результат",
                            $"✅ Опора установлена!\n" +
                            $"Отступ: {displayOffset:F0} мм {directionText}\n" +
                            $"Расчет: {projectionInfo}\n" +
                            $"Позиция: X={placePoint.X:F2}, Y={placePoint.Y:F2}, Z={placePoint.Z:F2}\n" +
                            $"Диаметр трубы: Ø{diameter:F0} мм");
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

        // ====================================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ====================================================================

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Pipe;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
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