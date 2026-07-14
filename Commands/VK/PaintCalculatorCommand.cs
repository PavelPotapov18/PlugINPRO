using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using PlugINPRO.Commands.VK.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaintCalculatorCommand : IExternalCommand
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
                // 1. Показываем диалог выбора режима
                SelectionModeDialog modeDialog = new SelectionModeDialog();
                if (modeDialog.ShowDialog() != true)
                    return Result.Cancelled;

                PipingSystem selectedSystem = null;
                List<Element> selectedElements = null;
                double totalArea = 0;

                if (modeDialog.SelectionMode == SelectionMode.WholeSystem)
                {
                    // 2. Режим "Вся система" - выбираем систему
                    selectedSystem = SelectPipingSystem(uidoc, doc);
                    if (selectedSystem == null)
                        return Result.Cancelled;

                    totalArea = CalculateTotalArea(selectedSystem, doc);
                }
                else
                {
                    // 3. Режим "По элементам" - выбираем элементы
                    selectedElements = SelectSystemElements(uidoc, doc);
                    if (selectedElements == null || selectedElements.Count == 0)
                        return Result.Cancelled;

                    // Считаем ТОЛЬКО выбранные элементы, без поиска системы
                    totalArea = CalculateAreaForElements(selectedElements, doc);
                }

                // 4. Открываем диалог для ввода данных о краске
                PaintInputDialog dialog = new PaintInputDialog(totalArea);
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                string paintName = dialog.PaintName;
                double consumptionPerSquareMeter = dialog.ConsumptionPerSquareMeter;

                // 5. Рассчитываем общий расход краски
                double totalConsumption = totalArea * consumptionPerSquareMeter;

                // 6. Создаем элемент с информацией о краске
                using (Transaction trans = new Transaction(doc, "Создать информацию о краске"))
                {
                    trans.Start();

                    FamilyInstance paintInfoElement = CreatePaintInformationElement(
                        doc,
                        paintName,
                        consumptionPerSquareMeter,
                        totalConsumption,
                        selectedSystem ?? (object)selectedElements?.FirstOrDefault());

                    trans.Commit();
                }

                // 7. Показываем результат
                string messageText = $"Информация о краске добавлена!\n\n" +
                    $"Наименование: {paintName}\n" +
                    $"Расход на м²: {consumptionPerSquareMeter:F2} кг/м²\n" +
                    $"Общая площадь: {totalArea:F2} м²\n" +
                    $"Общий расход краски: {totalConsumption:F2} кг";

                if (selectedSystem != null)
                {
                    messageText += $"\n\nСистема: {GetSystemName(selectedSystem)}";
                }
                else if (selectedElements != null)
                {
                    messageText += $"\n\nВыбрано элементов: {selectedElements.Count}";
                }

                MessageBox.Show(messageText, "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        #region Выбор режима

        public enum SelectionMode
        {
            ByElements,
            WholeSystem
        }

        public class SelectionModeDialog : Window
        {
            public SelectionMode SelectionMode { get; private set; }

            public SelectionModeDialog()
            {
                Title = "Выбор режима расчета";
                Width = 400;
                Height = 250;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ResizeMode = ResizeMode.NoResize;

                var stackPanel = new StackPanel { Margin = new Thickness(20) };

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Выберите способ выбора элементов для расчета",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 20)
                });

                var elementsButton = new Button
                {
                    Content = "Выбрать элементы вручную",
                    Height = 40,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10, 5, 10, 5)
                };
                elementsButton.Click += (s, e) =>
                {
                    SelectionMode = SelectionMode.ByElements;
                    DialogResult = true;
                    Close();
                };
                stackPanel.Children.Add(elementsButton);

                var systemButton = new Button
                {
                    Content = "Выбрать всю систему",
                    Height = 40,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10, 5, 10, 5)
                };
                systemButton.Click += (s, e) =>
                {
                    SelectionMode = SelectionMode.WholeSystem;
                    DialogResult = true;
                    Close();
                };
                stackPanel.Children.Add(systemButton);

                var cancelButton = new Button
                {
                    Content = "Отмена",
                    Height = 30,
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                cancelButton.Click += (s, e) =>
                {
                    DialogResult = false;
                    Close();
                };
                stackPanel.Children.Add(cancelButton);

                Content = stackPanel;
            }
        }

        #endregion

        #region Выбор системы или элементов

        private PipingSystem SelectPipingSystem(UIDocument uidoc, Document doc)
        {
            Reference refElement = uidoc.Selection.PickObject(
                Autodesk.Revit.UI.Selection.ObjectType.Element,
                new PipingSystemSelectionFilter(),
                "Выберите любой элемент из системы (трубу или фитинг)");

            if (refElement == null)
                return null;

            Element element = doc.GetElement(refElement.ElementId);

            // Исправленный поиск системы
            PipingSystem system = FindSystemInElement(element);

            if (system == null)
            {
                MessageBox.Show(
                    "Не удалось найти систему трубопроводов для выбранного элемента.\n\n" +
                    "Попробуйте:\n" +
                    "1. Выбрать другой элемент из той же системы\n" +
                    "2. Использовать режим 'Выбрать элементы вручную'",
                    "Система не найдена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            return system;
        }

        /// <summary>
        /// Правильный способ получения системы из элемента
        /// </summary>
        private PipingSystem FindSystemInElement(Element element)
        {
            // Для труб - прямое свойство MEPSystem
            if (element is Pipe pipe)
            {
                return pipe.MEPSystem as PipingSystem;
            }

            // Для фитингов - через коннекторы
            if (element is FamilyInstance fi)
            {
                MEPModel mepModel = fi.MEPModel;
                if (mepModel?.ConnectorManager != null)
                {
                    foreach (Connector conn in mepModel.ConnectorManager.Connectors)
                    {
                        if (conn.Domain == Domain.DomainPiping && conn.MEPSystem != null)
                        {
                            return conn.MEPSystem as PipingSystem;
                        }
                    }
                }
            }

            return null;
        }

        private List<Element> SelectSystemElements(UIDocument uidoc, Document doc)
        {
            IList<Reference> refs = null;

            try
            {
                refs = uidoc.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new PipingSystemSelectionFilter(),
                    "Выберите трубы и/или фитинги (можно несколько)");
            }
            catch
            {
                return null;
            }

            if (refs == null || refs.Count == 0)
                return null;

            List<Element> elements = new List<Element>();
            foreach (Reference r in refs)
            {
                Element elem = doc.GetElement(r.ElementId);
                if (elem != null)
                    elements.Add(elem);
            }

            return elements;
        }

        private string GetSystemName(PipingSystem system)
        {
            // Правильный способ получить имя системы
            if (!string.IsNullOrEmpty(system.Name))
                return system.Name;

            Parameter nameParam = system.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (nameParam != null && nameParam.HasValue)
                return nameParam.AsString();

            return system.Id.ToString();
        }

        #endregion

        #region Расчет площади

        private double CalculateTotalArea(PipingSystem system, Document doc)
        {
            double totalArea = 0;
            ElementId systemId = system.Id;

            var pipes = new FilteredElementCollector(doc)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .Where(p => IsElementInSystem(p, systemId))
                .ToList();

            var fittings = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(f => f.Category != null &&
                           f.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting &&
                           IsElementInSystem(f, systemId))
                .ToList();

            foreach (Pipe pipe in pipes)
            {
                totalArea += CalculatePipeArea(pipe);
            }

            foreach (FamilyInstance fitting in fittings)
            {
                totalArea += CalculateFittingArea(fitting);
            }

            return totalArea;
        }

        private double CalculateAreaForElements(List<Element> elements, Document doc)
        {
            double totalArea = 0;

            foreach (Element element in elements)
            {
                if (element is Pipe pipe)
                {
                    totalArea += CalculatePipeArea(pipe);
                }
                else if (element is FamilyInstance fitting)
                {
                    if (fitting.Category != null &&
                        fitting.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting)
                    {
                        totalArea += CalculateFittingArea(fitting);
                    }
                }
            }

            return totalArea;
        }

        private bool IsElementInSystem(Element element, ElementId systemId)
        {
            PipingSystem system = FindSystemInElement(element);
            return system != null && system.Id == systemId;
        }

        #endregion

        #region Расчет площади труб и фитингов

        private double CalculatePipeArea(Pipe pipe)
        {
            double area = 0;

            try
            {
                Parameter areaParam = pipe.get_Parameter(BuiltInParameter.SURFACE_AREA);
                if (areaParam != null && areaParam.HasValue && areaParam.AsDouble() > 0)
                {
                    area = areaParam.AsDouble();
                }
                else
                {
                    Parameter lengthParam = pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lengthParam == null || !lengthParam.HasValue)
                    {
                        lengthParam = pipe.LookupParameter("Длина");
                    }

                    if (lengthParam != null && lengthParam.HasValue)
                    {
                        double length = lengthParam.AsDouble();
                        double diameter = 0;

                        string[] diameterParamNames = new string[]
                        {
                            "Диаметр", "Размер", "DN", "Diameter", "Size",
                            "Номинальный диаметр", "Наружный диаметр"
                        };

                        Parameter diameterParam = null;
                        foreach (string name in diameterParamNames)
                        {
                            Parameter param = pipe.LookupParameter(name);
                            if (param != null && param.HasValue)
                            {
                                diameterParam = param;
                                break;
                            }
                        }

                        if (diameterParam != null)
                        {
                            if (diameterParam.StorageType == StorageType.Double)
                            {
                                diameter = diameterParam.AsDouble();
                            }
                            else if (diameterParam.StorageType == StorageType.String)
                            {
                                string sizeStr = diameterParam.AsString();
                                if (!string.IsNullOrEmpty(sizeStr))
                                {
                                    string digits = System.Text.RegularExpressions.Regex.Match(sizeStr, @"\d+").Value;
                                    if (!string.IsNullOrEmpty(digits))
                                    {
                                        diameter = double.Parse(digits) / 1000.0;
                                    }
                                }
                            }
                        }

                        if (diameter > 0 && length > 0)
                        {
                            area = Math.PI * diameter * length;
                        }
                    }
                }
            }
            catch
            {
                area = 0;
            }

            return area * 0.09290304;
        }

        private double CalculateFittingArea(FamilyInstance fitting)
        {
            double area = 0;

            try
            {
                MEPModel mepModel = fitting.MEPModel;
                var connectors = mepModel?.ConnectorManager?.Connectors
                    .Cast<Connector>()
                    .Where(c => c.Domain == Domain.DomainPiping)
                    .ToList();

                if (connectors != null && connectors.Count > 0)
                {
                    XYZ center = new XYZ(
                        connectors.Average(c => c.Origin.X),
                        connectors.Average(c => c.Origin.Y),
                        connectors.Average(c => c.Origin.Z));

                    foreach (Connector conn in connectors)
                    {
                        if (conn.Shape != ConnectorProfileType.Round)
                            continue;

                        double diameter = conn.Radius * 2;
                        double length = conn.Origin.DistanceTo(center);

                        // Только если длина меньше 10% диаметра (для заглушек)
                        if (length < diameter * 0.1)
                            length = diameter;

                        area += Math.PI * diameter * length;
                    }
                }
            }
            catch
            {
                area = 0;
            }

            // Fallback через bounding box (если нет коннекторов)
            if (area == 0)
            {
                try
                {
                    BoundingBoxXYZ bbox = fitting.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        double w = bbox.Max.X - bbox.Min.X;
                        double h = bbox.Max.Y - bbox.Min.Y;
                        double d = bbox.Max.Z - bbox.Min.Z;
                        area = 2 * (w * h + w * d + h * d) * 0.7;
                    }
                }
                catch
                {
                    area = 0;
                }
            }

            return area * 0.09290304;
        }

        #endregion

        #region Создание элемента информации о краске

        private FamilyInstance CreatePaintInformationElement(
            Document doc,
            string paintName,
            double consumptionPerM2,
            double totalConsumption,
            object systemOrElement)
        {
            FamilySymbol paintFamilySymbol = FindPaintFamilySymbol(doc);

            if (paintFamilySymbol == null)
            {
                throw new Exception(
                    "Не найдено семейство для информации о краске.\n\n" +
                    "Создайте семейство категории 'Pipe Fittings' с именем 'Краска' и следующими параметрами:\n" +
                    "- ADSK_Наименование (текстовый)\n" +
                    "- ADSK_Техническая характеристика (текстовый)\n" +
                    "- ADSK_Примечание (текстовый)");
            }

            if (!paintFamilySymbol.IsActive)
                paintFamilySymbol.Activate();

            XYZ insertionPoint = GetInsertionPoint(doc);

            FamilyInstance instance = doc.Create.NewFamilyInstance(
                insertionPoint,
                paintFamilySymbol,
                StructuralType.NonStructural);

            Parameter nameParam = instance.LookupParameter("ADSK_Наименование");
            if (nameParam != null && !nameParam.IsReadOnly)
            {
                nameParam.Set(paintName);
            }

            Parameter techParam = instance.LookupParameter("ADSK_Техническая характеристика");
            if (techParam != null && !techParam.IsReadOnly)
            {
                techParam.Set($"{consumptionPerM2:F3} кг/м²");
            }

            Parameter noteParam = instance.LookupParameter("ADSK_Примечание");
            if (noteParam != null && !noteParam.IsReadOnly)
            {
                noteParam.Set($"{totalConsumption:F2} кг");
            }

            return instance;
        }

        private XYZ GetInsertionPoint(Document doc)
        {
            // Всегда ставим в базовую точку проекта (Project Base Point)
            XYZ projectBasePoint = GetProjectBasePoint(doc);

            if (projectBasePoint != null)
            {
                // Ставим кубик в базовую точку + смещение вверх, чтобы было видно
                return projectBasePoint + new XYZ(0, 0, 3);
            }

            // Это не должно произойти, но на всякий случай
            return XYZ.Zero;
        }

        /// <summary>
        /// Получение координат базовой точки проекта (Project Base Point)
        /// </summary>
        private XYZ GetProjectBasePoint(Document doc)
        {
            try
            {
                // Ищем базовую точку проекта
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var basePoints = collector
                    .OfClass(typeof(BasePoint))
                    .Cast<BasePoint>()
                    .Where(bp => bp.IsShared == false) // Project Base Point (не Survey Point)
                    .ToList();

                if (basePoints.Count > 0)
                {
                    BasePoint projectBasePoint = basePoints.First();

                    // Получаем координаты базовой точки
                    LocationPoint location = projectBasePoint.Location as LocationPoint;
                    if (location != null)
                    {
                        return location.Point;
                    }
                }

                // Если не нашли Project Base Point, пробуем найти Survey Point
                var surveyPoints = collector
                    .OfClass(typeof(BasePoint))
                    .Cast<BasePoint>()
                    .Where(bp => bp.IsShared == true) // Survey Point
                    .ToList();

                if (surveyPoints.Count > 0)
                {
                    BasePoint surveyPoint = surveyPoints.First();
                    LocationPoint location = surveyPoint.Location as LocationPoint;
                    if (location != null)
                    {
                        return location.Point;
                    }
                }
            }
            catch
            {
                // Если что-то пошло не так - возвращаем null
            }

            return null;
        }

        private FamilySymbol FindPaintFamilySymbol(Document doc)
        {
            var familySymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                            fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting &&
                            fs.FamilyName.Contains("Краска"))
                .ToList();

            return familySymbols.FirstOrDefault();
        }

        #endregion
    }

    // Фильтр выбора
    public class PipingSystemSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is Pipe)
                return true;

            if (elem is FamilyInstance)
            {
                FamilyInstance fi = elem as FamilyInstance;
                if (fi.Category != null && fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting)
                    return true;
            }

            return false;
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return true;
        }
    }
}