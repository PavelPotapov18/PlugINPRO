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
    public class CableTraySupportCopyCommand : IExternalCommand
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
                Reference supportRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new SupportSelectionFilter(),
                    "Выберите опору для копирования");

                FamilyInstance support = doc.GetElement(supportRef) as FamilyInstance;
                if (support == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является опорой");
                    return Result.Failed;
                }

                CableTray tray = FindHostCableTray(support, doc);
                if (tray == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось найти лоток, на котором установлена опора");
                    return Result.Failed;
                }

                LocationCurve locationCurve = tray.Location as LocationCurve;
                if (locationCurve == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить геометрию лотка");
                    return Result.Failed;
                }

                Curve curve = locationCurve.Curve;
                XYZ trayDirection = GetTrayDirection(curve);
                bool isVertical = Math.Abs(trayDirection.Z) > 0.9;

                CableTraySupportCopySettingsWindow settingsWindow = new CableTraySupportCopySettingsWindow(
                    support, tray, isVertical);

                bool? dialogResult = settingsWindow.ShowDialog();
                if (dialogResult != true) return Result.Cancelled;

                double offsetMm = settingsWindow.Offset;
                int copyCount = settingsWindow.Count;
                double spacingMm = settingsWindow.Spacing;
                bool copyMultiple = settingsWindow.CopyMultiple;

                double offsetFeet = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
                double spacingFeet = copyMultiple ? UnitUtils.ConvertToInternalUnits(spacingMm, UnitTypeId.Millimeters) : 0;

                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить положение опоры");
                    return Result.Failed;
                }

                XYZ projectedPoint = GetClosestPointOnCurve(curve, supportPoint);
                if (projectedPoint == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось спроецировать точку на трассу лотка");
                    return Result.Failed;
                }

                double parameter = GetParameterOnCurve(curve, projectedPoint);
                if (parameter < 0)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить параметр точки на кривой");
                    return Result.Failed;
                }

                double curveLength = curve.Length;
                double currentDistance = parameter * curveLength;

                List<XYZ> newPoints = new List<XYZ>();
                double currentOffset = offsetFeet;
                int maxCopies = copyMultiple ? copyCount : 1;

                for (int i = 0; i < maxCopies; i++)
                {
                    double newDistance = currentDistance + currentOffset;
                    if (newDistance < 0 || newDistance > curveLength)
                    {
                        if (i == 0)
                        {
                            TaskDialog.Show("Внимание", $"Смещение {offsetMm:F0} мм выходит за пределы лотка!");
                            return Result.Cancelled;
                        }
                        break;
                    }

                    XYZ newPoint = GetPointAtDistance(curve, newDistance);
                    if (newPoint != null) newPoints.Add(newPoint);
                    currentOffset += spacingFeet;
                }

                if (newPoints.Count == 0)
                {
                    TaskDialog.Show("Внимание", "Не удалось создать ни одной копии опоры!");
                    return Result.Cancelled;
                }

                // ✅ ВСЕ ПАРАМЕТРЫ СИСТЕМЫ - ПРОСТО ПУСТЫЕ СТРОКИ
                string systemClassification = "";
                string systemType = "";
                string systemName = "";
                string systemAbbreviation = "";

                CableTrayDimensions dims = GetCableTrayDimensions(tray);

                XYZ rotationOrientation;
                if (isVertical)
                    rotationOrientation = GetTrayWallOrientation(tray);
                else
                {
                    rotationOrientation = new XYZ(trayDirection.X, trayDirection.Y, 0);
                    rotationOrientation = rotationOrientation.Normalize();
                }

                using (Transaction trans = new Transaction(doc, "Копирование опор"))
                {
                    trans.Start();

                    FamilySymbol familySymbol = support.Symbol;
                    if (!familySymbol.IsActive) familySymbol.Activate();

                    int placedCount = 0;

                    foreach (XYZ point in newPoints)
                    {
                        try
                        {
                            FamilyInstance newSupport = doc.Create.NewFamilyInstance(
                                point,
                                familySymbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (newSupport != null)
                            {
                                SetCableTrayParameters(newSupport, dims,
                                    systemClassification,
                                    systemType,
                                    systemName,
                                    systemAbbreviation);

                                RotateSupport(newSupport, point, rotationOrientation);
                                placedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Ошибка создания опоры: {ex.Message}");
                        }
                    }

                    trans.Commit();

                    string directionText = offsetMm > 0 ? "вперед" : "назад";
                    string multipleInfo = copyMultiple ? $"\nКоличество опор: {placedCount}\nШаг: {spacingMm:F0} мм" : "";

                    TaskDialog.Show("Результат",
                        $"Создано опор: {placedCount} из {newPoints.Count}\n" +
                        $"Начальное смещение: {Math.Abs(offsetMm):F0} мм {directionText}{multipleInfo}");
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

        private class SupportSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem is FamilyInstance instance)
                {
                    Category cat = instance.Category;
                    if (cat != null && cat.Id.IntegerValue == (int)BuiltInCategory.OST_CableTrayFitting)
                        return true;
                }
                return false;
            }
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

        private CableTray FindHostCableTray(FamilyInstance support, Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ICollection<Element> trays = collector
                    .OfClass(typeof(CableTray))
                    .WhereElementIsNotElementType()
                    .ToElements();

                XYZ supportPoint = GetSupportLocation(support);
                if (supportPoint == null) return null;

                double minDistance = double.MaxValue;
                CableTray closestTray = null;

                foreach (CableTray tray in trays)
                {
                    LocationCurve locationCurve = tray.Location as LocationCurve;
                    if (locationCurve == null) continue;

                    IntersectionResult result = locationCurve.Curve.Project(supportPoint);
                    if (result != null && result.Distance < minDistance)
                    {
                        minDistance = result.Distance;
                        closestTray = tray;
                    }
                }

                double maxDistanceFeet = 1.0 / 304.8;
                if (closestTray != null && minDistance < maxDistanceFeet)
                    return closestTray;

                return null;
            }
            catch { return null; }
        }

        private XYZ GetSupportLocation(FamilyInstance support)
        {
            try
            {
                LocationPoint locPoint = support.Location as LocationPoint;
                if (locPoint != null) return locPoint.Point;

                Options options = new Options();
                options.ComputeReferences = true;
                GeometryElement geometry = support.get_Geometry(options);

                if (geometry != null)
                {
                    BoundingBoxXYZ bbox = support.get_BoundingBox(null);
                    if (bbox != null) return (bbox.Min + bbox.Max) / 2;
                }
                return null;
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

        private double GetParameterOnCurve(Curve curve, XYZ point)
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
                    return Math.Max(0, Math.Min(1, projection / length));
                }
                else
                {
                    IntersectionResult result = curve.Project(point);
                    return result?.Parameter ?? -1;
                }
            }
            catch { return -1; }
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