using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.UI.Windows.ES;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlugINPRO.Commands.ES
{
    [Transaction(TransactionMode.Manual)]
    public class CableTraySupportsByLengthCommand : IExternalCommand
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
                CableTrayFittingByLengthSettingsWindow settingsWindow = new CableTrayFittingByLengthSettingsWindow(doc);
                bool? dialogResult = settingsWindow.ShowDialog();

                if (dialogResult != true || settingsWindow.SelectedFamily == null)
                    return Result.Cancelled;

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

                CableTrayDimensions trayDims = GetCableTrayDimensions(tray);

                // ✅ ВСЕ ПАРАМЕТРЫ СИСТЕМЫ - ПРОСТО ПУСТЫЕ СТРОКИ
                string systemClassification = "";
                string systemType = "";
                string systemName = "";
                string systemAbbreviation = "";

                LocationCurve locationCurve = tray.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию лотка");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                double curveLength = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);

                double spacingFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Spacing, UnitTypeId.Millimeters);
                double offsetFeet = UnitUtils.ConvertToInternalUnits(settingsWindow.Offset, UnitTypeId.Millimeters);

                List<XYZ> placementPoints = CalculatePlacementPoints(curve, spacingFeet, offsetFeet);

                if (placementPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание", $"Не удалось рассчитать точки размещения опор.\nДлина: {curveLength:F0} мм");
                    return Result.Cancelled;
                }

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

                using (Transaction trans = new Transaction(doc, "Размещение опор кабельного лотка"))
                {
                    trans.Start();

                    if (!settingsWindow.SelectedFamily.IsActive)
                        settingsWindow.SelectedFamily.Activate();

                    int placedCount = 0;

                    foreach (XYZ point in placementPoints)
                    {
                        try
                        {
                            FamilyInstance support = doc.Create.NewFamilyInstance(
                                point,
                                settingsWindow.SelectedFamily,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (support != null)
                            {
                                SetCableTrayParameters(support, trayDims,
                                    systemClassification,
                                    systemType,
                                    systemName,
                                    systemAbbreviation);

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

                    if (!settingsWindow.UsePreviousFamily)
                        App.SelectedCableTraySupportFamily = settingsWindow.SelectedFamily;

                    TaskDialog.Show("Результат",
                        $"Установлено опор: {placedCount} из {placementPoints.Count}\n" +
                        $"Шаг: {settingsWindow.Spacing} мм\n" +
                        $"Отступ: {settingsWindow.Offset} мм\n" +
                        $"Тип: {(isVertical ? "Вертикальный" : "Горизонтальный")}\n" +
                        $"Размер: {trayDims.Width:F0}×{trayDims.Height:F0} мм");
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
            public bool AllowReference(Reference reference, XYZ position) => false;
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

        private List<XYZ> CalculatePlacementPoints(Curve curve, double spacing, double offset)
        {
            List<XYZ> points = new List<XYZ>();
            try
            {
                double curveLength = curve.Length;
                if (offset >= curveLength) return points;

                double currentDistance = offset;
                while (currentDistance < curveLength)
                {
                    XYZ point = GetPointAtDistance(curve, currentDistance);
                    if (point != null) points.Add(point);
                    currentDistance += spacing;

                    if (currentDistance >= curveLength && points.Count > 0)
                    {
                        double distanceToEnd = curveLength - (currentDistance - spacing);
                        double minDistanceFeet = 1.0 / 304.8;
                        if (distanceToEnd > minDistanceFeet)
                        {
                            XYZ endPoint = GetPointAtDistance(curve, curveLength);
                            if (endPoint != null) points.Add(endPoint);
                        }
                        break;
                    }
                }

                if (points.Count == 0 && offset < curveLength)
                {
                    double middleDistance = offset + (curveLength - offset) / 2;
                    XYZ middlePoint = GetPointAtDistance(curve, middleDistance);
                    if (middlePoint != null) points.Add(middlePoint);
                }
            }
            catch { }
            return points;
        }

        private XYZ GetPointAtDistance(Curve curve, double distance)
        {
            try
            {
                double tolerance = 0.0001;
                if (distance < 0) distance = 0;
                if (distance > curve.Length) distance = curve.Length;

                if (Math.Abs(distance - curve.Length) < tolerance)
                    return curve.GetEndPoint(1);

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
            catch { return curve.GetEndPoint(0); }
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