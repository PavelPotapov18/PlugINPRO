using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class PipeSlopeTriangleCommand : IExternalCommand
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
                // 1. Выбираем трубу
                Reference pipeRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "Выберите трубу для построения треугольника уклона");

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
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);

                // 3. Определяем, какой конец выше
                XYZ topPoint, bottomPoint;
                if (startPoint.Z > endPoint.Z)
                {
                    topPoint = startPoint;
                    bottomPoint = endPoint;
                }
                else
                {
                    topPoint = endPoint;
                    bottomPoint = startPoint;
                }

                // 4. Проверяем, что труба не горизонтальная
                double heightDiff = Math.Abs(topPoint.Z - bottomPoint.Z);
                if (heightDiff < 0.001)
                {
                    TaskDialog.Show("Информация", "Труба горизонтальная, уклон отсутствует");
                    return Result.Cancelled;
                }

                // 5. Получаем длину трубы и проекции
                double pipeLength = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);
                double heightDiffMm = UnitUtils.ConvertFromInternalUnits(heightDiff, UnitTypeId.Millimeters);

                double horizontalLengthMm = Math.Sqrt(pipeLength * pipeLength - heightDiffMm * heightDiffMm);

                // 6. Получаем уклон
                double slope = GetPipeSlope(pipe);
                double slopePercent = slope * 100;

                // 7. Находим точки для размеров
                double height = bottomPoint.Z;

                XYZ horizontalStart = new XYZ(bottomPoint.X, bottomPoint.Y, height);
                XYZ horizontalEnd = new XYZ(topPoint.X, topPoint.Y, height);

                XYZ verticalStart = new XYZ(topPoint.X, topPoint.Y, height);
                XYZ verticalEnd = new XYZ(topPoint.X, topPoint.Y, topPoint.Z);

                // 8. Получаем стиль линии для ModelLine
                GraphicsStyle lineStyle = GetLineStyle(doc, "ADSK_Штриховая_ш3 п1.5_Черная_1");

                // 9. Получаем тип размера
                DimensionType dimensionType = GetDimensionType(doc);

                // 10. Создаем размеры
                using (Transaction trans = new Transaction(doc, "Построить треугольник уклона"))
                {
                    trans.Start();

                    View view = uidoc.ActiveView;

                    // --- 10.1. ГОРИЗОНТАЛЬНЫЙ РАЗМЕР ---
                    Plane horizontalPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, height));
                    SketchPlane horizontalSketchPlane = SketchPlane.Create(doc, horizontalPlane);

                    Line horizontalModelLine = Line.CreateBound(horizontalStart, horizontalEnd);

                    SketchPlane originalSketchPlane = view.SketchPlane;
                    view.SketchPlane = horizontalSketchPlane;

                    ModelLine helperHorizontal = doc.Create.NewModelCurve(horizontalModelLine, horizontalSketchPlane) as ModelLine;

                    if (helperHorizontal != null)
                    {
                        // 🆕 Применяем стиль линии к ModelLine
                        if (lineStyle != null)
                            helperHorizontal.LineStyle = lineStyle;

                        XYZ offset = new XYZ(0, -0.3, 0);
                        XYZ dimStart = horizontalStart + offset;
                        XYZ dimEnd = horizontalEnd + offset;
                        Line dimLine = Line.CreateBound(dimStart, dimEnd);

                        ReferenceArray refArray = new ReferenceArray();
                        refArray.Append(helperHorizontal.GeometryCurve.GetEndPointReference(0));
                        refArray.Append(helperHorizontal.GeometryCurve.GetEndPointReference(1));

                        Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                        if (dimensionType != null)
                            dim.DimensionType = dimensionType;
                    }

                    // --- 10.2. ВЕРТИКАЛЬНЫЙ РАЗМЕР ---
                    XYZ midPoint = new XYZ(
                        (verticalStart.X + verticalEnd.X) / 2,
                        verticalStart.Y,
                        (verticalStart.Z + verticalEnd.Z) / 2
                    );

                    Plane verticalPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisY, midPoint);
                    SketchPlane verticalSketchPlane = SketchPlane.Create(doc, verticalPlane);

                    view.SketchPlane = verticalSketchPlane;

                    Line verticalModelLine = Line.CreateBound(verticalStart, verticalEnd);
                    ModelLine helperVertical = doc.Create.NewModelCurve(verticalModelLine, verticalSketchPlane) as ModelLine;

                    if (helperVertical != null)
                    {
                        // 🆕 Применяем стиль линии к ModelLine
                        if (lineStyle != null)
                            helperVertical.LineStyle = lineStyle;

                        XYZ offset = new XYZ(0.3, 0, 0);
                        XYZ dimStart = verticalStart + offset;
                        XYZ dimEnd = verticalEnd + offset;
                        Line dimLine = Line.CreateBound(dimStart, dimEnd);

                        ReferenceArray refArray = new ReferenceArray();
                        refArray.Append(helperVertical.GeometryCurve.GetEndPointReference(0));
                        refArray.Append(helperVertical.GeometryCurve.GetEndPointReference(1));

                        Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                        if (dimensionType != null)
                            dim.DimensionType = dimensionType;
                    }

                    view.SketchPlane = originalSketchPlane;

                    trans.Commit();
                }

                TaskDialog.Show("Результат",
                    $"✅ Построен треугольник уклона\n" +
                    $"Длина трубы: {pipeLength:F0} мм\n" +
                    $"Перепад высот: {heightDiffMm:F0} мм\n" +
                    $"Горизонтальная проекция: {horizontalLengthMm:F0} мм\n" +
                    $"Уклон: {slopePercent:F1}%");

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

        private DimensionType GetDimensionType(Document doc)
        {
            try
            {
                var dimensionTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(dt => dt.StyleType == DimensionStyleType.Linear)
                    .ToList();

                if (dimensionTypes.Count == 0)
                    return null;

                return dimensionTypes.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // 🆕 Метод для получения стиля линии по имени
        private GraphicsStyle GetLineStyle(Document doc, string styleName)
        {
            try
            {
                // Ищем все стили линий
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ICollection<Element> lineStyles = collector
                    .OfClass(typeof(GraphicsStyle))
                    .ToElements();

                foreach (GraphicsStyle style in lineStyles)
                {
                    if (style.Name == styleName)
                        return style;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}