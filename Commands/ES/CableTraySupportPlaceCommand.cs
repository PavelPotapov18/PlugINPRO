using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.ES;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlugINPRO.Commands.ES
{
    [Transaction(TransactionMode.Manual)]
    public class CableTraySupportPlaceCommand : IExternalCommand
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
                CableTraySupportSelectWindow selectWindow = new CableTraySupportSelectWindow(doc);
                bool? dialogResult = selectWindow.ShowDialog();

                if (dialogResult != true || selectWindow.SelectedFamily == null)
                    return Result.Cancelled;

                FamilySymbol selectedFamily = selectWindow.SelectedFamily;

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

                LocationCurve locationCurve = tray.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию лотка");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double curveLengthFeet = curve.Length;
                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(curveLengthFeet, UnitTypeId.Millimeters);

                Reference clickRef = uidoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new CableTraySelectionFilter(),
                    "Кликните на лоток в месте установки опоры");

                XYZ clickPoint = clickRef.GlobalPoint;
                if (clickPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить точку клика");
                    return Result.Failed;
                }

                XYZ projectedPoint = GetClosestPointOnCurve(curve, clickPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось спроецировать точку на ось лотка");
                    return Result.Failed;
                }

                double distanceFromStartFeet = GetDistanceAlongCurve(curve, projectedPoint);
                double distanceFromStartMm = UnitUtils.ConvertFromInternalUnits(distanceFromStartFeet, UnitTypeId.Millimeters);
                double distanceFromEndMm = totalLengthMm - distanceFromStartMm;

                CableTraySupportPlaceSettingsWindow settingsWindow = new CableTraySupportPlaceSettingsWindow(
                    distanceFromStartMm,
                    distanceFromEndMm,
                    totalLengthMm);

                bool? settingsResult = settingsWindow.ShowDialog();
                if (settingsResult != true) return Result.Cancelled;

                double userOffsetMm = settingsWindow.UserOffset;
                bool isFromStart = settingsWindow.IsFromStart;

                double offsetFromStartMm = isFromStart ? userOffsetMm : totalLengthMm - userOffsetMm;
                if (offsetFromStartMm < 0 || offsetFromStartMm > totalLengthMm)
                {
                    TaskDialog.Show("Внимание", $"Отступ выходит за пределы лотка!\nМаксимальный отступ: {totalLengthMm:F0} мм");
                    return Result.Cancelled;
                }

                double offsetFromStartFeet = UnitUtils.ConvertToInternalUnits(offsetFromStartMm, UnitTypeId.Millimeters);

                XYZ placePoint = GetPointAtDistance(curve, offsetFromStartFeet);
                if (placePoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось вычислить точку установки");
                    return Result.Failed;
                }

                // ✅ ВСЕ ПАРАМЕТРЫ СИСТЕМЫ - ПРОСТО ПУСТЫЕ СТРОКИ
                string systemClassification = "";
                string systemType = "";
                string systemName = "";
                string systemAbbreviation = "";

                CableTrayDimensions dims = GetCableTrayDimensions(tray);

                XYZ trayDirection = GetTrayDirection(curve);
                bool isVertical = Math.Abs(trayDirection.Z) > 0.9;

                XYZ rotationOrientation;
                if (isVertical)
                    rotationOrientation = GetTrayWallOrientation(tray);
                else
                {
                    rotationOrientation = new XYZ(trayDirection.X, trayDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

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
                        SetCableTrayParameters(support, dims,
                            systemClassification,
                            systemType,
                            systemName,
                            systemAbbreviation);

                        RotateSupport(support, placePoint, rotationOrientation);

                        trans.Commit();

                        string directionText = isFromStart ? "от начала" : "от конца";
                        TaskDialog.Show("Результат",
                            $"✅ Опора установлена!\n" +
                            $"Отступ: {userOffsetMm:F0} мм {directionText}\n" +
                            $"Позиция: X={placePoint.X:F2}, Y={placePoint.Y:F2}, Z={placePoint.Z:F2}\n" +
                            $"Размер лотка: {dims.Width:F0}×{dims.Height:F0} мм");
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

        private class CableTraySelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is CableTray;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private class CableTrayDimensions
        {
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private CableTrayDimensions GetCableTrayDimensions(CableTray tray)
        {
            CableTrayDimensions dims = new CableTrayDimensions();
            try
            {
                Parameter widthParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                Parameter heightParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

                if (widthParam != null && widthParam.HasValue)
                    dims.Width = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Millimeters);
                else
                    dims.Width = 100.0;

                if (heightParam != null && heightParam.HasValue)
                    dims.Height = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Millimeters);
                else
                    dims.Height = 50.0;
            }
            catch
            {
                dims.Width = 100.0;
                dims.Height = 50.0;
            }
            return dims;
        }

        private XYZ GetTrayDirection(Curve curve)
        {
            try
            {
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ direction = end - start;
                if (direction.GetLength() < 0.001) return XYZ.BasisZ;
                return direction.Normalize();
            }
            catch { return XYZ.BasisZ; }
        }

        private XYZ GetTrayWallOrientation(CableTray tray)
        {
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geometry = tray.get_Geometry(options);
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
                                if (normal != null && normal.GetLength() > 0.001 && Math.Abs(normal.Z) < 0.3)
                                    wallNormals.Add(normal);
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
                        return wallDirection.Normalize();
                }
                return XYZ.BasisX;
            }
            catch { return XYZ.BasisX; }
        }

        private XYZ GetFaceNormalAtCenter(Face face)
        {
            try
            {
                BoundingBoxUV bbox = face.GetBoundingBox();
                UV centerUV = new UV((bbox.Min.U + bbox.Max.U) * 0.5, (bbox.Min.V + bbox.Max.V) * 0.5);
                return face.ComputeNormal(centerUV);
            }
            catch { return null; }
        }

        private XYZ GetClosestPointOnCurve(Curve curve, XYZ point)
        {
            try
            {
                IntersectionResult result = curve.Project(point);
                return result?.XYZPoint;
            }
            catch { return null; }
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
                    return result?.Parameter * curve.Length ?? 0;
                }
            }
            catch { return 0; }
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
            catch { return null; }
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
            catch { }
        }

        private void SetCableTrayParameters(FamilyInstance support, CableTrayDimensions dims,
            string systemClassification, string systemType, string systemName, string systemAbbreviation)
        {
            try
            {
                SetDoubleParameterValue(support, "CABLETRAY_H", dims.Height);
                SetDoubleParameterValue(support, "CABLETRAY_B", dims.Width);
                SetDoubleParameterValue(support, "H", dims.Height);
                SetDoubleParameterValue(support, "B", dims.Width);
                SetDoubleParameterValue(support, "Height", dims.Height);
                SetDoubleParameterValue(support, "Width", dims.Width);
                SetDoubleParameterValue(support, "Высота", dims.Height);
                SetDoubleParameterValue(support, "Ширина", dims.Width);

                SetStringParameterValue(support, "ADSK_Система_Классификация", systemClassification);
                SetStringParameterValue(support, "ADSK_Система_Тип", systemType);
                SetStringParameterValue(support, "ADSK_Система_Имя", systemName);
                SetStringParameterValue(support, "ADSK_Система_Сокращение", systemAbbreviation);
            }
            catch { }
        }

        private void SetDoubleParameterValue(FamilyInstance support, string paramName, double valueMm)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.Double)
                    param.Set(UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters));
            }
            catch { }
        }

        private void SetStringParameterValue(FamilyInstance support, string paramName, string value)
        {
            try
            {
                Parameter param = support.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                    param.Set(value);
            }
            catch { }
        }
    }
}