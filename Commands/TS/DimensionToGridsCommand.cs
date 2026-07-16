using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace PlugINPRO.Commands.TS
{
    [Transaction(TransactionMode.Manual)]
    public class DimensionToGridsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // 1. Выбираем первую ось
                Reference gridRef1 = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new GridSelectionFilter(),
                    "Выберите первую ось");
                Grid grid1 = doc.GetElement(gridRef1) as Grid;

                // 2. Выбираем вторую ось
                Reference gridRef2 = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new GridSelectionFilter(),
                    "Выберите вторую ось");
                Grid grid2 = doc.GetElement(gridRef2) as Grid;

                if (grid1 == null || grid2 == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить оси");
                    return Result.Failed;
                }

                // 3. Находим точку пересечения осей
                XYZ intersection = GetIntersection(grid1, grid2);
                if (intersection == null)
                {
                    TaskDialog.Show("Ошибка", "Оси не пересекаются");
                    return Result.Failed;
                }

                // 4. Выбираем точку на элементе
                Reference refElement = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new ElementSelectionFilter(),
                    "Выберите точку на элементе");

                Element selectedElement = doc.GetElement(refElement);
                if (selectedElement == null)
                {
                    TaskDialog.Show("Ошибка", "Элемент не найден");
                    return Result.Failed;
                }

                // 5. Получаем координаты выбранной точки
                XYZ elementPoint = refElement.GlobalPoint;
                if (elementPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить точку");
                    return Result.Failed;
                }

                // 6. Получаем направления осей
                XYZ grid1Dir = GetGridDirection(grid1);
                XYZ grid2Dir = GetGridDirection(grid2);

                // 7. Строим вертикальную линию и размеры
                using (Transaction trans = new Transaction(doc, "Построить вертикальную линию и размеры"))
                {
                    trans.Start();

                    // --- 7.1. Строим ВЕРТИКАЛЬНУЮ линию в точке пересечения осей ---
                    XYZ vertStart = new XYZ(intersection.X, intersection.Y, -5);
                    XYZ vertEnd = new XYZ(intersection.X, intersection.Y, 5);
                    Line verticalLine = Line.CreateBound(vertStart, vertEnd);

                    Plane vertPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisX, vertStart);
                    SketchPlane vertSketchPlane = SketchPlane.Create(doc, vertPlane);
                    ModelLine vertHelperLine = doc.Create.NewModelCurve(verticalLine, vertSketchPlane) as ModelLine;

                    if (vertHelperLine == null)
                    {
                        TaskDialog.Show("Ошибка", "Не удалось создать вертикальную линию");
                        return Result.Failed;
                    }

                    // Высота, на которой будут размеры
                    double height = elementPoint.Z;

                    // Получаем Reference от вертикальной линии
                    Reference vertRef = vertHelperLine.GeometryCurve.GetEndPointReference(0);

                    // Для размера используем Reference от выбранной грани/ребра
                    Reference elemRef = refElement;

                    // --- 7.2. ПЕРВЫЙ РАЗМЕР: по X ---
                    XYZ projX = new XYZ(elementPoint.X, intersection.Y, height);
                    XYZ startDimX = new XYZ(intersection.X, intersection.Y, height);

                    if (startDimX.DistanceTo(projX) > 0.001)
                    {
                        Line dimLineX = Line.CreateBound(startDimX, projX);

                        ReferenceArray refArrayX = new ReferenceArray();
                        refArrayX.Append(vertRef);
                        refArrayX.Append(elemRef);

                        Dimension dimX = doc.Create.NewDimension(uiDoc.ActiveView, dimLineX, refArrayX);
                    }

                    // --- 7.3. ВТОРОЙ РАЗМЕР: по Y ---
                    XYZ projY = new XYZ(intersection.X, elementPoint.Y, height);
                    XYZ startDimY = new XYZ(intersection.X, intersection.Y, height);

                    if (startDimY.DistanceTo(projY) > 0.001)
                    {
                        Line dimLineY = Line.CreateBound(startDimY, projY);

                        ReferenceArray refArrayY = new ReferenceArray();
                        refArrayY.Append(vertRef);
                        refArrayY.Append(elemRef);

                        Dimension dimY = doc.Create.NewDimension(uiDoc.ActiveView, dimLineY, refArrayY);
                    }

                    trans.Commit();
                }

                TaskDialog.Show("Готово",
                    $"✅ Построена вертикальная линия в пересечении осей {grid1.Name} и {grid2.Name}\n" +
                    $"✅ Построены 2 размера до выбранной точки\n" +
                    $"Точка: X={elementPoint.X:F2}, Y={elementPoint.Y:F2}, Z={elementPoint.Z:F2}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.Message);
                return Result.Failed;
            }
        }

        // ====================================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ====================================================================

        private XYZ GetIntersection(Grid g1, Grid g2)
        {
            Curve c1 = g1.Curve;
            Curve c2 = g2.Curve;

            XYZ p1 = c1.GetEndPoint(0);
            XYZ p2 = c1.GetEndPoint(1);
            XYZ p3 = c2.GetEndPoint(0);
            XYZ p4 = c2.GetEndPoint(1);

            double d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
            if (Math.Abs(d) < 0.0001) return null;

            double t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;

            return new XYZ(
                p1.X + t * (p2.X - p1.X),
                p1.Y + t * (p2.Y - p1.Y),
                0
            );
        }

        private XYZ GetGridDirection(Grid grid)
        {
            Curve curve = grid.Curve;
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ direction = (end - start).Normalize();
            return new XYZ(direction.X, direction.Y, 0).Normalize();
        }

        private XYZ GetPointFromElement(Element element, Reference reference)
        {
            try
            {
                // Если есть GlobalPoint - берем его
                if (reference.GlobalPoint != null)
                {
                    return reference.GlobalPoint;
                }

                // Если элемент имеет LocationPoint
                LocationPoint locPoint = element.Location as LocationPoint;
                if (locPoint != null && locPoint.Point != null)
                {
                    return locPoint.Point;
                }

                // Если элемент имеет LocationCurve - берем середину
                LocationCurve locCurve = element.Location as LocationCurve;
                if (locCurve != null)
                {
                    Curve curve = locCurve.Curve;
                    double midParam = (curve.GetEndParameter(0) + curve.GetEndParameter(1)) / 2;
                    return curve.Evaluate(midParam, true);
                }

                // Если ничего не помогает - берем центр BoundingBox
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox != null)
                {
                    return (bbox.Min + bbox.Max) / 2;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        // ФИЛЬТРЫ ВЫБОРА
        // ====================================================================

        private class GridSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Grid;
            public bool AllowReference(Reference refe, XYZ pos) => true;
        }

        private class ElementSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null)
                    return false;

                int[] forbiddenCategories = new int[]
                {
                    (int)BuiltInCategory.OST_Levels,
                    (int)BuiltInCategory.OST_Grids,
                    (int)BuiltInCategory.OST_Views,
                    (int)BuiltInCategory.OST_Sheets,
                    (int)BuiltInCategory.OST_TitleBlocks,
                    (int)BuiltInCategory.OST_Viewports,
                    (int)BuiltInCategory.OST_Cameras,
                    (int)BuiltInCategory.OST_AreaSchemes
                };

                int catId = elem.Category.Id.IntegerValue;
                foreach (int forbiddenId in forbiddenCategories)
                {
                    if (catId == forbiddenId)
                        return false;
                }

                return true;
            }
            public bool AllowReference(Reference refe, XYZ pos)
            {
                // Разрешаем все Reference (грани, ребра, точки)
                return true;
            }
        }
    }
}