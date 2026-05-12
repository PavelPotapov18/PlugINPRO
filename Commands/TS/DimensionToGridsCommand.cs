using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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
                // Выбираем первую ось
                Grid grid1 = doc.GetElement(uiDoc.Selection.PickObject(ObjectType.Element, new GridSelectionFilter(), "Выберите первую ось")) as Grid;

                // Выбираем вторую ось  
                Grid grid2 = doc.GetElement(uiDoc.Selection.PickObject(ObjectType.Element, new GridSelectionFilter(), "Выберите вторую ось")) as Grid;

                // Находим точку пересечения
                XYZ intersection = GetIntersection(grid1, grid2);
                if (intersection == null)
                {
                    TaskDialog.Show("Ошибка", "Оси не пересекаются");
                    return Result.Failed;
                }

                // Строим ВЕРТИКАЛЬНУЮ линию в точке пересечения
                using (Transaction trans = new Transaction(doc, "Построить вертикальную линию"))
                {
                    trans.Start();

                    XYZ start = new XYZ(intersection.X, intersection.Y, -5);
                    XYZ end = new XYZ(intersection.X, intersection.Y, 5);
                    Line line = Line.CreateBound(start, end);

                    // Плоскость для вертикальной линии
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisX, start);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    doc.Create.NewModelCurve(line, sketchPlane);

                    trans.Commit();
                }

                TaskDialog.Show("Готово", $"✅ Вертикальная линия построена в точке пересечения осей {grid1.Name} и {grid2.Name}\nКоординаты: X={intersection.X}, Y={intersection.Y}");
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

            return new XYZ(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y), 0);
        }

        private class GridSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Grid;
            public bool AllowReference(Reference refe, XYZ pos) => true;
        }
    }
}