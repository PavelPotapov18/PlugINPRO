using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PlugINPRO.UI.Windows.IFC;


namespace PlugINPRO.Commands.IFC
{
    [Transaction(TransactionMode.Manual)]
    internal class IFCPaintCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            View currentView = doc.ActiveView;

            if (currentView.ViewTemplateId != ElementId.InvalidElementId)
            {
                View template = doc.GetElement(currentView.ViewTemplateId) as View;

                if (template == null)
                    return Result.Cancelled;

                ElementId filtersParamId = new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS);

                // Все параметры, контролируемые шаблоном
                ICollection<ElementId> controlledParams =
                    template.GetTemplateParameterIds();

                // Параметры, НЕ контролируемые шаблоном
                ICollection<ElementId> nonControlledParams =
                    template.GetNonControlledTemplateParameterIds();

                bool filtersControlled =
                    controlledParams.Contains(filtersParamId) &&
                    !nonControlledParams.Contains(filtersParamId);

                if (!filtersControlled)
                {
                    TaskDialog.Show("Info", "Фильтры не управляются шаблоном.");
                }

                using (Transaction t = new Transaction(doc, "Отключить управление фильтрами"))
                {
                    t.Start();

                    List<ElementId> updatedNonControlled =
                        nonControlledParams.ToList();

                    updatedNonControlled.Add(filtersParamId);

                    template.SetNonControlledTemplateParameterIds(updatedNonControlled);

                    t.Commit();
                }

            }


            string viewName = currentView.Name
                .Replace("{", "")
                .Replace("}", "");

            IFCPaintWindow settingsWindow = new IFCPaintWindow(doc);

            if (settingsWindow.ShowDialog() != true)
                return Result.Cancelled;

            Color selectedColor = settingsWindow.SelectedColor;
            string filterName = settingsWindow.FilterName;

            FillPatternElement solidFillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            if (solidFillPattern == null)
            {
                TaskDialog.Show("Ошибка", "Не найдена сплошная заливка.");
                return Result.Failed;
            }

            ElementId solidLinePatternId = LinePatternElement.GetSolidPatternId();


            if (solidLinePatternId == null)
            {
                TaskDialog.Show("Ошибка", "Не найдена сплошная линия.");
                return Result.Failed;
            }

            try
            {
                IList<Reference> pickedRefs =
                    uidoc.Selection.PickObjects(
                        ObjectType.LinkedElement,
                        new IfcLinkSelectionFilter(),
                        "Выберите элементы из IFC связи");


                List<Element> linkedElements = new List<Element>();
                List<ElementId> linkedElementsCategories = new List<ElementId>();

                foreach (Reference reference in pickedRefs)
                {
                    RevitLinkInstance linkInstance =
                        doc.GetElement(reference.ElementId) as RevitLinkInstance;

                    if (linkInstance == null)
                        continue;

                    Document linkDoc = linkInstance.GetLinkDocument();

                    if (linkDoc == null)
                        continue;

                    Element linkedElement =
                        linkDoc.GetElement(reference.LinkedElementId);

                    if (linkedElement != null && linkedElement.Category != null)
                    {
                        linkedElements.Add(linkedElement);
                        linkedElementsCategories.Add(linkedElement.Category.Id);
                    }
                }
                linkedElementsCategories = linkedElementsCategories.Distinct().ToList();

                TaskDialog.Show(
                    "IFC Selection",
                    $"Выбрано элементов: {linkedElements.Count}");

                IList<ElementFilter> filters = new List<ElementFilter>();


                foreach (Element linkedElement in linkedElements)
                {
                    Parameter IfcGuid = linkedElement.get_Parameter(BuiltInParameter.IFC_GUID);
                    if (IfcGuid == null || !IfcGuid.HasValue)
                        continue;
                    string IfcGuidText = IfcGuid.AsString();

                    ElementId paramId = IfcGuid.Definition is InternalDefinition internalDef
                        ? internalDef.Id
                        : ElementId.InvalidElementId;

                    ParameterValueProvider provider = new ParameterValueProvider(paramId);

                    FilterRule rule = new FilterStringRule(
                        provider,
                        new FilterStringContains(),
                        IfcGuidText);

                    IList<FilterRule> rules = new List<FilterRule> { rule };

                    ElementParameterFilter elementFilter = new ElementParameterFilter(rules);

                    filters.Add(elementFilter);
                }

                using (Transaction t = new Transaction(doc, "Создание фильтра IFC элементов"))
                {
                    t.Start();

                    LogicalOrFilter OrRuleSet = new LogicalOrFilter(filters);

                    ParameterFilterElement paintFilter =
                    ParameterFilterElement.Create(
                        doc,
                        filterName,
                        linkedElementsCategories,
                        OrRuleSet);

                    currentView.AddFilter(paintFilter.Id);
                    currentView.SetFilterVisibility(paintFilter.Id, true);

                    OverrideGraphicSettings filterSettings = new OverrideGraphicSettings();

                    if (currentView is View3D)
                    {
                        SetSurfaceProjectionGraphics(
                            ref filterSettings,
                            solidFillPattern,
                            solidLinePatternId,
                            selectedColor);
                    }

                    else
                    {
                        SetSurfaceProjectionGraphics(
                            ref filterSettings,
                            solidFillPattern,
                            solidLinePatternId,
                            selectedColor);

                        SetCutGraphics(
                            ref filterSettings,
                            solidFillPattern,
                            solidLinePatternId,
                            selectedColor);
                    }

                    currentView.SetFilterOverrides(paintFilter.Id, filterSettings);
                    t.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        public void SetSurfaceProjectionGraphics(
            ref OverrideGraphicSettings filterSettings,
            FillPatternElement fillPattern,
            ElementId linePatternId,
            Color color)
        {
            filterSettings.SetSurfaceForegroundPatternId(fillPattern.Id);
            filterSettings.SetSurfaceForegroundPatternColor(color);
            filterSettings.SetSurfaceBackgroundPatternVisible(false);
            filterSettings.SetProjectionLinePatternId(linePatternId);
            filterSettings.SetProjectionLineColor(color);
            filterSettings.SetProjectionLineWeight(3);
        }

        public void SetCutGraphics(
            ref OverrideGraphicSettings filterSettings,
            FillPatternElement fillPattern,
            ElementId linePatternId,
            Color color)
        {
            filterSettings.SetCutBackgroundPatternVisible(false);
            filterSettings.SetCutForegroundPatternId(fillPattern.Id);
            filterSettings.SetCutForegroundPatternColor(color);
            filterSettings.SetCutLinePatternId(linePatternId);
            filterSettings.SetCutLineColor(color);
            filterSettings.SetCutLineWeight(3);
        }
        public class IfcLinkSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is RevitLinkInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

    }
}