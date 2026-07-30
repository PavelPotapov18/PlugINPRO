using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class PipeSupportAlignZCommand : IExternalCommand
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
                // 1. Выбираем трубу-эталон
                Reference pipeRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "Выберите трубу, к которой нужно выровнять опоры");

                Pipe pipe = doc.GetElement(pipeRef) as Pipe;

                if (pipe == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является трубопроводом");
                    return Result.Failed;
                }

                // 2. Получаем геометрию трубы
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию трубопровода");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double pipeDiameter = GetPipeDiameter(pipe);

                // 3. Выбираем опоры
                IList<Reference> supportRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new SupportSelectionFilter(),
                    "Выберите опоры для выравнивания (можно несколько)");

                if (supportRefs.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Опоры не выбраны");
                    return Result.Cancelled;
                }

                List<FamilyInstance> supports = new List<FamilyInstance>();
                List<string> details = new List<string>();

                foreach (Reference refElem in supportRefs)
                {
                    FamilyInstance support = doc.GetElement(refElem) as FamilyInstance;
                    if (support != null) supports.Add(support);
                }

                if (supports.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Не найдено подходящих опор");
                    return Result.Failed;
                }

                // 4. Выравниваем опоры
                using (Transaction trans = new Transaction(doc, "Выравнивание опор по Z"))
                {
                    trans.Start();

                    int alignedCount = 0;
                    int skippedCount = 0;

                    foreach (FamilyInstance support in supports)
                    {
                        try
                        {
                            LocationPoint locPoint = support.Location as LocationPoint;
                            if (locPoint == null)
                            {
                                details.Add($"❌ Опора {support.Id.IntegerValue}: не удалось получить Location");
                                skippedCount++;
                                continue;
                            }

                            XYZ currentPoint = locPoint.Point;
                            double currentZ = UnitUtils.ConvertFromInternalUnits(currentPoint.Z, UnitTypeId.Millimeters);

                            // ═══════════════════════════════════════════════
                            // 🔥 Строим вертикальную линию и ищем пересечение с трубой
                            // ═══════════════════════════════════════════════
                            XYZ zMin = new XYZ(currentPoint.X, currentPoint.Y, -1000); // 1000м вниз
                            XYZ zMax = new XYZ(currentPoint.X, currentPoint.Y, 1000);  // 1000м вверх
                            Line verticalLine = Line.CreateBound(zMin, zMax);

                            // Ищем пересечение вертикальной линии с трубой
                            XYZ intersectionPoint = GetLineCurveIntersection(curve, verticalLine);

                            if (intersectionPoint == null)
                            {
                                details.Add($"❌ Опора {support.Id.IntegerValue}: не на оси трубы (вертикальная линия не пересекает трубу)");
                                skippedCount++;
                                continue;
                            }

                            double targetZ = intersectionPoint.Z;
                            double targetZmm = UnitUtils.ConvertFromInternalUnits(targetZ, UnitTypeId.Millimeters);
                            double diffMm = targetZmm - currentZ;

                            details.Add($"🔄 Опора {support.Id.IntegerValue}: {currentZ:F0} → {targetZmm:F0} мм ({diffMm:F0} мм)");

                            // Если разница меньше 1 мм - пропускаем
                            if (Math.Abs(diffMm) < 1.0)
                            {
                                details.Add($"ℹ️ Опора {support.Id.IntegerValue}: уже на месте");
                                alignedCount++;
                                continue;
                            }

                            // Проверяем, что разница не слишком большая (максимум 10 метров)
                            if (Math.Abs(diffMm) > 10000)
                            {
                                details.Add($"❌ Опора {support.Id.IntegerValue}: слишком большое изменение ({diffMm:F0} мм)");
                                skippedCount++;
                                continue;
                            }

                            // 🆕 Новая точка - старый X и Y, НОВЫЙ Z (только Z меняется!)
                            XYZ newPoint = new XYZ(currentPoint.X, currentPoint.Y, targetZ);

                            // Перемещаем опору (только по Z)
                            ElementTransformUtils.MoveElement(doc, support.Id, newPoint - currentPoint);
                            alignedCount++;

                            // Проверяем диаметр опоры
                            double supportDiameter = GetSupportDiameter(support);
                            bool diameterMismatch = Math.Abs(supportDiameter - pipeDiameter) > 1.0;

                            if (diameterMismatch && supportDiameter > 0)
                            {
                                SetSupportDiameter(support, pipeDiameter);
                                details.Add($"ℹ️ Опора {support.Id.IntegerValue}: диаметр {supportDiameter:F0}→{pipeDiameter:F0} мм");
                            }
                        }
                        catch (Exception ex)
                        {
                            details.Add($"❌ Опора {support.Id.IntegerValue}: {ex.Message}");
                            skippedCount++;
                        }
                    }

                    trans.Commit();

                    string resultMessage =
                        $"✅ Выровнено опор: {alignedCount}\n" +
                        $"⚠ Пропущено: {skippedCount}\n" +
                        $"📏 Диаметр трубы: Ø{pipeDiameter:F0} мм\n\n" +
                        $"Детали:\n" + string.Join("\n", details.Take(20));

                    if (details.Count > 20)
                        resultMessage += $"\n... и еще {details.Count - 20} сообщений";

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

        // ====================================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ====================================================================

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Pipe;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

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
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        /// <summary>
        /// Находит пересечение кривой трубы и вертикальной линии
        /// </summary>
        private XYZ GetLineCurveIntersection(Curve curve, Line verticalLine)
        {
            try
            {
                // Проверяем пересечение в обоих направлениях
                IntersectionResultArray results;
                SetComparisonResult result = curve.Intersect(verticalLine, out results);

                if (result == SetComparisonResult.Overlap || result == SetComparisonResult.Subset)
                {
                    if (results != null && results.Size > 0)
                    {
                        // Берем первую точку пересечения
                        XYZ point = results.get_Item(0).XYZPoint;
                        return point;
                    }
                }

                // Если не нашли пересечение - пытаемся найти ближайшую точку с теми же X и Y
                // Разбиваем кривую на много отрезков
                int segments = 500;
                double minDist = double.MaxValue;
                XYZ bestPoint = null;

                for (int i = 0; i <= segments; i++)
                {
                    double t = (double)i / segments;
                    XYZ curvePoint = curve.Evaluate(t, true);

                    // Расстояние только по X и Y
                    double dx = curvePoint.X - verticalLine.GetEndPoint(0).X;
                    double dy = curvePoint.Y - verticalLine.GetEndPoint(0).Y;
                    double distXY = Math.Sqrt(dx * dx + dy * dy);

                    if (distXY < minDist)
                    {
                        minDist = distXY;
                        bestPoint = curvePoint;
                    }
                }

                // Если расстояние по XY меньше 1 мм - считаем что пересечение есть
                if (bestPoint != null && minDist < 0.001)
                {
                    return bestPoint;
                }

                return null;
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
                }
                return 50.0;
            }
            catch
            {
                return 50.0;
            }
        }

        private double GetSupportDiameter(FamilyInstance support)
        {
            try
            {
                string[] paramNames = new string[]
                {
                    "D", "DN", "Диаметр", "Diameter", "PIPE_D", "PIPE_DIAMETER",
                    "Размер_трубы", "Pipe_Size", "Size"
                };

                foreach (string name in paramNames)
                {
                    Parameter param = support.LookupParameter(name);
                    if (param != null && param.HasValue)
                    {
                        if (param.StorageType == StorageType.Double)
                            return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.Millimeters);
                        else if (param.StorageType == StorageType.String)
                        {
                            if (double.TryParse(param.AsString(), out double result))
                                return result;
                        }
                        else if (param.StorageType == StorageType.Integer)
                            return param.AsInteger();
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private void SetSupportDiameter(FamilyInstance support, double diameterMm)
        {
            try
            {
                string[] paramNames = new string[]
                {
                    "D", "DN", "Диаметр", "Diameter", "PIPE_D", "PIPE_DIAMETER",
                    "Размер_трубы", "Pipe_Size", "Size"
                };

                foreach (string name in paramNames)
                {
                    Parameter param = support.LookupParameter(name);
                    if (param != null && !param.IsReadOnly)
                    {
                        if (param.StorageType == StorageType.Double)
                        {
                            param.Set(UnitUtils.ConvertToInternalUnits(diameterMm, UnitTypeId.Millimeters));
                            break;
                        }
                        else if (param.StorageType == StorageType.String)
                        {
                            param.Set(diameterMm.ToString("F0"));
                            break;
                        }
                        else if (param.StorageType == StorageType.Integer)
                        {
                            param.Set((int)Math.Round(diameterMm));
                            break;
                        }
                    }
                }
            }
            catch { }
        }
    }
}