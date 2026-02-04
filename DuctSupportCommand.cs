// DuctSupportsCommand.cs - НОВЫЙ ФАЙЛ
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class DuctSupportsCommand : IExternalCommand
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
                // 1. Выбор воздуховода
                Reference ductRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new DuctSelectionFilter(),
                    "Выберите воздуховод");

                Duct duct = doc.GetElement(ductRef) as Duct;

                if (duct == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является воздуховодом");
                    return Result.Failed;
                }

                // 2. Выбор семейства опоры
                FamilySymbol supportSymbol = SelectSupportFamily(doc);
                if (supportSymbol == null) return Result.Cancelled;

                // 3. Ввод шага размещения через диалог Revit
                double spacing = GetSpacingFromUser();
                if (spacing <= 0) return Result.Cancelled;

                // 4. Размещение опор
                using (Transaction trans = new Transaction(doc, "Размещение опор воздуховода"))
                {
                    trans.Start();

                    // Активация семейства если не активно
                    if (!supportSymbol.IsActive)
                        supportSymbol.Activate();

                    // Получение кривой воздуховода
                    LocationCurve locationCurve = duct.Location as LocationCurve;
                    if (locationCurve == null)
                    {
                        TaskDialog.Show("Ошибка", "Не удалось получить геометрию воздуховода");
                        return Result.Failed;
                    }

                    Curve curve = locationCurve.Curve;

                    // Расчет точек размещения
                    List<XYZ> placementPoints = CalculatePlacementPoints(curve, spacing);

                    // Определение высоты воздуховода для правильного размещения опор
                    double ductHeight = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;

                    // Размещение опор
                    foreach (XYZ point in placementPoints)
                    {
                        try
                        {
                            // Получаем касательный вектор в точке
                            double parameter = curve.Project(point).Parameter;
                            XYZ tangent = curve.ComputeDerivatives(parameter, true).BasisX;

                            // Создаем опору
                            FamilyInstance support = doc.Create.NewFamilyInstance(
                                point,
                                supportSymbol,
                                StructuralType.NonStructural);

                            // Поворачиваем опору по направлению воздуховода
                            if (!tangent.IsZeroLength())
                            {
                                double angle = Math.Atan2(tangent.Y, tangent.X);
                                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, support.Id, axis, angle);
                            }

                            // Опционально: устанавливаем смещение по высоте
                            // Это зависит от типа опоры - нужно тестировать
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Внимание", $"Ошибка при размещении опоры: {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                TaskDialog.Show("Успех", $"Установлено опор: {placementPoints.Count}");
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

        // Фильтр для выбора воздуховодов
        private class DuctSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Duct;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        // Выбор семейства опоры
        private FamilySymbol SelectSupportFamily(Document doc)
        {
            try
            {
                // Фильтруем семейства по категории (можно настроить под свои нужды)
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .WhereElementIsElementType();

                // Преобразуем в список для выбора
                List<FamilySymbol> supportSymbols = collector.Cast<FamilySymbol>()
                    .Where(fs => fs.Family.Name.ToLower().Contains("опор") ||
                                 fs.Family.Name.ToLower().Contains("support") ||
                                 fs.Family.Name.ToLower().Contains("подвес") ||
                                 fs.Name.ToLower().Contains("опор"))
                    .ToList();

                if (supportSymbols.Count == 0)
                {
                    TaskDialog.Show("Внимание", "В проекте не найдены семейства опор.\nЗагрузите семейство опор воздуховода.");
                    return null;
                }

                // Создаем форму для выбора или используем первый найденный
                if (supportSymbols.Count == 1)
                {
                    return supportSymbols[0];
                }
                else
                {
                    // Здесь можно реализовать диалог выбора из нескольких семейств
                    // Для простоты используем первое подходящее
                    return supportSymbols.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Ошибка при выборе семейства: {ex.Message}");
                return null;
            }
        }

        // Ввод шага размещения
        private double GetSpacingFromUser()
        {
            // Простой диалог для ввода шага
            TaskDialog dialog = new TaskDialog("Шаг опор");
            dialog.MainInstruction = "Введите шаг расстановки опор (мм)";
            dialog.MainContent = "Рекомендуемый шаг: 2000-3000 мм";

            // Добавляем текстовое поле
            TaskDialogCommonButtons buttons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            dialog.CommonButtons = buttons;

            // Создаем поле ввода
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Шаг 2000 мм");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Шаг 2500 мм");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Шаг 3000 мм");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Другой шаг...");

            TaskDialogResult result = dialog.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    return 2000;
                case TaskDialogResult.CommandLink2:
                    return 2500;
                case TaskDialogResult.CommandLink3:
                    return 3000;
                case TaskDialogResult.CommandLink4:
                    // Запрос произвольного значения
                    TaskDialog dialogCustom = new TaskDialog("Ввод шага");
                    dialogCustom.MainInstruction = "Введите шаг в миллиметрах:";

                    // Можно использовать более сложный диалог с TextBox
                    // Для простоты используем стандартный ввод
                    string input = Microsoft.VisualBasic.Interaction.InputBox(
                        "Введите шаг опор (мм):",
                        "Шаг расстановки",
                        "2500",
                        0, 0);

                    if (double.TryParse(input, out double customSpacing))
                        return customSpacing;
                    else
                        return 0;
                default:
                    return 0;
            }
        }

        // Расчет точек размещения
        private List<XYZ> CalculatePlacementPoints(Curve curve, double spacing)
        {
            List<XYZ> points = new List<XYZ>();

            try
            {
                double curveLength = curve.Length;

                // Начинаем с отступа от начала (половина шага или можно сделать настраиваемым)
                double offset = spacing / 2;
                double currentDistance = offset;

                while (currentDistance < curveLength - offset)
                {
                    // Для линейных кривых
                    if (curve is Line line)
                    {
                        XYZ start = line.GetEndPoint(0);
                        XYZ end = line.GetEndPoint(1);
                        XYZ direction = (end - start).Normalize();

                        XYZ point = start + direction * currentDistance;
                        points.Add(point);
                    }
                    // Для дуговых кривых
                    else if (curve is Arc arc)
                    {
                        double parameter = currentDistance / curveLength;
                        XYZ point = arc.Evaluate(parameter, true);
                        points.Add(point);
                    }
                    // Для других типов кривых
                    else
                    {
                        double parameter = currentDistance / curveLength;
                        XYZ point = curve.Evaluate(parameter, true);
                        points.Add(point);
                    }

                    currentDistance += spacing;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Ошибка расчета точек: {ex.Message}");
            }

            return points;
        }
    }
}