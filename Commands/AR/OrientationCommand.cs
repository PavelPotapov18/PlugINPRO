using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class OrientationCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            //Получаем базовую точку проекта, берем угол к истинному Северу и строим вектор
            BasePoint basePoint = BasePoint.GetProjectBasePoint(doc);
            Parameter trueNorthAngle = basePoint.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM);
            //double trueNorthAngleDegrees = UnitUtils.ConvertFromInternalUnits(trueNorthAngle.AsDouble(), UnitTypeId.Degrees);

            Transform rotation = Transform.CreateRotation(XYZ.BasisZ, trueNorthAngle.AsDouble());
            XYZ trueNorthVector = rotation.OfVector(XYZ.BasisY);

            //Получаем все необходимые стены
            List<Wall> walls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();

            //Получаем все необходимые зависимые элементы (двери, окна, и т.д.)
            List<FamilyInstance> hostedElems = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            using (Transaction t = new Transaction(doc, "Заполнение параметра \"INP_Ориентация по сторонам света\""))
            {
                t.Start();

                foreach (Wall wall in walls)
                {
                    if (wall.WallType.Function != WallFunction.Exterior || wall.WallType.Name.Contains("Отделка"))
                        continue;

                    Parameter wallOrientParam = wall.LookupParameter("INP_Ориентация по сторонам света");

                    XYZ wallOrientation = WallOrientation(wall);
                    double angleOrientation;

                    if (wallOrientation.X == -1 || wallOrientation.Y == 1)
                    {
                        angleOrientation = 360 - UnitUtils.ConvertFromInternalUnits(trueNorthVector.AngleTo(wallOrientation), UnitTypeId.Degrees);
                    }
                    else
                    {
                        angleOrientation = UnitUtils.ConvertFromInternalUnits(trueNorthVector.AngleTo(wallOrientation), UnitTypeId.Degrees);
                    }
                    SetOrientationText(wallOrientParam, angleOrientation);

                    foreach(FamilyInstance elem in hostedElems)
                    {
                        Parameter elemOrientParam = elem.LookupParameter("INP_Ориентация по сторонам света");
                        Element host = elem.Host;
                        if (elemOrientParam == null || host == null)
                            continue;
                        if (host.Id == wall.Id)
                            elemOrientParam.Set(wallOrientParam.AsString());
                    }

                }
                t.Commit();
            }
            TaskDialog.Show("Результат", "Параметры ориентации успешно заполнены!");
            return Result.Succeeded;
        }
        public XYZ WallOrientation(Wall wall)
        {
            Location loc = wall.Location;

            if (loc is LocationCurve locCurve)
            {
                Curve lcurve = locCurve.Curve;
                bool flipped = wall.Flipped;

                // Если стена прямая
                if (lcurve is Line)
                {
                    return flipped
                        ? wall.Orientation.Negate()
                        : wall.Orientation;
                }
                else
                {
                    XYZ direction = (lcurve.GetEndPoint(1) - lcurve.GetEndPoint(0)).Normalize();

                    XYZ normal = XYZ.BasisZ.CrossProduct(direction);

                    return flipped
                        ? normal.Negate()
                        : normal;
                }
            }

            return null;
        }

        public void SetOrientationText(Parameter param, double angle)
        {
            if ((angle >= 0 && angle <= 22.5) || (angle >= 337.5 && angle <= 360))
                param.Set("С");
            else if (angle > 22.5 && angle < 67.5)
                param.Set("СВ");
            else if (angle >= 67.5 && angle <= 112.5)
                param.Set("В");
            else if (angle > 112.5 && angle < 157.5)
                param.Set("ЮВ");
            else if (angle >= 157.5 && angle <= 202.5)
                param.Set("Ю");
            else if (angle > 202.5 && angle < 247.5)
                param.Set("ЮЗ");
            else if (angle >= 247.5 && angle <= 292.5)
                param.Set("З");
            else if (angle > 292.5 && angle < 337.5)
                param.Set("СЗ");
        }
    }
}
